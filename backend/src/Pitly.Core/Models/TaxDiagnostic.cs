namespace Pitly.Core.Models;

public enum DiagnosticSeverity
{
    /// <summary>An explanation of what was done; the amounts do not depend on it.</summary>
    Info,
    /// <summary>Something the user has to check before filing; the amounts may be incomplete.</summary>
    Warning,
    /// <summary>Data that cannot be used as it is (an old session that must be imported again).</summary>
    Error
}

/// <summary>Stable codes of the diagnostics. The UI translates the code; the message stays English.</summary>
public static class DiagnosticCodes
{
    /// <summary>A withholding event booked in one year belongs to a payment of another year; the tax year of its effect is a policy question.</summary>
    public const string CrossYearAdjustment = "CROSS_YEAR_ADJUSTMENT";
    /// <summary>A withholding or refund row that matches no payment in the uploaded history.</summary>
    public const string UnmatchedWithholding = "UNMATCHED_WHT";
    /// <summary>A withholding row that fits several payments; it was not attributed to any of them.</summary>
    public const string AmbiguousMatch = "AMBIGUOUS_MATCH";
    /// <summary>A refund larger than the tax withheld on the payment it points at.</summary>
    public const string ExcessRefund = "EXCESS_REFUND";
    /// <summary>A statement row of a kind Pitly does not handle; it was left out.</summary>
    public const string UnsupportedRow = "UNSUPPORTED_ROW";
    /// <summary>A stored session that was saved without the data needed to rebuild its amounts exactly.</summary>
    public const string ReimportRequired = "REIMPORT_REQUIRED";
    /// <summary>A withholding row whose kind of income (dividend, interest, ...) could not be established.</summary>
    public const string UnknownIncomeKind = "UNKNOWN_INCOME_KIND";
    /// <summary>The reconstructed positions differ from the opening positions the statement reports.</summary>
    public const string PositionCheck = "POSITION_CHECK";
    /// <summary>Files without an account number were assumed to belong to one account.</summary>
    public const string AccountAssumed = "ACCOUNT_ASSUMED";
    /// <summary>Rows that another uploaded file already contributed were skipped.</summary>
    public const string OverlapSkipped = "OVERLAP_SKIPPED";
    /// <summary>One withholding row was shared between several payments of the same distribution event.</summary>
    public const string SharedWithholding = "SHARED_WITHHOLDING";
    /// <summary>Dividends and interest share one form field; each stream was rounded by its own rule.</summary>
    public const string MixedStreamRounding = "MIXED_STREAM_ROUNDING";
    /// <summary>Tax paid on a day other than the income day was converted at the rate of its own day.</summary>
    public const string RateDate = "RATE_DATE";
    /// <summary>A non-default method of limiting the foreign tax credit was used.</summary>
    public const string CreditMethod = "CREDIT_METHOD";
    /// <summary>A control total of the statement does not agree with the rows that were read from it.</summary>
    public const string SummaryMismatch = "SUMMARY_MISMATCH";
    /// <summary>Income whose legal classification Pitly does not establish (securities lending, payments in lieu).</summary>
    public const string ClassificationUnverified = "CLASSIFICATION_UNVERIFIED";
    /// <summary>Income whose classification the user decided (securities lending, payments in lieu); the finding says how it was declared.</summary>
    public const string ClassificationDecided = "CLASSIFICATION_DECIDED";
    /// <summary>Two files of one account describe the same day's operations with different amounts; the second file's rows were not added.</summary>
    public const string SourceConflict = "SOURCE_CONFLICT";
    /// <summary>The issuer's country has no treaty rate on file (or cannot be told), so the foreign tax was not credited under the treaty cap.</summary>
    public const string TreatyRateUnknown = "TREATY_RATE_UNKNOWN";
    /// <summary>Interest rounded per payment gives another figure than the same interest rounded once on the yearly total; the policy says which is used.</summary>
    public const string InterestRoundingLevel = "INTEREST_ROUNDING_LEVEL";
    /// <summary>A sale executed in one year that settles (or may settle) in the next: which year's return it belongs to is not decided by Pitly.</summary>
    public const string RecognitionDate = "RECOGNITION_DATE";
    /// <summary>Trades dated by the settlement date whose statement gives none: they were dated and converted by the trade date.</summary>
    public const string SettlementDateMissing = "SETTLEMENT_DATE_MISSING";
}

/// <summary>
/// One structured finding about the data or the calculation. <see cref="Message"/> is English and complete on
/// its own; the other fields let the UI group, translate and print the finding and tell the user how much money
/// depends on it.
/// </summary>
/// <param name="Code">One of <see cref="DiagnosticCodes"/>.</param>
/// <param name="Source">The file or statement the finding comes from, when known.</param>
/// <param name="AccountId">The broker account the finding is scoped to, when known.</param>
/// <param name="AffectedYears">Tax years whose return the finding may change.</param>
/// <param name="UnresolvedAmountPln">Money that is not in the declared amounts because of this finding.</param>
/// <param name="AffectsAmounts">True when the declared amounts cannot be called final until this is resolved.</param>
/// <param name="Decision">
/// The user's decision (one of <c>DecisionTopics</c>) that resolves the finding or that the finding reports on, so
/// that the UI can offer the choice next to it.
/// </param>
public record TaxDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? Source = null,
    string? AccountId = null,
    IReadOnlyList<int>? AffectedYears = null,
    decimal? UnresolvedAmountPln = null,
    bool AffectsAmounts = false,
    string? Decision = null)
{
    /// <summary>A finding the user must act on before the amounts can be trusted.</summary>
    public bool BlocksFiling => AffectsAmounts && Severity >= DiagnosticSeverity.Warning;
}
