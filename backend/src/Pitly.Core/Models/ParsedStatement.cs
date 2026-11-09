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
    // Broker account the statement belongs to (IB "Account Information"); duplicates are only removed within one account
    string? AccountId = null);

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
    Interest
}

public record RawDividend(
    string Symbol,
    string Currency,
    DateTime Date,
    decimal Amount,
    string? Isin = null,
    IncomeKind Kind = IncomeKind.Dividend);

public record RawWithholdingTax(
    string Symbol,
    string Currency,
    DateTime Date,
    decimal Amount,
    string? Isin = null,
    IncomeKind Kind = IncomeKind.Dividend);

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
    string? Description = null)
{
    public decimal Factor => Denominator == 0 ? 0 : Numerator / Denominator;
    public string EffectiveSourceSymbol => string.IsNullOrWhiteSpace(SourceSymbol) ? Symbol : SourceSymbol;
    public string? EffectiveSourceIsin => string.IsNullOrWhiteSpace(SourceIsin) ? Isin : SourceIsin;
}

public record CarryInPosition(
    string Symbol,
    decimal Quantity,
    int Year,
    string? Isin = null);
