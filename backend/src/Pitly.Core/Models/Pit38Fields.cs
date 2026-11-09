using Pitly.Core.Tax;

namespace Pitly.Core.Models;

/// <summary>
/// Maps calculated tax values to actual PIT-38(18) form field positions (poz.).
/// Field numbers match the official form published by the Polish Ministry of Finance.
/// </summary>
public record Pit38Fields(
    int Year,

    // ── Section C: Dochody / Straty — art. 30b ust. 1 ustawy ──
    // Row 1 (poz. 20-21): Przychody wykazane w PIT-8C — always 0 for foreign brokers
    // Row 2 (poz. 22-23): Inne przychody — this is where IB income goes
    decimal Poz22Przychody,
    decimal Poz23Koszty,
    // Row 3 (poz. 24-25): Razem = sum of rows 1+2 (equal to 22-23 since row 1 is empty)
    decimal Poz24RazemPrzychody,
    decimal Poz25RazemKoszty,
    // Poz. 26: Dochód (gain), only if proceeds > costs, otherwise 0
    decimal Poz26Dochod,
    // Poz. 27: Strata (loss as positive number), only if costs > proceeds, otherwise 0
    decimal Poz27Strata,

    // ── Section D: Obliczenie zobowiązania podatkowego — art. 30b ust. 1 ustawy ──
    // Poz. 30: Straty z lat ubiegłych (cannot exceed the income it is deducted from)
    decimal Poz30StratyZLatUbieglych,
    // Poz. 31: Podstawa obliczenia podatku = dochód minus poz. 30 (rounded to full PLN per art. 63 § 1 O.p.)
    decimal Poz31PodstawaObliczenia,
    // Poz. 33: Podatek = poz. 31 × 19%
    decimal Poz33Podatek,
    // Poz. 35: Podatek należny (rounded to full PLN)
    decimal Poz35PodatekNalezny,

    // ── Section G: Podatek do zapłaty / Nadpłata ──
    // Poz. 47: Zryczałtowany podatek 19% od przychodów zagranicznych z art. 30a ust. 1 pkt 1-5
    //          (dywidendy, odsetki, payments in lieu)
    decimal Poz47ZryczaltowanyPodatek,
    // Poz. 48: Podatek zapłacony za granicą (treaty-capped, and never above poz. 47, per art. 30a ust. 9)
    decimal Poz48PodatekZaplaconyZaGranica,
    // Poz. 49: Różnica = poz. 47 − poz. 48 (rounded per art. 63 § 1a O.p. — see footnote 8 on PIT-38(18))
    decimal Poz49Roznica,
    // Poz. 51: PODATEK DO ZAPŁATY = poz. 35 + poz. 49
    decimal Poz51PodatekDoZaplaty,

    // ── Danina solidarnościowa (art. 30h) — separate DSF-1 declaration, not a PIT-38 field ──
    // Excess of the art. 30b income base (poz. 31) over 1 000 000 PLN. Only income known to Pitly is
    // counted; employment, business and other qualifying incomes also count toward the threshold.
    decimal DaninaSolidarnosciowaNadwyzka,
    // 4% of the excess, rounded to full PLN
    decimal DaninaSolidarnosciowaPln,

    // ── Informational (not a PIT-38 field — shown for user reference) ──
    decimal TotalDividendsPln,

    // Per-loss breakdown of poz. 30 and warnings about the deduction (art. 9 ust. 3)
    IReadOnlyList<PriorYearLossLine> PriorYearLosses,
    IReadOnlyList<string> Warnings)
{
    public static Pit38Fields FromSummary(TaxSummary summary)
    {
        // Section C — Capital gains
        var przychody = summary.TotalProceedsPln;
        var koszty = summary.TotalCostPln;
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

        // Section G — Dividends, interest, payments in lieu
        // Poz. 47/49 fall under art. 30a ust. 1 — per art. 63 § 1a Ordynacji podatkowej
        // (footnote 8 on PIT-38(18)), amounts are rounded to full groszy upward, not full PLN.
        var zryczaltowanyPodatek = RoundToGroszUp(summary.TotalDividendsPln * TaxConstants.TaxRate);
        // Only the treaty-capped (creditable) part of foreign withholding may be entered in poz. 48.
        var podatekZaGranica = Math.Round(Math.Min(summary.TotalCreditableWithholdingPln, zryczaltowanyPodatek), 2);
        var roznica = RoundToGroszUp(Math.Max(zryczaltowanyPodatek - podatekZaGranica, 0));

        var podatekDoZaplaty = podatekNalezny + roznica;

        // Danina solidarnościowa — art. 30h: 4% of the excess over 1 000 000 PLN. Prior-year losses reduce
        // the base (interpretacja ogólna MF, 2023). Art. 30a income (dividends, interest) is NOT included.
        var daninaNadwyzka = Math.Max(podstawa - TaxConstants.SolidarityLevyThresholdPln, 0);
        var danina = RoundToFullPln(daninaNadwyzka * TaxConstants.SolidarityLevyRate);

        return new Pit38Fields(
            Year: summary.Year,
            Poz22Przychody: przychody,
            Poz23Koszty: koszty,
            Poz24RazemPrzychody: przychody,
            Poz25RazemKoszty: koszty,
            Poz26Dochod: dochod,
            Poz27Strata: strata,
            Poz30StratyZLatUbieglych: straty,
            Poz31PodstawaObliczenia: podstawa,
            Poz33Podatek: podatek,
            Poz35PodatekNalezny: podatekNalezny,
            Poz47ZryczaltowanyPodatek: zryczaltowanyPodatek,
            Poz48PodatekZaplaconyZaGranica: podatekZaGranica,
            Poz49Roznica: roznica,
            Poz51PodatekDoZaplaty: podatekDoZaplaty,
            DaninaSolidarnosciowaNadwyzka: daninaNadwyzka,
            DaninaSolidarnosciowaPln: danina,
            TotalDividendsPln: summary.TotalDividendsPln,
            PriorYearLosses: lossResult.Lines,
            Warnings: lossResult.Warnings);
    }

    /// <summary>
    /// Rounds to full PLN per art. 63 § 1 Ordynacji podatkowej:
    /// amounts below 50 groszy are dropped, 50+ groszy rounded up.
    /// </summary>
    private static decimal RoundToFullPln(decimal value)
        => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds to full groszy upward per art. 63 § 1a Ordynacji podatkowej:
    /// for lump-sum tax under art. 30a ust. 1 pkt 1-3, any fractional grosz rounds up.
    /// Referenced by footnote 8 on PIT-38(18) form for poz. 46 and 49.
    /// </summary>
    private static decimal RoundToGroszUp(decimal value)
        => Math.Ceiling(value * 100m) / 100m;
}
