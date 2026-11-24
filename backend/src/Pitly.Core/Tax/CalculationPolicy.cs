namespace Pitly.Core.Tax;

/// <summary>
/// How the foreign tax credit on art. 30a income is limited. The default is the position of the Ministry of
/// Finance and the tax information office (KIS): only tax withheld in line with the treaty rate is creditable,
/// anything above it has to be reclaimed abroad. The alternative reproduces the line of several administrative
/// court rulings (NSA) that read art. 30a ust. 9 literally: the tax actually paid abroad is credited up to 19% of
/// the income, whatever the treaty says. It exists for comparison and is never the default; a session records
/// which method it used so that an old result is not reinterpreted silently.
/// </summary>
public enum CreditMethod
{
    /// <summary>
    /// Credit capped at the treaty rate of the issuer's country (art. 30a ust. 9 read with the relevant treaty).
    /// Sources: interpretacja ogólna and individual rulings of KIS on dividends taxed at 30% in the US without a
    /// W-8BEN; Broszura informacyjna PIT-38, part G ("podatek zapłacony za granicą ... do wysokości wynikającej z
    /// umowy").
    /// </summary>
    TreatyCap,
    /// <summary>
    /// Credit of the tax actually paid abroad up to 19% of the income (the literal wording of art. 30a ust. 9).
    /// For comparison with the NSA line of rulings only.
    /// </summary>
    StatutoryCap
}

/// <summary>
/// The rounding rules applied to section G and their version. The dividend and the interest rule each have a
/// source; the rule for a mix of both streams in the single field of the form does not, and the policy says so.
/// </summary>
public sealed record RoundingPolicy(
    string Version,
    string DividendRule,
    string InterestRule,
    string MixedRule,
    bool MixedRuleIsAssumption,
    IReadOnlyList<string> Sources)
{
    public static readonly RoundingPolicy Current = new(
        Version: "2025.3",
        DividendRule:
            "19% of the dividends is entered in zl, gr; the tax paid abroad is entered in zl, gr; only their difference is rounded to full PLN (art. 63 par. 1 Ordynacji podatkowej).",
        InterestRule:
            "For each interest payment the base in PLN is rounded up to the grosz, 19% of it is rounded up to the grosz (art. 63 par. 1a Ordynacji podatkowej for art. 30a ust. 1 pkt 1-3), " +
            "the tax paid abroad on it is entered in zl, gr, and the difference is rounded up to the grosz; the per-payment figures are added. " +
            "Rounding per payment rather than on the yearly total is Pitly's reading (each payment is a separate base, as a Polish payer withholds); " +
            "art. 63 par. 1a fixes the direction of rounding, not the level, and whenever the yearly total would round differently the result says so (INTEREST_ROUNDING_LEVEL).",
        MixedRule:
            "When one return holds both dividends and interest, each stream is rounded by its own rule and the two results are added. The form has one field for both and the Ministry gives no rule for the mix.",
        MixedRuleIsAssumption: true,
        Sources:
        [
            "Broszura informacyjna do zeznania PIT-38 za 2025 r., part G, p. 8: https://podatki.gov.pl/media/g5ebnm2e/broszura-do-pit-38-za-2025-r.pdf",
            "Ordynacja podatkowa art. 63 par. 1 and par. 1a",
            "Ustawa o PIT art. 30a ust. 1 pkt 1-5, art. 30a ust. 9, art. 11a, art. 45 ust. 3a"
        ]);
}

/// <summary>
/// The dates a trade is recognised and converted on, and their version. The rule is one reading of the law: art.
/// 17 ust. 1ab pkt 1 ustawy o PIT ties the income from a sale of securities to the transfer of ownership, which
/// for dematerialised securities is the entry on the securities account (settlement), while the day of the
/// transaction is the date brokers' statements and most practice use. The two readings differ only at the year
/// end and by the rate of a day or two; Pitly uses the trade date, keeps the settlement date on every row that
/// has one, and reports a sale whose settlement falls in the next year for both years instead of deciding it.
/// </summary>
public sealed record DatePolicy(
    string Version,
    string RecognitionRule,
    string RateRule,
    bool RecognitionRuleIsInterpretation,
    IReadOnlyList<string> Sources)
{
    public static readonly DatePolicy Current = new(
        Version: "2025.3",
        RecognitionRule:
            "A sale is counted in the year of its trade (execution) date; a purchase is costed as of its trade date. The settlement date, where the statement gives one, " +
            "is kept on the row for the audit trail. A sale executed in one year that settles in the next (or, without a settlement date, is executed in the last days of December) " +
            "is reported for both years and blocks filing until the year is confirmed: art. 17 ust. 1ab pkt 1 ties the income to the transfer of ownership, " +
            "which for dematerialised securities is the entry on the securities account (settlement), and Pitly does not decide between that reading and the trade date.",
        RateRule:
            "Proceeds, costs and commissions are converted at the NBP mid rate of the last business day before the trade date (art. 11a ust. 1 and 2); " +
            "the day of the NBP table actually used is kept on each row.",
        RecognitionRuleIsInterpretation: true,
        Sources:
        [
            "Ustawa o PIT art. 11a ust. 1-2, art. 17 ust. 1ab pkt 1, art. 30b",
            "Ustawa o obrocie instrumentami finansowymi art. 7 ust. 2 (dematerialised securities pass with the entry on the securities account)",
            "Broszura informacyjna do zeznania PIT-38 za 2025 r.: https://podatki.gov.pl/media/g5ebnm2e/broszura-do-pit-38-za-2025-r.pdf"
        ]);
}

/// <summary>Versions of the rules a stored session was computed with, so that an old result can be told apart from a new one.</summary>
public static class RuleVersions
{
    /// <summary>Changes whenever the matching of withholding, the rounding, the credit or the date rules change.</summary>
    public const string Rules = "2025.3";
}
