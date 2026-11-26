namespace Pitly.Core.Tax;

/// <summary>Which date of a trade sets its tax year and its exchange rate.</summary>
public enum TradeDateBasis
{
    /// <summary>
    /// The settlement date, the day ownership passes: art. 17 ust. 1ab pkt 1 ustawy o PIT ties the income from a
    /// sale of securities to the transfer of ownership, which for dematerialised securities is the entry on the
    /// securities account (art. 7 ust. 2 ustawy o obrocie instrumentami finansowymi). The cost of a purchase is
    /// incurred when it is paid for, on its settlement day (art. 11a ust. 2). The default.
    /// </summary>
    SettlementDate,
    /// <summary>The trade (execution) date, which statements show first and much practice uses.</summary>
    TradeDate
}

/// <summary>How fees for lending out shares (IB "SYEP") are classified.</summary>
public enum LendingIncomeTreatment
{
    /// <summary>Not confirmed: included as interest, and the result is not ready to file.</summary>
    Unconfirmed,
    /// <summary>Confirmed as interest in section G of PIT-38 (art. 30a ust. 1 pkt 3), as the broker books it.</summary>
    Interest,
    /// <summary>Income from other sources (art. 20 ust. 1), declared in PIT-36 at the scale: left out of PIT-38.</summary>
    OtherSources
}

/// <summary>How payments in lieu of dividends are classified.</summary>
public enum PaymentInLieuTreatment
{
    /// <summary>Not confirmed: included like a dividend, and the result is not ready to file.</summary>
    Unconfirmed,
    /// <summary>Confirmed as dividend-equivalent income in section G of PIT-38 (art. 30a ust. 1 pkt 4).</summary>
    Dividend,
    /// <summary>Income from other sources (art. 20 ust. 1), declared in PIT-36 at the scale: left out of PIT-38.</summary>
    OtherSources
}

/// <summary>Where the effect of a withholding refund (or extra tax) booked in a later year than its payment belongs.</summary>
public enum CrossYearRefundTreatment
{
    /// <summary>Not confirmed: attributed to the payment's year, and both years are not ready to file.</summary>
    Unconfirmed,
    /// <summary>
    /// A correction of the payment's year (art. 81 O.p.): the credit of art. 30a ust. 9 is the tax actually paid
    /// abroad, so a refund means less was paid for that year. A return already filed for it is corrected.
    /// </summary>
    CorrectOriginalYear
}

/// <summary>
/// The user's answers to the questions no source settles for Pitly. Each answer is recorded with the session and
/// printed with the result; an unanswered question keeps the result from being ready to file. The default answer
/// for the date of a trade is the settlement date (the reading of art. 17 ust. 1ab pkt 1 that the tax
/// information office follows); the classifications and the cross-year treatment have no default.
/// </summary>
public sealed record TaxDecisions(
    TradeDateBasis DateBasis = TradeDateBasis.SettlementDate,
    LendingIncomeTreatment SecuritiesLending = LendingIncomeTreatment.Unconfirmed,
    PaymentInLieuTreatment PaymentsInLieu = PaymentInLieuTreatment.Unconfirmed,
    CrossYearRefundTreatment CrossYearRefunds = CrossYearRefundTreatment.Unconfirmed)
{
    /// <summary>The answers of a new calculation when the user gave none.</summary>
    public static readonly TaxDecisions Default = new();

    /// <summary>
    /// What a session stored before decisions existed was computed with: the trade date, and nothing confirmed.
    /// </summary>
    public static readonly TaxDecisions Legacy = new(TradeDateBasis.TradeDate);
}

/// <summary>Names of the decisions, as a finding refers to the one that resolves it (<see cref="Models.TaxDiagnostic.Decision"/>).</summary>
public static class DecisionTopics
{
    public const string DateBasis = "dateBasis";
    public const string SecuritiesLending = "securitiesLending";
    public const string PaymentsInLieu = "paymentsInLieu";
    public const string CrossYearRefunds = "crossYearRefunds";
}
