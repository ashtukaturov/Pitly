namespace Pitly.Core.Models;

public enum WithholdingEventStatus
{
    /// <summary>Attributed to exactly one payment.</summary>
    Matched,
    /// <summary>Attributed to the payments of one distribution event in proportion to their gross amounts.</summary>
    Shared,
    /// <summary>Not attributed; the amount is not in the declared figures and a diagnostic says why.</summary>
    Unresolved
}

/// <summary>
/// A withholding event as it was read from the statement and what the calculation did with it. Stored with the
/// session so that the attribution can be explained and audited after the fact. Nothing here identifies a
/// person: the raw description is the broker's text for the row.
/// </summary>
public record WithholdingEvent(
    string? AccountId,
    string Symbol,
    string? Isin,
    string Currency,
    IncomeKind Kind,
    // Signed in the broker's currency: positive = withheld, negative = refunded
    decimal Amount,
    // Date the broker refers the event to (the income date)
    DateTime Date,
    DateTime? BookingDate,
    int? SourceStatementYear,
    string? TransactionId,
    string? ActionId,
    string? Reference,
    string? SourceFile,
    string? RawType,
    string? RawDescription,
    WithholdingEventStatus Status,
    // Symbol and date of the payment the event was attributed to ("AAPL 2025-05-15"), for display
    string? MatchedPayment,
    // Value of the event in PLN at the rate of its own day
    decimal AmountPln,
    // Part of the event that is not in the declared amounts (the whole event when unresolved, the excess of a refund)
    decimal UnresolvedAmountPln,
    string? DiagnosticCode);
