using System.Globalization;
using Pitly.Core.Models;
using Pitly.Core.Services;

namespace Pitly.Core.Tax;

public interface ICapitalGainsTaxCalculator
{
    /// <summary>
    /// Computes FIFO results; when <paramref name="warnings"/> is given, consistency checks are appended to it, and
    /// when <paramref name="diagnostics"/> is given, findings about single trades (a sale whose settlement falls
    /// in the next year) are appended to it. <paramref name="basis"/> is the date that sets a trade's tax year and
    /// rate (see <see cref="DatePolicy"/>).
    /// </summary>
    Task<List<TradeResult>> CalculateAsync(
        ParsedStatement statement,
        TaxPeriod taxPeriod,
        List<string>? warnings = null,
        List<TaxDiagnostic>? diagnostics = null,
        TradeDateBasis basis = TradeDateBasis.SettlementDate);
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
        public required TradeDateBasis Basis { get; init; }
        public required IReadOnlyDictionary<string, decimal> CarryInByKey { get; init; }
        // Trades of the period dated by the trade date only because their statement gives no settlement date
        public int UndatedSettlements { get; set; }
        public List<TradeResult> Results { get; } = [];
        public Dictionary<string, LinkedList<Lot>> BuyLots { get; } = new(StringComparer.OrdinalIgnoreCase);
        // Old lot key -> current key after a split with a new ISIN, an ISIN change or a merger moved the lots.
        // Statements often keep mapping the ticker to the old ISIN for a while, so later trades still resolve.
        public Dictionary<string, string> KeyAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        // Lot key -> last symbol seen for it, for messages
        public Dictionary<string, string> KeySymbols { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? Warnings { get; init; }
        public List<TaxDiagnostic>? Diagnostics { get; init; }
        public bool OpeningPositionsChecked { get; set; }
        // Text such as " on account U111" appended to messages when the upload spans several accounts
        public string AccountSuffix { get; init; } = string.Empty;
    }

    private const decimal PositionTolerance = 0.001m;

    /// <summary>Start of every opening-position warning; such warnings are repeated on PIT-38 and in the printout.</summary>
    public const string PositionCheckPrefix = "Position check for ";

    /// <summary>
    /// Every broker account has its own FIFO queues: a purchase on one account can never be the cost of a sale on
    /// another, and a split or rename reported by both statements changes each position exactly once. The accounts
    /// are calculated one after another and their results are combined afterwards.
    /// </summary>
    public async Task<List<TradeResult>> CalculateAsync(
        ParsedStatement statement,
        TaxPeriod taxPeriod,
        List<string>? warnings = null,
        List<TaxDiagnostic>? diagnostics = null,
        TradeDateBasis basis = TradeDateBasis.SettlementDate)
    {
        var carryIns = statement.CarryInPositions ?? [];
        var actions = statement.CorporateActions ?? [];
        var accounts = statement.Trades.Select(t => AccountKey(t.AccountId))
            .Concat(actions.Select(a => AccountKey(a.AccountId)))
            .Concat(carryIns.Select(p => AccountKey(p.AccountId)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var undated = 0;
        List<TradeResult> results;
        if (accounts.Count <= 1)
        {
            results = await CalculateAccountAsync(statement.Trades, actions, carryIns, taxPeriod, basis, warnings, diagnostics, accountSuffix: string.Empty, count => undated += count);
        }
        else
        {
            results = [];
            foreach (var account in accounts)
            {
                results.AddRange(await CalculateAccountAsync(
                    statement.Trades.Where(t => SameAccount(t.AccountId, account)).ToList(),
                    actions.Where(a => SameAccount(a.AccountId, account)).ToList(),
                    carryIns.Where(p => SameAccount(p.AccountId, account)).ToList(),
                    taxPeriod,
                    basis,
                    warnings,
                    diagnostics,
                    accountSuffix: account.Length == 0 ? " on the account without a number" : " on account " + account,
                    count => undated += count));
            }

            // Stable sort: rows of one account keep their timeline order.
            results = results.OrderBy(r => r.DateTime).ToList();
        }

        if (undated > 0 && basis == TradeDateBasis.SettlementDate)
        {
            diagnostics?.Add(new TaxDiagnostic(DiagnosticCodes.SettlementDateMissing, DiagnosticSeverity.Info,
                $"{undated} {(undated == 1 ? "trade" : "trades")} of {taxPeriod.Year} come from a statement that gives no settlement date (the Activity Statement, Trading 212): " +
                "they are dated and converted by the trade date. By the settlement date the rate is that of a day or two later. " +
                "The Flex Query of an IB account carries the settlement date of every trade.",
                AffectedYears: [taxPeriod.Year], Decision: DecisionTopics.DateBasis));
        }

        return results;
    }

    /// <summary>
    /// The day that sets the trade's tax year and its rate: the settlement date when that is the basis and the
    /// statement gives it, otherwise the trade date (see <see cref="DatePolicy"/>).
    /// </summary>
    private static DateTime RecognitionDate(Trade trade, TradeDateBasis basis)
        => basis == TradeDateBasis.SettlementDate && trade.SettleDate is { } settle ? settle.Date : trade.DateTime;

    private static string AccountKey(string? accountId) => accountId?.Trim() ?? string.Empty;

    private static bool SameAccount(string? accountId, string account)
        => string.Equals(AccountKey(accountId), account, StringComparison.OrdinalIgnoreCase);

    private async Task<List<TradeResult>> CalculateAccountAsync(
        IReadOnlyList<Trade> trades,
        IReadOnlyList<CorporateAction> actions,
        IReadOnlyList<CarryInPosition> carryIns,
        TaxPeriod taxPeriod,
        TradeDateBasis basis,
        List<string>? warnings,
        List<TaxDiagnostic>? diagnostics,
        string accountSuffix,
        Action<int> reportUndated)
    {
        var ctx = new Context
        {
            TaxPeriod = taxPeriod,
            Basis = basis,
            Warnings = warnings,
            Diagnostics = diagnostics,
            AccountSuffix = accountSuffix,
            CarryInByKey = carryIns
                .Where(p => p.Year == taxPeriod.Year)
                .GroupBy(GetLotKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity), StringComparer.OrdinalIgnoreCase)
        };

        // Earlier years are replayed to rebuild the FIFO lots. Anything after the end of the selected period cannot
        // change its result, so it is not replayed: a later sale with incomplete history must not block this year.
        var events = trades
            .Select(trade => new TimelineEvent(trade.DateTime, trade, null))
            .Concat(actions.Select(action => new TimelineEvent(action.DateTime, null, action)))
            .Where(e => e.DateTime.Date <= taxPeriod.TaxableTo)
            .OrderBy(e => e.DateTime)
            .ThenBy(e => e.Trade is null ? 0 : 1)
            .ToList();

        foreach (var position in carryIns.Where(p => p.Year == taxPeriod.Year))
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

        reportUndated(ctx.UndatedSettlements);
        return ctx.Results;
    }

    private async Task ProcessTradeAsync(Trade trade, Context ctx, string? note)
    {
        // The decided date (the settlement date by default, the trade date where the statement gives none or the
        // user chose it) sets the tax year and the rate: the NBP table of the last business day before it, art. 11a
        // ust. 1-2. The FIFO order stays the order of execution. See DatePolicy.
        var recognised = RecognitionDate(trade, ctx.Basis);
        var quote = await _rateService.GetQuoteAsync(trade.Currency, recognised);
        var rate = quote.Rate;
        var inPeriod = ctx.TaxPeriod.IncludesDate(recognised);
        if (inPeriod && trade.SettleDate is null && note is null)
            ctx.UndatedSettlements++;
        var lotKey = ResolveAlias(ctx, GetLotKey(trade));
        ctx.KeySymbols[lotKey] = trade.Symbol;
        var lots = GetOrCreateLots(ctx.BuyLots, lotKey);

        if (trade.Type == TradeType.Buy)
        {
            var costPerSharePln = trade.Price * rate;
            var commissionPln = await GetCommissionPlnAsync(trade, rate, recognised);
            var commissionPerSharePln = trade.Quantity > 0 ? commissionPln / trade.Quantity : 0m;
            lots.AddLast(new Lot(trade.Quantity, costPerSharePln, commissionPerSharePln));

            var totalBuyCostPln = trade.Quantity * costPerSharePln + commissionPln;

            if (inPeriod)
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
                    Note: note,
                    Isin: trade.Isin,
                    CountryCode: trade.CountryCode,
                    SettleDate: trade.SettleDate,
                    RateDate: quote.EffectiveDate));
            }

            return;
        }

        if (note is null)
            CheckRecognitionYear(trade, ctx);

        // Przychod (poz. 22) is the gross sale value; the sell commission is a koszt uzyskania
        // przychodu (art. 22 ust. 1 / art. 23 ust. 1 pkt 38 ustawy o PIT) and belongs in poz. 23.
        var proceedsPln = trade.Proceeds * rate;
        var sellCommissionPln = await GetCommissionPlnAsync(trade, rate, recognised);

        decimal totalCostPln = 0;
        var remainingQty = trade.Quantity;

        if (lots.Count == 0)
            throw new InvalidOperationException(BuildMissingLotsMessage(trade, ctx.TaxPeriod.Year, ctx.CarryInByKey, ctx.AccountSuffix));

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
                ctx.CarryInByKey,
                ctx.AccountSuffix));

        var totalCostWithSellCommissionPln = totalCostPln + sellCommissionPln;
        var gainLoss = proceedsPln - totalCostWithSellCommissionPln;

        if (inPeriod)
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
                Note: note,
                Isin: trade.Isin,
                CountryCode: trade.CountryCode,
                SettleDate: trade.SettleDate,
                RateDate: quote.EffectiveDate));
        }
    }

    /// <summary>
    /// A sale executed in one year that settles in the next belongs to the first year by its trade date and to the
    /// second by the day ownership passes (art. 17 ust. 1ab pkt 1 ustawy o PIT read with art. 7 ust. 2 ustawy o
    /// obrocie instrumentami finansowymi). The user's date decision settles which: the sale is counted in that
    /// year and a note for both years says where it went, so that a return of the other year (perhaps filed under
    /// the other reading) does not count it again. Without a settlement date in the statement, a sale in the last
    /// days of December may settle after the year end under T+1 or T+2: by the settlement date its year is then not
    /// known, and the finding blocks filing until the statement that gives the date is uploaded or the trade date
    /// is chosen.
    /// </summary>
    private static void CheckRecognitionYear(Trade trade, Context ctx)
    {
        if (ctx.Diagnostics is null)
            return;
        var tradeYear = trade.DateTime.Year;
        var quantity = trade.Quantity.ToString("G29", CultureInfo.InvariantCulture);
        if (trade.SettleDate is { } settle)
        {
            if (settle.Year == tradeYear || (ctx.TaxPeriod.Year != tradeYear && ctx.TaxPeriod.Year != settle.Year))
                return;
            var bySettlement = ctx.Basis == TradeDateBasis.SettlementDate;
            var counted = bySettlement ? settle.Year : tradeYear;
            var other = bySettlement ? tradeYear : settle.Year;
            ctx.Diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.RecognitionDate, DiagnosticSeverity.Info,
                $"Sale of {quantity} {trade.Symbol} executed on {trade.DateTime:yyyy-MM-dd}{ctx.AccountSuffix} settles on {settle:yyyy-MM-dd}: " +
                (bySettlement
                    ? $"by the settlement date (your decision; art. 17 ust. 1ab pkt 1 ustawy o PIT: the entry on the securities account) it is counted in {counted}, at the rate of the day before {settle:yyyy-MM-dd}. "
                    : $"by the trade date (your decision) it is counted in {counted}, at the rate of the day before {trade.DateTime:yyyy-MM-dd}; by the settlement date it would belong to {settle.Year}. ") +
                $"It is not in the {other} amounts: if a return for {other} already counted it under the other reading, it must not be counted twice.",
                AccountId: trade.AccountId, AffectedYears: new[] { tradeYear, settle.Year }.OrderBy(y => y).ToList(), Decision: DecisionTopics.DateBasis));
            return;
        }

        if (trade.DateTime.Month != 12 || trade.DateTime.Day < 29 || (ctx.TaxPeriod.Year != tradeYear && ctx.TaxPeriod.Year != tradeYear + 1))
            return;
        if (ctx.Basis == TradeDateBasis.TradeDate)
        {
            ctx.Diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.RecognitionDate, DiagnosticSeverity.Info,
                $"Sale of {quantity} {trade.Symbol} executed on {trade.DateTime:yyyy-MM-dd}{ctx.AccountSuffix} is counted in {tradeYear} by the trade date (your decision). " +
                $"It probably settles in {tradeYear + 1} (T+1 or T+2); it is not in the {tradeYear + 1} amounts and must not be counted there again.",
                AccountId: trade.AccountId, AffectedYears: [tradeYear, tradeYear + 1], Decision: DecisionTopics.DateBasis));
            return;
        }

        ctx.Diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.RecognitionDate, DiagnosticSeverity.Warning,
            $"Sale of {quantity} {trade.Symbol} executed on {trade.DateTime:yyyy-MM-dd}{ctx.AccountSuffix}: the statement gives no settlement date, " +
            $"and a sale in the last days of December settles in {tradeYear + 1} under T+1 or T+2. By the settlement date (your decision) its year is not known; " +
            $"it is counted in {tradeYear} by the trade date for now. Upload the Flex Query of the account (its rows carry the settlement date), " +
            "or choose the trade date for every year.",
            AccountId: trade.AccountId, AffectedYears: [tradeYear, tradeYear + 1], AffectsAmounts: true, Decision: DecisionTopics.DateBasis));
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
            Note: note + " (no matching lots, zero cost)",
            Isin: isCashInLieu ? action.EffectiveSourceIsin : action.Isin));
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
                PositionCheckPrefix + "{0}: {1} shares of {2} were reconstructed from the uploaded trades{4}, but the statement reports {3} shares at the start of {0}. A statement for a year with trades or a stock split is probably missing, or a corporate action was not recognised; the cost basis of {2} sales may be wrong.",
                year, Fmt(have), symbol, Fmt(priorQuantity), ctx.AccountSuffix));
        }

        foreach (var (key, have) in reconstructed.OrderBy(kv => kv.Key))
        {
            if (have <= PositionTolerance || matched.Contains(key))
                continue;

            var symbol = ctx.KeySymbols.TryGetValue(key, out var s) ? s : key;
            ctx.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                PositionCheckPrefix + "{0}: {1} shares of {2} were reconstructed from earlier statements{3}, but the {0} statement shows no such position at the start of the year. A sale or corporate action in a missing year may have closed it.",
                year, Fmt(have), symbol, ctx.AccountSuffix));
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

    private async Task<decimal> GetCommissionPlnAsync(Trade trade, decimal tradeRate, DateTime recognised)
    {
        if (trade.Commission == 0)
            return 0;

        var commissionRate = trade.CommissionCurrency.Equals(trade.Currency, StringComparison.OrdinalIgnoreCase)
            ? tradeRate
            : await _rateService.GetRateAsync(trade.CommissionCurrency, recognised);

        return trade.Commission * commissionRate;
    }

    private static string BuildMissingLotsMessage(
        Trade trade,
        int targetYear,
        IReadOnlyDictionary<string, decimal> carryInByKey,
        string accountSuffix)
    {
        if (carryInByKey.TryGetValue(GetLotKey(trade), out var carryInQuantity) && carryInQuantity > 0)
        {
            return
                $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}{accountSuffix}: " +
                $"this sale depends on {carryInQuantity} shares carried into {targetYear} from earlier statements. " +
                "Upload prior-year CSVs so Pitly can reconstruct the original FIFO buy lots.";
        }

        return
            $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}{accountSuffix}: no buy lots available. " +
            "Upload earlier statements so Pitly can reconstruct the original FIFO buy lots.";
    }

    private static string BuildPartialLotsMessage(
        Trade trade,
        decimal availableQuantity,
        int targetYear,
        IReadOnlyDictionary<string, decimal> carryInByKey,
        string accountSuffix)
    {
        if (carryInByKey.TryGetValue(GetLotKey(trade), out var carryInQuantity) && carryInQuantity > 0)
        {
            return
                $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}{accountSuffix}: " +
                $"only {availableQuantity} shares were reconstructed from uploaded statements, and this position carried {carryInQuantity} shares into {targetYear}. " +
                "Upload prior-year CSVs so Pitly can reconstruct the missing FIFO buy lots.";
        }

        return
            $"Cannot sell {trade.Quantity} shares of {trade.Symbol} on {trade.DateTime:yyyy-MM-dd}{accountSuffix}: " +
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
