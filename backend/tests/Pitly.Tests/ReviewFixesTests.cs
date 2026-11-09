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
        var v17 = Pit38Fields.FromSummary(Summary(2024, 0m, 0m, 0m)).Form;
        Assert.Equal(17, v17.Version);
        Assert.Equal((28, 29, 31, 33, 45, 46, 47, 49), (v17.Straty, v17.Podstawa, v17.Podatek, v17.PodatekNalezny, v17.Zryczaltowany, v17.Zaplacony, v17.Roznica, v17.DoZaplaty));

        var v18 = Pit38Fields.FromSummary(Summary(2025, 0m, 0m, 0m)).Form;
        Assert.Equal(18, v18.Version);
        Assert.Equal((30, 31, 33, 35, 47, 48, 49, 51), (v18.Straty, v18.Podstawa, v18.Podatek, v18.PodatekNalezny, v18.Zryczaltowany, v18.Zaplacony, v18.Roznica, v18.DoZaplaty));
    }

    [Fact]
    public void FlatTax_RoundsDividendsToFullPlnAndInterestToGroszUp()
    {
        // Dividends only: 23.16 PLN of tax rounds to 23 PLN; credit 18.76 -> difference 4.24 -> 4 PLN (art. 63 par. 1).
        var dividends = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 121.9m, interestPln: 0m, creditable: 18.76m));
        Assert.Equal(23m, dividends.Poz47ZryczaltowanyPodatek);
        Assert.Equal(18.76m, dividends.Poz48PodatekZaplaconyZaGranica);
        Assert.Equal(4m, dividends.Poz49Roznica);

        // Interest only (art. 30a ust. 1 pkt 1-3): 1.2331 -> 1.24 PLN, rounded up to the grosz (art. 63 par. 1a).
        var interest = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 0m, interestPln: 6.49m, creditable: 0m));
        Assert.Equal(1.24m, interest.Poz47ZryczaltowanyPodatek);
        Assert.Equal(1.24m, interest.Poz49Roznica);

        // Mixed: each stream rounded on its own and then added.
        var mixed = Pit38Fields.FromSummary(Summary(2025, dividendsPln: 121.9m, interestPln: 6.49m, creditable: 18.76m));
        Assert.Equal(24.24m, mixed.Poz47ZryczaltowanyPodatek);
        Assert.Equal(5.24m, mixed.Poz49Roznica);
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
