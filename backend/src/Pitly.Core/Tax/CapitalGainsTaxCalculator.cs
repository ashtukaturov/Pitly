using System.Globalization;
using Pitly.Core.Models;
using Pitly.Core.Services;

namespace Pitly.Core.Tax;

public interface ICapitalGainsTaxCalculator
{
    /// <summary>Computes FIFO results; when <paramref name="warnings"/> is given, consistency checks are appended to it.</summary>
    Task<List<TradeResult>> CalculateAsync(ParsedStatement statement, TaxPeriod taxPeriod, List<string>? warnings = null);
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
        // Old lot key -> current key after a split with a new ISIN, an ISIN change or a merger moved the lots.
        // Statements often keep mapping the ticker to the old ISIN for a while, so later trades still resolve.
        public Dictionary<string, string> KeyAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        // Lot key -> last symbol seen for it, for messages
        public Dictionary<string, string> KeySymbols { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? Warnings { get; init; }
        public bool OpeningPositionsChecked { get; set; }
    }

    private const decimal PositionTolerance = 0.001m;

    public async Task<List<TradeResult>> CalculateAsync(ParsedStatement statement, TaxPeriod taxPeriod, List<string>? warnings = null)
    {
        var ctx = new Context
        {
            TaxPeriod = taxPeriod,
            Warnings = warnings,
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

        foreach (var position in (statement.CarryInPositions ?? []).Where(p => p.Year == taxPeriod.Year))
            ctx.KeySymbols.TryAdd(GetLotKey(position), position.Symbol);

        var yearStart = new DateTime(taxPeriod.Year, 1, 1);
        foreach (var timelineEvent in events)
        {
            // Once the timeline reaches the tax year, the lots must equal the broker's opening positions.
            if (!ctx.OpeningPositionsChecked && timelineEvent.DateTime >= yearStart)
                CheckOpeningPositions(ctx);

            if (timelineEvent.Action is not null)
            {
                await ApplyCorporateActionAsync(timelineEvent.Action, ctx);
                continue;
            }

            await ProcessTradeAsync(timelineEvent.Trade!, ctx, note: null);
        }

        if (!ctx.OpeningPositionsChecked)
            CheckOpeningPositions(ctx);

        return ctx.Results;
    }

    private async Task ProcessTradeAsync(Trade trade, Context ctx, string? note)
    {
        var rate = await _rateService.GetRateAsync(trade.Currency, trade.DateTime);
        var lotKey = ResolveAlias(ctx, GetLotKey(trade));
        ctx.KeySymbols[lotKey] = trade.Symbol;
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
        var sourceKey = ResolveAlias(ctx, ResolveExistingKey(ctx.BuyLots, action.EffectiveSourceIsin, action.EffectiveSourceSymbol));
        var targetKey = GetLotKey(action);
        ctx.KeySymbols.TryAdd(targetKey, action.Symbol);
        var currency = string.IsNullOrWhiteSpace(action.Currency) ? "USD" : action.Currency;

        switch (action.Type)
        {
            case CorporateActionType.StockSplit:
            {
                var factor = action.Factor;
                if (factor <= 0)
                    throw new InvalidOperationException(
                        $"Unsupported stock split ratio for {action.Symbol} on {action.DateTime:yyyy-MM-dd}.");
                MoveLots(ctx, sourceKey, targetKey);
                ScaleLots(ctx.BuyLots, targetKey, factor);
                break;
            }

            case CorporateActionType.IsinChange:
                MoveLots(ctx, sourceKey, targetKey);
                break;

            case CorporateActionType.StockMerger:
            {
                var factor = action.Factor;
                if (factor <= 0)
                    throw new InvalidOperationException(
                        $"Unsupported exchange ratio in merger of {action.EffectiveSourceSymbol} into {action.Symbol} on {action.DateTime:yyyy-MM-dd}.");
                MoveLots(ctx, sourceKey, targetKey);
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

    /// <summary>
    /// Compares the lots reconstructed from uploaded trades with the positions the tax-year statement reports at the
    /// start of the year (IB Mark-to-Market "Prior Quantity"). A mismatch usually means a year with trades or a stock
    /// split is missing from the upload, or a corporate action was not recognised.
    /// </summary>
    private static void CheckOpeningPositions(Context ctx)
    {
        ctx.OpeningPositionsChecked = true;
        if (ctx.Warnings is null || ctx.CarryInByKey.Count == 0)
            return;

        var year = ctx.TaxPeriod.Year;
        var reconstructed = ctx.BuyLots
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Sum(l => l.Quantity), StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, priorQuantity) in ctx.CarryInByKey.OrderBy(kv => kv.Key))
        {
            var resolved = ResolveAlias(ctx, key);
            matched.Add(resolved);
            reconstructed.TryGetValue(resolved, out var have);
            if (Math.Abs(have - priorQuantity) <= PositionTolerance)
                continue;

            var symbol = ctx.KeySymbols.TryGetValue(resolved, out var s) ? s : key;
            ctx.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "Position check for {0}: {1} shares of {2} were reconstructed from the uploaded trades, but the statement reports {3} shares at the start of {0}. A statement for a year with trades or a stock split is probably missing, or a corporate action was not recognised; the cost basis of {2} sales may be wrong.",
                year, Fmt(have), symbol, Fmt(priorQuantity)));
        }

        foreach (var (key, have) in reconstructed.OrderBy(kv => kv.Key))
        {
            if (have <= PositionTolerance || matched.Contains(key))
                continue;

            var symbol = ctx.KeySymbols.TryGetValue(key, out var s) ? s : key;
            ctx.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "Position check for {0}: {1} shares of {2} were reconstructed from earlier statements, but the {0} statement shows no such position at the start of the year. A sale or corporate action in a missing year may have closed it.",
                year, Fmt(have), symbol));
        }
    }

    private static string Fmt(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);

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

    private static string ResolveAlias(Context ctx, string key)
    {
        var guard = 0;
        while (ctx.KeyAliases.TryGetValue(key, out var next) && guard++ < 16)
            key = next;
        return key;
    }

    private static void MoveLots(Context ctx, string sourceKey, string targetKey)
    {
        if (string.Equals(sourceKey, targetKey, StringComparison.OrdinalIgnoreCase))
            return;

        // From now on anything booked under the old identifier belongs to the new one, even if no lots were open.
        ctx.KeyAliases[sourceKey] = targetKey;
        foreach (var alias in ctx.KeyAliases.Where(kv => string.Equals(kv.Value, sourceKey, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            ctx.KeyAliases[alias] = targetKey;

        if (!ctx.BuyLots.TryGetValue(sourceKey, out var source) || source.Count == 0)
            return;

        var target = GetOrCreateLots(ctx.BuyLots, targetKey);
        foreach (var lot in source)
            target.AddLast(lot);
        ctx.BuyLots.Remove(sourceKey);
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
