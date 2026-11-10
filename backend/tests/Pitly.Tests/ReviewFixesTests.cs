using Pitly.Core.Models;
using Pitly.Core.Parsing;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>Regression tests for the issues raised in the external code review.</summary>
public class ReviewFixesTests
{
    private static TaxSummary Summary(int year, decimal dividendsPln, decimal interestPln, decimal creditable, decimal interestCreditable = 0m)
        => new(
            TotalProceedsPln: 0m, TotalCostPln: 0m, CapitalGainPln: 0m, CapitalGainTaxPln: 0m,
            TotalDividendsPln: dividendsPln + interestPln, TotalWithholdingPln: creditable,
            TotalCreditableWithholdingPln: creditable, DividendTaxOwedPln: 0m,
            Year: year, TaxableFrom: new DateTime(year, 1, 1), TaxableTo: new DateTime(year, 12, 31),
            TradeResults: [], Dividends: [],
            TotalInterestPln: interestPln, TotalInterestCreditableWithholdingPln: interestCreditable);

    [Fact]
    public void FormLayout_FollowsTheFormVersionOfTheTaxYear()
    {
        var v17 = Pit38Fields.FromSummary(Summary(2024, 0m, 0m, 0m)).Form!;
        Assert.Equal(17, v17.Version);
        Assert.Equal((28, 29, 31, 33, 45, 46, 47, 49), (v17.Straty, v17.Podstawa, v17.Podatek, v17.PodatekNalezny, v17.Zryczaltowany, v17.Zaplacony, v17.Roznica, v17.DoZaplaty));
        Assert.Equal((22, 23, 24, 25, 26, 27, 44), (v17.InnePrzychody, v17.InneKoszty, v17.RazemPrzychody, v17.RazemKoszty, v17.Dochod, v17.Strata, v17.ZryczaltowanyNiepobrany));

        // PIT-38(18) added row 3 (poz. 24-25) to section C, so "Razem" and everything after it moved by two.
        var v18 = Pit38Fields.FromSummary(Summary(2025, 0m, 0m, 0m)).Form!;
        Assert.Equal(18, v18.Version);
        Assert.Equal((30, 31, 33, 35, 47, 48, 49, 51), (v18.Straty, v18.Podstawa, v18.Podatek, v18.PodatekNalezny, v18.Zryczaltowany, v18.Zaplacony, v18.Roznica, v18.DoZaplaty));
        Assert.Equal((22, 23, 26, 27, 28, 29, 46), (v18.InnePrzychody, v18.InneKoszty, v18.RazemPrzychody, v18.RazemKoszty, v18.Dochod, v18.Strata, v18.ZryczaltowanyNiepobrany));
    }

    [Theory]
    [InlineData(2021, 15)]
    [InlineData(2022, 16)]
    [InlineData(2023, 16)]
    [InlineData(2024, 17)]
    [InlineData(2025, 18)]
    public void FormLayout_ListsEveryVerifiedTaxYear(int year, int version)
    {
        Assert.Equal(version, Pit38FormLayout.ForYear(year)!.Version);
        Assert.Contains(year, Pit38FormLayout.SupportedYears);
    }

    [Theory]
    [InlineData(2020)]
    [InlineData(2026)]
    public void FormLayout_GivesNoFieldNumbersForAYearWithoutAVerifiedForm(int year)
    {
        var pit38 = Pit38Fields.FromSummary(Summary(year, dividendsPln: 100m, interestPln: 0m, creditable: 0m));

        Assert.Null(pit38.Form);
        // The amounts are still computed; only the numbering is withheld.
        Assert.Equal(19m, pit38.ZryczaltowanyPodatek);
        Assert.Equal(19m, pit38.PodatekDoZaplaty);
    }

    // Rounding order per the form PIT-38(18) and the Ministry's brochure, part G
    // (https://www.podatki.gov.pl/media/g5ebnm2e/broszura-do-pit-38-za-2025-r.pdf, p. 8): the flat tax (poz. 47) and
    // the tax paid abroad (poz. 48) are entered in zl, gr; only the difference (poz. 49) is rounded, to full PLN,
    // or up to the grosz for interest under art. 30a ust. 1 pkt 1-3 (art. 63 par. 1a O.p.).
    [Fact]
    public void FlatTax_KeepsPrecisionUntilTheDifferenceIsRounded()
    {
        // Control example: dividend 103 PLN, creditable foreign tax 15.10 PLN.
        var control = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 103m, interestPln: 0m, creditable: 15.10m));
        Assert.Equal(19.57m, control.ZryczaltowanyPodatek);
        Assert.Equal(15.10m, control.PodatekZaplaconyZaGranica);
        // 19.57 - 15.10 = 4.47 -> 4 PLN. Rounding the tax to 20 PLN first would give 4.90 -> 5 PLN.
        Assert.Equal(4m, control.Roznica);
        Assert.Equal(4m, control.PodatekDoZaplaty);

        // Dividends only: 23.161 PLN of tax is entered as 23.16; 23.161 - 18.76 = 4.401 -> 4 PLN.
        var dividends = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 121.9m, interestPln: 0m, creditable: 18.76m));
        Assert.Equal(23.16m, dividends.ZryczaltowanyPodatek);
        Assert.Equal(18.76m, dividends.PodatekZaplaconyZaGranica);
        Assert.Equal(4m, dividends.Roznica);

        // A difference of exactly 50 groszy rounds up: 100 x 19% = 19.00, credit 14.50 -> 4.50 -> 5 PLN.
        var half = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 100m, interestPln: 0m, creditable: 14.50m));
        Assert.Equal(5m, half.Roznica);
    }

    [Fact]
    public void FlatTax_RoundsInterestUpToTheGrosz()
    {
        // Interest only (art. 30a ust. 1 pkt 1-3): 1.2331 -> 1.24 PLN, rounded up to the grosz (art. 63 par. 1a).
        var interest = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 0m, interestPln: 6.49m, creditable: 0m));
        Assert.Equal(1.24m, interest.ZryczaltowanyPodatek);
        Assert.Equal(0m, interest.PodatekZaplaconyZaGranica);
        Assert.Equal(1.24m, interest.Roznica);

        // Foreign tax on interest is credited up to the Polish tax and the rest stays in groszy, not full PLN.
        var withCredit = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 0m, interestPln: 100m, creditable: 10.01m, interestCreditable: 10.01m));
        Assert.Equal(19m, withCredit.ZryczaltowanyPodatek);
        Assert.Equal(10.01m, withCredit.PodatekZaplaconyZaGranica);
        Assert.Equal(8.99m, withCredit.Roznica); // stays in groszy; 8.99 is not rounded to 9 PLN
    }

    [Fact]
    public void FlatTax_RoundsAMixOfDividendsAndInterestStreamByStream()
    {
        // The Ministry gives no rule for one field holding both streams: each is rounded by its own rule, then added.
        var mixed = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 121.9m, interestPln: 6.49m, creditable: 18.76m));
        Assert.Equal(24.40m, mixed.ZryczaltowanyPodatek);      // 23.16 + 1.24
        Assert.Equal(18.76m, mixed.PodatekZaplaconyZaGranica);
        Assert.Equal(5.24m, mixed.Roznica);                    // 4 PLN (dividends) + 1.24 PLN (interest)

        // The tax paid abroad can never exceed the flat tax it is credited against.
        var capped = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 103m, interestPln: 0m, creditable: 30m));
        Assert.Equal(19.57m, capped.PodatekZaplaconyZaGranica);
        Assert.Equal(0m, capped.Roznica);
    }

    [Fact]
    public async Task Withholding_IsSharedBetweenDividendRowsOfTheSameDay()
    {
        var calculator = new DividendTaxCalculator(new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m }));
        var date = new DateTime(2025, 3, 3);
        var dividends = new List<RawDividend>
        {
            new("AAPL", "USD", date, 100m, "US0378331005"),
            new("AAPL", "USD", date, 100m, "US0378331005")
        };
        var taxes = new List<RawWithholdingTax> { new("AAPL", "USD", date, 10m, "US0378331005") };

        var results = await calculator.CalculateAsync(dividends, taxes);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(5m, r.WithholdingTaxOriginal));
        Assert.Equal(40m, results.Sum(r => r.WithholdingTaxPln)); // 10 USD x 4, not 80
    }

    [Fact]
    public void Merge_DoesNotTreatTheSameDividendOnTwoAccountsAsADuplicate()
    {
        var date = new DateTime(2025, 5, 15);
        RawDividend Div() => new("AAPL", "USD", date, 10m, "US0378331005");
        var a = new ParsedStatement([], [Div()], [], StatementYear: 2025, AccountId: "U111");
        var b = new ParsedStatement([], [Div()], [], StatementYear: 2025, AccountId: "U222");
        var bAgain = new ParsedStatement([], [Div()], [], StatementYear: 2025, AccountId: "U222");

        var warnings = new List<string>();
        var merged = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("a.csv", a), new StatementMerger.NamedStatement("b.csv", b), new StatementMerger.NamedStatement("b2.csv", bAgain)],
            2025,
            warnings);

        Assert.Equal(2, merged.Dividends.Count); // one per account; the repeated export of U222 is skipped
        var warning = Assert.Single(warnings);
        Assert.Contains("b2.csv", warning);
    }

    [Fact]
    public void PriorYearLoss_OneOffAlreadyUsedFallsBackToHalfPerYear()
    {
        var result = PriorYearLossCalculator.Apply([new PriorYearLoss(2022, 1_000m, DeductedPln: 300m, OneOffUsed: true)], 2025, 10_000m);

        var line = Assert.Single(result.Lines);
        Assert.Equal(700m, line.RemainingPln);
        Assert.Equal(500m, line.MaxDeductiblePln);
        Assert.Equal(500m, line.AppliedPln);
        Assert.Contains(line.Notes, n => n.Contains("One-off deduction already used"));
    }
}
