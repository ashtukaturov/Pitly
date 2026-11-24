using System.Globalization;
using Pitly.Core.Tax;

namespace Pitly.Core.Models;

/// <summary>
/// The amounts that go into PIT-38, named after the fields of the form rather than their numbers: the numbers
/// differ between form versions and are supplied separately in <see cref="Form"/> for tax years whose version was
/// verified (see <see cref="Pit38FormLayout"/>). This record is the single source of the amount to pay: the
/// dashboard, the declaration page and the printout all show these values.
/// </summary>
public record Pit38Fields(
    int Year,

    // ── Section C: Dochody / straty — art. 30b ust. 1 ustawy ──
    // Row "Inne przychody": where income from a foreign broker goes (no PIT-8C is issued)
    decimal InnePrzychody,
    decimal InneKoszty,
    // Row "Razem": equal to "Inne przychody" because the PIT-8C and exempt-income rows stay empty
    decimal RazemPrzychody,
    decimal RazemKoszty,
    // Dochód (gain), only if proceeds exceed costs, otherwise 0
    decimal Dochod,
    // Strata (loss as a positive number), only if costs exceed proceeds, otherwise 0
    decimal Strata,

    // ── Section D: Obliczenie zobowiązania podatkowego — art. 30b ust. 1 ustawy ──
    // Straty z lat ubiegłych (cannot exceed the income it is deducted from)
    decimal StratyZLatUbieglych,
    // Podstawa obliczenia podatku = dochód minus straty, rounded to full PLN (art. 63 § 1 O.p.)
    decimal PodstawaObliczenia,
    // Podatek = podstawa × 19%
    decimal Podatek,
    // Podatek należny (rounded to full PLN)
    decimal PodatekNalezny,

    // ── Section G: Podatek do zapłaty / nadpłata ──
    // Zryczałtowany podatek od przychodów z art. 30a ust. 1 pkt 1-5 uzyskanych za granicą (zł, gr; not rounded to PLN)
    decimal ZryczaltowanyPodatek,
    // Podatek zapłacony za granicą, art. 30a ust. 9 (zł, gr; treaty-capped and never above the flat tax)
    decimal PodatekZaplaconyZaGranica,
    // Różnica: the only rounded field of the three (full PLN; grosz upward for interest, art. 63 § 1a O.p.)
    decimal Roznica,
    // PODATEK DO ZAPŁATY = podatek należny + różnica
    decimal PodatekDoZaplaty,

    // ── Danina solidarnościowa (art. 30h) — separate DSF-1 declaration, not a PIT-38 field ──
    // Excess of the art. 30b income base over 1 000 000 PLN. Only income known to Pitly is
    // counted; employment, business and other qualifying incomes also count toward the threshold.
    decimal DaninaSolidarnosciowaNadwyzka,
    // 4% of the excess, rounded to full PLN
    decimal DaninaSolidarnosciowaPln,

    // ── Informational (not a PIT-38 field — shown for user reference) ──
    decimal TotalDividendsPln,

    // Per-loss breakdown of the deduction and warnings about it (art. 9 ust. 3)
    IReadOnlyList<PriorYearLossLine> PriorYearLosses,
    IReadOnlyList<string> Warnings,

    // PIT/ZG annex: art. 30b income per issuer country (dividend figures included for reference only)
    IReadOnlyList<CountryIncome> PitZg,

    // Field numbers of the verified form version for Year; null when no version was verified for that year
    Pit38FormLayout? Form,

    // Warnings that put the amounts themselves in doubt (opening positions that do not match the uploaded
    // history, withholding that could not be attributed); shown next to the result and printed with it
    IReadOnlyList<string> DataWarnings,

    // The session was saved by an older version without the data needed to rebuild these amounts exactly
    bool NeedsReimport = false,

    // Every finding with its code, scope and the amount that depends on it; DataWarnings are the messages of the
    // findings that are at least warnings
    IReadOnlyList<TaxDiagnostic>? Diagnostics = null,

    // False while any finding says the declared amounts cannot be called final (an unresolved refund, a
    // withholding row that matches nothing, a cross-year adjustment whose year is not decided, an old session)
    bool ReadyToFile = true,

    // Money that is not in the amounts above because of unresolved findings
    decimal UnresolvedAmountPln = 0m,

    // The rounding rules applied to section G, with their version and sources
    RoundingPolicy? RoundingPolicy = null,

    // How the foreign tax credit was limited
    CreditMethod CreditMethod = CreditMethod.TreatyCap,

    // The dates a trade is recognised and converted on, with their version and sources
    DatePolicy? DatePolicy = null)
{
    public static Pit38Fields FromSummary(TaxSummary summary)
    {
        // Section C — Capital gains. The summary keeps full precision; the form fields are in zl, gr.
        var przychody = RoundToGrosz(summary.TotalProceedsPln);
        var koszty = RoundToGrosz(summary.TotalCostPln);
        var difference = przychody - koszty;
        var dochod = Math.Round(Math.Max(difference, 0), 2);
        var strata = Math.Round(Math.Max(-difference, 0), 2);

        // Section D — Tax calculation
        // Prior-year losses (art. 9 ust. 3): deductible over the next 5 years, max 50% of each year's loss
        // per year (or in one go up to 5 000 000 PLN for losses from 2019+). Each entered loss is checked
        // against the 5-year window and per-year limits by PriorYearLossCalculator and capped at the income.
        var lossResult = PriorYearLossCalculator.Apply(summary.PriorYearLosses, summary.Year, dochod);
        var straty = lossResult.TotalAppliedPln;
        var podstawa = RoundToFullPln(dochod - straty);
        var podatek = Math.Round(podstawa * TaxConstants.TaxRate, 2);
        var podatekNalezny = RoundToFullPln(podatek);

        // Section G — Dividends, interest, payments in lieu received abroad.
        //
        // Order of calculation per the form and the Ministry's brochure ("Broszura informacyjna do zeznania
        // PIT-38 za 2025 rok", part G, https://www.podatki.gov.pl/media/g5ebnm2e/broszura-do-pit-38-za-2025-r.pdf;
        // same wording for 2022-2024 with the earlier field numbers):
        //   1. the flat tax is the income multiplied by 19% and is entered in zł, gr (no rounding prescribed);
        //   2. the tax paid abroad is entered in zł, gr and may not exceed the flat tax;
        //   3. only the difference is rounded: "Kwoty w poz. 46 i 49 podaje się po zaokrągleniu do pełnych
        //      złotych" (below 50 groszy dropped, 50 and more rounded up), with the exception of art. 63 § 1a
        //      O.p. for the flat tax under art. 30a ust. 1 pkt 1-3 (interest), which is rounded up to full groszy.
        // So the dividend tax keeps its precision until the difference is taken; rounding it to full PLN before
        // subtracting the foreign tax would overstate the difference (19.57 - 15.10 must give 4, not 20 - 15.10 = 5).
        //
        // Interest (art. 30a ust. 1 pkt 1-3): art. 63 § 1a O.p. rounds BOTH the base and the tax up to the grosz
        // ("podstawy opodatkowania ... oraz kwoty podatków od nich pobierane zaokrągla się do pełnych groszy w górę"),
        // so 0.1051 PLN of interest is a base of 0.11 PLN and a tax of 0.0209 -> 0.03 PLN, not 19% of 0.1051 rounded
        // up (0.02). The rule is applied per payment: each payment is a separate base from which the payer withholds
        // (and the taxpayer self-assesses) the tax, so the sum of the per-payment taxes is what the payer would have
        // withheld. The exact gross stays in the summary for the audit; the rounded base is a separate figure.
        //
        // The form has a single field for both streams and the Ministry gives no rule for a mix of interest and
        // dividends. Each stream is therefore rounded by its own rule and the two results are added.
        var interestRows = (summary.Dividends ?? []).Where(d => d.Kind == IncomeKind.Interest).ToList();
        var interestPln = Math.Max(summary.TotalInterestPln, 0m);
        var dividendPln = Math.Max(summary.TotalDividendsPln - interestPln, 0m);
        var interestCreditable = Math.Max(summary.TotalInterestCreditableWithholdingPln, 0m);
        var dividendCreditable = Math.Max(summary.TotalCreditableWithholdingPln - interestCreditable, 0m);

        var dividendTax = dividendPln * TaxConstants.TaxRate;

        // Per interest payment: base rounded up, tax rounded up, credit entered in zł, gr, difference rounded up.
        // When the rows are not available (a session stored without them) the totals are treated as one payment.
        var interestPayments = interestRows.Count > 0 && interestRows.Sum(d => d.AmountPln) == summary.TotalInterestPln
            ? interestRows.Select(d => (Base: Math.Max(d.AmountPln, 0m), Credit: Math.Max(d.CreditableWithholdingPln, 0m))).ToList()
            : [(Base: interestPln, Credit: interestCreditable)];
        var interestTax = 0m;
        var interestCredit = 0m;
        var interestDifference = 0m;
        foreach (var (grossBase, creditable) in interestPayments)
        {
            var taxOnPayment = RoundToGroszUp(RoundToGroszUp(grossBase) * TaxConstants.TaxRate);
            var creditOnPayment = RoundToGrosz(Math.Min(creditable, taxOnPayment));
            interestTax += taxOnPayment;
            interestCredit += creditOnPayment;
            interestDifference += RoundToGroszUp(Math.Max(taxOnPayment - creditOnPayment, 0m));
        }

        var zryczaltowanyPodatek = RoundToGrosz(dividendTax) + interestTax;

        // Only the treaty-capped (creditable) part of foreign withholding may be entered as tax paid abroad,
        // and never more than the Polish flat tax on the same stream (art. 30a ust. 9). Every field of the form is
        // in zł, gr: a converted credit of 0.015 PLN is entered as 0.02 and the difference is taken from the
        // entered figures, so that the printed fields add up.
        var dividendCredit = Math.Min(dividendCreditable, dividendTax);
        var podatekZaGranica = Math.Min(RoundToGrosz(dividendCredit) + interestCredit, zryczaltowanyPodatek);

        var roznica = RoundToFullPln(Math.Max(dividendTax - dividendCredit, 0m)) + interestDifference;

        var podatekDoZaplaty = podatekNalezny + roznica;

        // Danina solidarnościowa — art. 30h: 4% of the excess over 1 000 000 PLN. Prior-year losses reduce
        // the base (interpretacja ogólna MF, 2023). Art. 30a income (dividends, interest) is NOT included.
        var daninaNadwyzka = Math.Max(podstawa - TaxConstants.SolidarityLevyThresholdPln, 0);
        var danina = RoundToFullPln(daninaNadwyzka * TaxConstants.SolidarityLevyRate);

        var diagnostics = (summary.Diagnostics ?? []).ToList();
        if (diagnostics.Count == 0 && summary.ResultWarnings is { Count: > 0 } legacy)
        {
            // A session stored before diagnostics existed: its result warnings are the findings.
            diagnostics.AddRange(legacy.Select(w => new TaxDiagnostic(
                w.StartsWith(CapitalGainsTaxCalculator.PositionCheckPrefix, StringComparison.Ordinal) ? DiagnosticCodes.PositionCheck
                    : w.StartsWith(Parsing.StatementMerger.AccountCheckPrefix, StringComparison.Ordinal) ? DiagnosticCodes.AccountAssumed
                    : DiagnosticCodes.UnmatchedWithholding,
                DiagnosticSeverity.Warning, w, AffectsAmounts: true)));
        }
        // Interest rounded per payment can give another figure than the same interest rounded once on the yearly
        // total. Art. 63 § 1a fixes the direction (up to the grosz), not the level at which a self-assessed return
        // aggregates the base; the level is Pitly's policy, so the result names what the other reading would give.
        // A difference between two calculators is then seen for what it is, and agreement with one proves nothing.
        if (interestPayments.Count > 1)
        {
            var yearlyTax = RoundToGroszUp(RoundToGroszUp(interestPln) * TaxConstants.TaxRate);
            if (yearlyTax != interestTax)
            {
                diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.InterestRoundingLevel, DiagnosticSeverity.Warning,
                    $"The flat tax on interest was rounded per payment ({interestPayments.Count} payments, the base and the tax of each rounded up to the grosz): {Grosz(interestTax)} PLN. " +
                    $"Rounded once on the yearly total of {Exact(interestPln)} PLN it would be {Grosz(yearlyTax)} PLN, {Grosz(interestTax - yearlyTax)} PLN less. " +
                    "Art. 63 § 1a Ordynacji podatkowej fixes the direction of rounding, not the level at which a self-assessed return aggregates the base; " +
                    $"Pitly applies it per payment (each payment is a separate base, as a Polish payer withholds) and records the choice as rounding policy {RoundingPolicy.Current.Version}. " +
                    "Agreement with another calculator proves neither reading.",
                    AffectedYears: [summary.Year]));
            }
        }

        var dataWarnings = diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.Message).Distinct().ToList();
        foreach (var w in summary.ResultWarnings ?? [])
        {
            if (!dataWarnings.Contains(w))
                dataWarnings.Add(w);
        }

        return new Pit38Fields(
            Year: summary.Year,
            InnePrzychody: przychody,
            InneKoszty: koszty,
            RazemPrzychody: przychody,
            RazemKoszty: koszty,
            Dochod: dochod,
            Strata: strata,
            StratyZLatUbieglych: straty,
            PodstawaObliczenia: podstawa,
            Podatek: podatek,
            PodatekNalezny: podatekNalezny,
            ZryczaltowanyPodatek: zryczaltowanyPodatek,
            PodatekZaplaconyZaGranica: podatekZaGranica,
            Roznica: roznica,
            PodatekDoZaplaty: podatekDoZaplaty,
            DaninaSolidarnosciowaNadwyzka: daninaNadwyzka,
            DaninaSolidarnosciowaPln: danina,
            TotalDividendsPln: summary.TotalDividendsPln,
            PriorYearLosses: lossResult.Lines,
            Warnings: lossResult.Warnings,
            PitZg: summary.Countries ?? [],
            Form: Pit38FormLayout.ForYear(summary.Year),
            DataWarnings: dataWarnings,
            Diagnostics: diagnostics,
            ReadyToFile: diagnostics.All(d => !d.BlocksFiling),
            UnresolvedAmountPln: diagnostics.Where(d => d.BlocksFiling).Sum(d => d.UnresolvedAmountPln ?? 0m),
            RoundingPolicy: RoundingPolicy.Current,
            CreditMethod: summary.CreditMethod,
            DatePolicy: DatePolicy.Current);
    }

    private static string Grosz(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Exact(decimal value) => value.ToString("0.00####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Rounds to full PLN per art. 63 § 1 Ordynacji podatkowej:
    /// amounts below 50 groszy are dropped, 50+ groszy rounded up.
    /// </summary>
    private static decimal RoundToFullPln(decimal value)
        => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>Amount as entered in a "zł, gr" field that has no rounding rule of its own.</summary>
    private static decimal RoundToGrosz(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds to full groszy upward per art. 63 § 1a Ordynacji podatkowej: the flat tax under
    /// art. 30a ust. 1 pkt 1-3 (interest) is rounded up to the grosz instead of to full PLN.
    /// </summary>
    private static decimal RoundToGroszUp(decimal value)
        => Math.Ceiling(value * 100m) / 100m;
}
