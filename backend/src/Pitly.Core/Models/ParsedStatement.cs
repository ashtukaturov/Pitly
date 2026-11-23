namespace Pitly.Core.Models;

public record ParsedStatement(
    List<Trade> Trades,
    List<RawDividend> Dividends,
    List<RawWithholdingTax> WithholdingTaxes,
    List<CorporateAction>? CorporateActions = null,
    List<CarryInPosition>? CarryInPositions = null,
    int? StatementYear = null,
    // Things the user should review: corporate actions handled with a conservative assumption, rows ignored, etc.
    List<string>? Warnings = null,
    // Broker account the statement belongs to (IB "Account Information"). Null when the export carries no account
    // number (Trading 212): that does not prove two such files belong to one account, so the user can label them.
    string? AccountId = null,
    // Broker name for messages and for telling apart statements without an account number
    string? Broker = null,
    // Structured findings of the parser (rows it left out, control totals that do not agree); the same facts as
    // the matching Warnings strings, with a code the UI can translate
    List<TaxDiagnostic>? Diagnostics = null,
    // Name and version of the parser that read the file, kept with the session for provenance
    string? ParserVersion = null);

/// <summary>
/// Kind of flat-tax (art. 30a) income. All kinds are taxed at 19% and reported in the same PIT-38 field,
/// but they are kept apart so the user can see and verify each stream separately.
/// </summary>
public enum IncomeKind
{
    /// <summary>Ordinary cash dividend (art. 30a ust. 1 pkt 4).</summary>
    Dividend,
    /// <summary>
    /// Substitute payment received while shares were lent out (IB "Payment In Lieu Of Dividends").
    /// Treated here like a dividend-equivalent flat-tax income; its exact classification is not settled,
    /// so it is flagged in the UI.
    /// </summary>
    PaymentInLieu,
    /// <summary>Interest on cash balances or securities lending (art. 30a ust. 1 pkt 3).</summary>
    Interest,
    /// <summary>
    /// Only for withholding rows: the statement does not say what income the tax was withheld on. Such a row is
    /// never matched to a payment; it is reported as unresolved.
    /// </summary>
    Unknown
}

/// <summary>
/// One payment of dividend, interest or payment in lieu as the statement reports it, in the broker's currency.
/// Dates, identifiers and the raw description are kept so that the attribution of withholding can be explained.
/// </summary>
public record RawDividend(
    string Symbol,
    string Currency,
    // Date of the income (the pay date the broker reports); the NBP rate is taken from the day before
    DateTime Date,
    decimal Amount,
    string? Isin = null,
    IncomeKind Kind = IncomeKind.Dividend,
    // Account the payment belongs to; stamped by StatementMerger so matching and FIFO never cross accounts
    string? AccountId = null,
    // Broker's own id of the operation (Trading 212 "ID", Flex "TransactionID"), used to recognise the same row in
    // overlapping exports
    string? TransactionId = null,
    // Tax withheld on this very payment when the broker reports it in the same row (Trading 212). The link is
    // kept instead of re-matching by date, so two payments of one day cannot swap or share their withholding.
    decimal? WithheldAmount = null,
    string? WithheldCurrency = null,
    // Source country named by the statement (generic CSV "country"), used for PIT/ZG instead of the ISIN prefix
    string? CountryCode = null,
    // Broker's id of the corporate event (IB Flex "ActionID"); withholding rows with the same id belong to it
    string? ActionId = null,
    // Reference the broker prints for income without an instrument, e.g. the period of credit interest
    // ("JAN-2025"); withholding on that interest carries the same reference
    string? Reference = null,
    // Day the broker booked the payment, when the statement gives one separate from the income date
    DateTime? BookingDate = null,
    // Where the row comes from: the uploaded file (stamped by the import) and the broker's own type and text
    string? SourceFile = null,
    string? RawType = null,
    string? RawDescription = null);

/// <summary>
/// One withholding event as the statement reports it. A positive <see cref="Amount"/> is tax withheld, a
/// negative one is tax refunded (a correction, a reclassification, a reversal). <see cref="Date"/> is the date
/// the broker refers the event to, normally the pay date of the income it belongs to; <see cref="BookingDate"/>
/// and <see cref="SourceStatementYear"/> say when it was actually booked, which can be a later year.
/// </summary>
public record RawWithholdingTax(
    string Symbol,
    string Currency,
    DateTime Date,
    decimal Amount,
    string? Isin = null,
    IncomeKind Kind = IncomeKind.Dividend,
    string? AccountId = null,
    string? TransactionId = null,
    string? ActionId = null,
    // Reference shared with the income it was withheld on (the period of credit interest)
    string? Reference = null,
    // Day the tax was actually paid or refunded, when the statement gives it (IB Flex "Date/Time")
    DateTime? BookingDate = null,
    // Year of the statement the row was read from; an Activity Statement books next year's corrections dated back
    // to the dividend, so this is the only sign that the money moved in another year
    int? SourceStatementYear = null,
    string? SourceFile = null,
    string? RawType = null,
    string? RawDescription = null)
{
    /// <summary>The day whose exchange rate values the event (art. 11a ust. 3: the day the tax was paid).</summary>
    public DateTime EffectiveDate => BookingDate ?? Date;

    /// <summary>The year the money actually moved, when the statement tells it.</summary>
    public int? BookingYear => BookingDate?.Year ?? SourceStatementYear;
}

public enum CorporateActionType
{
    /// <summary>Numerator new shares for Denominator old. Lots are scaled; if the ISIN changed they are re-keyed too.</summary>
    StockSplit,
    /// <summary>CUSIP/ISIN or ticker change: lots move to the new identifier unchanged.</summary>
    IsinChange,
    /// <summary>
    /// Shares exchanged for shares of another company at Numerator:Denominator. Not a taxable event for the
    /// shareholder; the acquisition cost of the old shares carries over to the new ones (art. 24 ust. 8 updof).
    /// Any cash component is reported as proceeds with zero cost (conservative).
    /// </summary>
    StockMerger,
    /// <summary>Position taken out for cash (acquisition, tender, buy-back): treated as a sale of all shares.</summary>
    CashMerger,
    /// <summary>
    /// Shares of another company received for free. Added as a lot with zero acquisition cost: the parent keeps
    /// its full cost and the whole sale price of the spun-off shares is taxed when they are sold (conservative).
    /// </summary>
    SpinOff,
    /// <summary>Extra shares of the same company received for free: zero-cost lot (conservative).</summary>
    StockDividend,
    /// <summary>Cash paid instead of a fractional share: a sale of that fraction (or pure proceeds if the fraction is unknown).</summary>
    CashInLieu
}

/// <summary>
/// A corporate action affecting FIFO lots. <see cref="Symbol"/>/<see cref="Isin"/> identify the resulting
/// (target) instrument; <see cref="SourceSymbol"/>/<see cref="SourceIsin"/> identify where the shares came
/// from when the identifier changed (null means same as target).
/// </summary>
public record CorporateAction(
    string Symbol,
    DateTime DateTime,
    CorporateActionType Type,
    decimal Numerator,
    decimal Denominator,
    string? Isin = null,
    string? SourceSymbol = null,
    string? SourceIsin = null,
    // Shares received (SpinOff, StockDividend), removed (CashMerger) or the fraction paid out (CashInLieu)
    decimal Quantity = 0m,
    // Cash received (CashMerger, CashInLieu, cash part of a StockMerger) in Currency
    decimal CashProceeds = 0m,
    string? Currency = null,
    string? Description = null,
    // Account whose position the action changes; stamped by StatementMerger
    string? AccountId = null,
    // Broker's id of the event (IB Flex "ActionID"), used to recognise the same action in two formats
    string? ActionId = null)
{
    public decimal Factor => Denominator == 0 ? 0 : Numerator / Denominator;
    public string EffectiveSourceSymbol => string.IsNullOrWhiteSpace(SourceSymbol) ? Symbol : SourceSymbol;
    public string? EffectiveSourceIsin => string.IsNullOrWhiteSpace(SourceIsin) ? Isin : SourceIsin;
}

public record CarryInPosition(
    string Symbol,
    decimal Quantity,
    int Year,
    string? Isin = null,
    string? AccountId = null);
