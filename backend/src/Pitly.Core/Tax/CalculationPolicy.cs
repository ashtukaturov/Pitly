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
        Version: "2025.1",
        DividendRule:
            "19% of the dividends is entered in zl, gr; the tax paid abroad is entered in zl, gr; only their difference is rounded to full PLN (art. 63 par. 1 Ordynacji podatkowej).",
        InterestRule:
            "19% of the interest is rounded up to the grosz (art. 63 par. 1a Ordynacji podatkowej for art. 30a ust. 1 pkt 1-3), and so is the difference after the tax paid abroad.",
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

/// <summary>Versions of the rules a stored session was computed with, so that an old result can be told apart from a new one.</summary>
public static class RuleVersions
{
    /// <summary>Changes whenever the matching of withholding, the rounding or the credit rules change.</summary>
    public const string Rules = "2025.1";
}
