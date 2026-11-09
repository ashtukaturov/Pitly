using Pitly.Core.Models;
using Pitly.Core.Services;

namespace Pitly.Core.Tax;

public interface ICapitalGainsTaxCalculator
{
    Task<List<TradeResult>> CalculateAsync(ParsedStatement statement, TaxPeriod taxPeriod);
}

public class CapitalGainsTaxCalculator : ICapitalGainsTaxCalculator
{
    private readonly INbpExchangeRateService _rateService;

    public CapitalGainsTaxCalculator(INbpExchangeRateService rateService)
    {
        _rateService = rateService;
    }

    private record struct Lot(decimal Quantity, decimal CostPerSharePln, decimal CommissionPerSharePln);

    private sealed class Context
    {
        public required TaxPeriod TaxPeriod { get; init; }
        public required IReadOnlyDictionary<string, decimal> CarryInByKey { get; init; }
        public List<TradeResult> Results { get; } = [];
        public Dictionary<string, LinkedList<Lot>> BuyLots { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<TradeResult>> CalculateAsync(ParsedStatement statement, TaxPeriod taxPeriod)
    {
        var ctx = new Context
        {
            TaxPeriod = taxPeriod,
            CarryInByKey = (statement.CarryInPositions ?? [])
                .Where(p => p.Year == taxPeriod.Year)
                .GroupBy(GetLotKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity), StringComparer.OrdinalIgnoreCase)
        };

        var events = statement.Trades
            .Select(trade => new TimelineEvent(trade.DateTime, trade, null))
            .Concat((statement.CorporateActions ?? [])
                .Select(action => new TimelineEvent(action.DateTime, null, action)))
            .OrderBy(e => e.DateTime)
            .ThenBy(e => e.Trade is null ? 0 : 1)
            .ToList();

        foreach (var timelineEvent in events)
        {
            if (timelineEvent.Action is not null)
            {
                await ApplyCorporateActionAsync(timelineEvent.Action, ctx);
                continue;
            }

            await ProcessTradeAsync(timelineEvent.Trade!, ctx, note: null);
        }

        return ctx.Results;
    }

    private async Task ProcessTradeAsync(Trade trade, Context ctx, string? note)
    {
        var rate = await _rateService.GetRateAsync(trade.Currency, trade.DateTime);
        var lotKey = GetLotKey(trade);
        var lots = GetOrCreateLots(ctx.BuyLots, lotKey);

        if (trade.Type == TradeType.Buy)
        {
            var costPerSharePln = trade.Price * rate;
            var commissionPln = await GetCommissionPlnAsync(trade, rate);
            var commissionPerSharePln = trade.Quantity > 0 ? commissionPln / trade.Quantity : 0m;
            lots.AddLast(new Lot(trade.Quantity, costPerSharePln, commissionPerSharePln));

            var totalBuyCostPln = trade.Quantity * costPerSharePln + commissionPln;

            if (ctx.TaxPeriod.IncludesDate(trade.DateTime))
            {
                ctx.Results.Add(new TradeResult(
                    Symbol: trade.Symbol,
                    DateTime: trade.DateTime,
                    Type: TradeType.Buy,
                    Quantity: trade.Quantity,
                    PriceOriginal: trade.Price,
                    ProceedsOriginal: 0,
                    CommissionOriginal: trade.Commission,
                    Currency: trade.Currency,
                    ExchangeRate: rate,
                    ProceedsPln: 0,
                    CostPln: totalBuyCostPln,
                    GainLossPln: 0,
                    Note: note));
            }

            return;
        }

        // Przychod (poz. 22) is the gross sale value; the sell commission is a koszt uzyskania
        // przychodu (art. 22 ust. 1 / art. 23 ust. 1 pkt 38 ustawy o PIT) and belongs in poz. 23.
        var proceedsPln = trade.Proceeds * rate;
        var sellCommissionPln = await GetCommissionPlnAsync(trade, rate);

        decimal totalCostPln = 0;
        var remainingQty = trade.Quantity;

        if (lots.Count == 0)
            throw new InvalidOperationException(BuildMissingLotsMessage(trade, ctx.TaxPeriod.Year, ctx.CarryInByKey));

        while (remainingQty > 0 && lots.Count > 0)
        {
            var lot = lots.First!.Value;
            var usedQty = Math.Min(remainingQty, lot.Quantity);

            totalCostPln += usedQty * (lot.CostPerSharePln + lot.CommissionPerSharePln);
            remainingQty -= usedQty;

            if (usedQty >= lot.Quantity)
                lots.RemoveFirst();
            else
                lots.First!.Value = lot with { Quantity = lot.Quantity - usedQty };
        }

        if (remainingQty > 0)
            throw new InvalidOperationException(BuildPartialLotsMessage(
                trade,
                trade.Quantity - remainingQty,
                ctx.TaxPeriod.Year,
                ctx.CarryInByKey));

        var totalCostWithSellCommissionPln = totalCostPln + sellCommissionPln;
        var gainLoss = proceedsPln - totalCostWithSellCommissionPln;

        if (ctx.TaxPeriod.IncludesDate(trade.DateTime))
        {
            ctx.Results.Add(new TradeResult(
                Symbol: trade.Symbol,
                DateTime: trade.DateTime,
                Type: TradeType.Sell,
                Quantity: trade.Quantity,
                PriceOriginal: trade.Price,
                ProceedsOriginal: trade.Proceeds,
                CommissionOriginal: trade.Commission,
                Currency: trade.Currency,
                ExchangeRate: rate,
                ProceedsPln: proceedsPln,
                CostPln: totalCostWithSellCommissionPln,
                GainLossPln: gainLoss,
                Note: note));
        }
    }

    private async Task ApplyCorporateActionAsync(CorporateAction action, Context ctx)
    {
        // Lots may be keyed by ISIN or, when the statement had no instrument info, by symbol. Resolve the source
        // to whichever key actually holds lots so a split or rename still finds them.
        var sourceKey = ResolveExistingKey(ctx.BuyLots, action.EffectiveSourceIsin, action.EffectiveSourceSymbol);
        var targetKey = GetLotKey(action);
        var currency = string.IsNullOrWhiteSpace(action.Currency) ? "USD" : action.Currency;

        switch (action.Type)
        {
            case CorporateActionType.StockSplit:
            {
                var factor = action.Factor;
                if (factor <= 0)
                    throw new InvalidOperationException(
                        $"Unsupported stock split ratio for {action.Symbol} on {action.DateTime:yyyy-MM-dd}.");
                MoveLots(ctx.BuyLots, sourceKey, targetKey);
                ScaleLots(ctx.BuyLots, targetKey, factor);
                break;
            }

            case CorporateActionType.IsinChange:
                MoveLots(ctx.BuyLots, sourceKey, targetKey);
                break;

            case CorporateActionType.StockMerger:
            {
                var factor = action.Factor;
                if (factor <= 0)
                    throw new InvalidOperationException(
                        $"Unsupported exchange ratio in merger of {action.EffectiveSourceSymbol} into {action.Symbol} on {action.DateTime:yyyy-MM-dd}.");
                MoveLots(ctx.BuyLots, sourceKey, targetKey);
                ScaleLots(ctx.BuyLots, targetKey, factor);
                if (action.CashProceeds > 0)
                {
                    await RecordCashAsync(action, ctx, targetKey, currency,
                        $"Cash received in merger of {action.EffectiveSourceSymbol} into {action.Symbol} (reported with zero cost)");
                }
                break;
            }

            case CorporateActionType.CashMerger:
            {
                if (action.Quantity <= 0)
                    break;
                var sell = new Trade(
                    Symbol: action.EffectiveSourceSymbol,
                    Currency: currency,
                    DateTime: action.DateTime,
                    Quantity: action.Quantity,
                    Price: Math.Round(action.CashProceeds / action.Quantity, 6),
                    Proceeds: action.CashProceeds,
                    Commission: 0m,
                    CommissionCurrency: currency,
                    RealizedPnL: 0m,
                    Type: TradeType.Sell,
                    // Use the key that actually holds the lots (ISIN when known, otherwise the symbol)
                    Isin: string.Equals(sourceKey, action.EffectiveSourceSymbol, StringComparison.OrdinalIgnoreCase) ? null : sourceKey);
                await ProcessTradeAsync(sell, ctx, "Cash merger / acquisition: position exchanged for cash, treated as a sale");
                break;
            }

            case CorporateActionType.SpinOff:
            case CorporateActionType.StockDividend:
            {
                if (action.Quantity > 0)
                {
                    var buy = new Trade(
                        Symbol: action.Symbol,
                        Currency: currency,
                        DateTime: action.DateTime,
                        Quantity: action.Quantity,
                        Price: 0m,
                        Proceeds: 0m,
                        Commission: 0m,
                        CommissionCurrency: currency,
                        RealizedPnL: 0m,
                        Type: TradeType.Buy,
                        Isin: action.Isin);
                    var note = action.Type == CorporateActionType.SpinOff
                        ? $"Spin-off from {action.EffectiveSourceSymbol}: shares received with zero acquisition cost"
                        : "Stock dividend: shares received with zero acquisition cost";
                    await ProcessTradeAsync(buy, ctx, note);
                }

                if (action.CashProceeds > 0)
                    await RecordCashAsync(action, ctx, targetKey, currency, "Cash in lieu of fractional shares");
                break;
            }

            case CorporateActionType.CashInLieu:
                if (action.CashProceeds > 0)
                    await RecordCashAsync(action, ctx, sourceKey, currency, "Cash in lieu of fractional shares");
                break;
        }
    }

    /// <summary>
    /// Records cash received in a corporate action. When the paid-out fraction is known and lots cover it, it is a
    /// regular FIFO sale of that fraction; otherwise the cash is reported as proceeds with zero cost.
    /// </summary>
    private async Task RecordCashAsync(CorporateAction action, Context ctx, string lotKey, string currency, string note)
    {
        var available = ctx.BuyLots.TryGetValue(lotKey, out var lots) ? lots.Sum(l => l.Quantity) : 0m;
        var isCashInLieu = action.Type == CorporateActionType.CashInLieu;
        var symbol = isCashInLieu ? action.EffectiveSourceSymbol : action.Symbol;

        if (action.Quantity > 0 && available >= action.Quantity)
        {
            var sell = new Trade(
                Symbol: symbol,
                Currency: currency,
                DateTime: action.DateTime,
                Quantity: action.Quantity,
                Price: Math.Round(action.CashProceeds / action.Quantity, 6),
                Proceeds: action.CashProceeds,
                Commission: 0m,
                CommissionCurrency: currency,
                RealizedPnL: 0m,
                Type: TradeType.Sell,
                Isin: string.Equals(lotKey, symbol, StringComparison.OrdinalIgnoreCase) ? null : lotKey);
            await ProcessTradeAsync(sell, ctx, note);
            return;
        }

        if (!ctx.TaxPeriod.IncludesDate(action.DateTime))
            return;

        var rate = await _rateService.GetRateAsync(currency, action.DateTime);
        var proceedsPln = action.CashProceeds * rate;
        ctx.Results.Add(new TradeResult(
            Symbol: symbol,
            DateTime: action.DateTime,
            Type: TradeType.Sell,
            Quantity: action.Quantity,
            PriceOriginal: action.Quantity > 0 ? Math.Round(action.CashProceeds / action.Quantity, 6) : 0m,
            ProceedsOriginal: action.CashProceeds,
            CommissionOriginal: 0m,
            Currency: currency,
            ExchangeRate: rate,
            ProceedsPln: proceedsPln,
            CostPln: 0m,
            GainLossPln: proceedsPln,
            Note: note + " (no matching lots, zero cost)"));
    }

    private static LinkedList<Lot> GetOrCreateLots(Dictionary<string, LinkedList<Lot>> buyLots, string key)
    {
        if (!buyLots.TryGetValue(key, out var lots))
        {
            lots = new LinkedList<Lot>();
            buyLots[key] = lots;
        }

        return lots;
    }

    private static string ResolveExistingKey(Dictionary<string, LinkedList<Lot>> buyLots, string? isin, string symbol)
    {
        if (string.IsNullOrWhiteSpace(isin))
            return symbol;

        if (buyLots.TryGetValue(isin, out var byIsin) && byIsin.Count > 0)
            return isin;
        if (buyLots.TryGetValue(symbol, out var bySymbol) && bySymbol.Count > 0)
            return symbol;
        return isin;
    }

    private static void MoveLots(Dictionary<string, LinkedList<Lot>> buyLots, string sourceKey, string targetKey)
    {
        if (string.Equals(sourceKey, targetKey, StringComparison.OrdinalIgnoreCase))
            return;
        if (!buyLots.TryGetValue(sourceKey, out var source) || source.Count == 0)
            return;

        var target = GetOrCreateLots(buyLots, targetKey);
        foreach (var lot in source)
            target.AddLast(lot);
        buyLots.Remove(sourceKey);
    }

    private static void ScaleLots(Dictionary<string, LinkedList<Lot>> buyLots, string key, decimal factor)
    {
        if (!buyLots.TryGetValue(key, out var lots) || lots.Count == 0)
            return;

        for (var node = lots.First; node is not null; node = node.Next)
        {
            var lot = node.Value;
            node.Value = new Lot(
                Quantity: lot.Quantity * factor,
                CostPerSharePln: lot.CostPerSharePln / factor,
                CommissionPerSharePln: lot.CommissionPerSharePln / factor);
        }
    }

    private async Task<decimal> GetCommissionPlnAsync(Trade trade, decimal tradeRate)
    {
        if (trade.Commission == 0)
            return 0;

        var commissionRate = trade.CommissionCurrency.Equals(trade.Currency, StringComparison.OrdinalIgnoreCase)
            ? tradeRate
            : await _rateService.GetRateAsync(trade.CommissionCurrency, trade.DateTime);

        return trade.Commission * commissionRate;
    }

    private static string BuildMissingLotsMessage(
        Trade trade,
        int targetYear,
        IReadOnlyDictionary<string, decimal> carryInByKey)
    {
        if (carryInByKey.TryGetValue(GetLotKey(trade), out var carryInQuantity) && carryInQuantity > 0)
        {
            return
                $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}: " +
                $"this sale depends on {carryInQuantity} shares carried into {targetYear} from earlier statements. " +
                "Upload prior-year CSVs so Pitly can reconstruct the original FIFO buy lots.";
        }

        return
            $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}: no buy lots available. " +
            "Upload earlier statements so Pitly can reconstruct the original FIFO buy lots.";
    }

    private static string BuildPartialLotsMessage(
        Trade trade,
        decimal availableQuantity,
        int targetYear,
        IReadOnlyDictionary<string, decimal> carryInByKey)
    {
        if (carryInByKey.TryGetValue(GetLotKey(trade), out var carryInQuantity) && carryInQuantity > 0)
        {
            return
                $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}: " +
                $"only {availableQuantity} shares were reconstructed from uploaded statements, and this position carried {carryInQuantity} shares into {targetYear}. " +
                "Upload prior-year CSVs so Pitly can reconstruct the missing FIFO buy lots.";
        }

        return
            $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}: " +
            $"only {availableQuantity} shares available in buy lots. " +
            "Upload earlier statements so Pitly can reconstruct the missing FIFO buy lots.";
    }

    private static string GetLotKey(Trade trade) => string.IsNullOrWhiteSpace(trade.Isin)
        ? trade.Symbol
        : trade.Isin;

    private static string GetLotKey(CorporateAction action) => string.IsNullOrWhiteSpace(action.Isin)
        ? action.Symbol
        : action.Isin;

    private static string GetLotKey(CarryInPosition position) => string.IsNullOrWhiteSpace(position.Isin)
        ? position.Symbol
        : position.Isin;

    private sealed record TimelineEvent(DateTime DateTime, Trade? Trade, CorporateAction? Action);
}
