using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

public class PriorYearLossCalculatorTests
{
    [Fact]
    public void Apply_ReturnsNothingWithoutLosses()
    {
        var result = PriorYearLossCalculator.Apply([], 2025, 10_000m);

        Assert.Empty(result.Lines);
        Assert.Equal(0m, result.TotalAppliedPln);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Apply_DeductsWholeRemainderOfPost2019LossInOneGo()
    {
        var result = PriorYearLossCalculator.Apply([new PriorYearLoss(2023, 1_200m)], 2025, 10_000m);

        var line = Assert.Single(result.Lines);
        Assert.True(line.Eligible);
        Assert.Equal(1_200m, line.RemainingPln);
        Assert.Equal(1_200m, line.MaxDeductiblePln);
        Assert.Equal(1_200m, line.AppliedPln);
        Assert.Equal(0m, line.UnusedPln);
        Assert.Equal(1_200m, result.TotalAppliedPln);
        // Taking more than 50% at once relies on the one-off deduction, so the user is told about it
        Assert.Contains(line.Notes, n => n.Contains("one-off", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_CapsPre2019LossAtHalfPerYear()
    {
        // Tax year 2023, loss from 2018 (still inside the window 2018-2022), nothing deducted yet
        var result = PriorYearLossCalculator.Apply([new PriorYearLoss(2018, 1_000m)], 2023, 10_000m);

        var line = Assert.Single(result.Lines);
        Assert.Equal(500m, line.MaxDeductiblePln);
        Assert.Equal(500m, line.AppliedPln);
        Assert.Equal(500m, line.UnusedPln);
        Assert.True(line.ExpiresAfterThisYear);
        Assert.Contains(line.Notes, n => n.Contains("50%"));
        Assert.Contains(result.Warnings, w => w.Contains("expires after tax year 2023"));
    }

    [Fact]
    public void Apply_IgnoresLossesOutsideFiveYearWindow()
    {
        var result = PriorYearLossCalculator.Apply(
            [new PriorYearLoss(2019, 500m), new PriorYearLoss(2020, 300m), new PriorYearLoss(2025, 100m)],
            2025,
            10_000m);

        Assert.Equal(3, result.Lines.Count);
        var expired = result.Lines.Single(l => l.Year == 2019);
        Assert.False(expired.Eligible);
        Assert.Equal(0m, expired.AppliedPln);
        var sameYear = result.Lines.Single(l => l.Year == 2025);
        Assert.False(sameYear.Eligible);
        var inWindow = result.Lines.Single(l => l.Year == 2020);
        Assert.True(inWindow.Eligible);
        Assert.Equal(300m, inWindow.AppliedPln);
        Assert.Equal(300m, result.TotalAppliedPln);
        Assert.Contains(result.Warnings, w => w.Contains("2020-2024"));
    }

    [Fact]
    public void Apply_UsesOldestLossFirstAndCapsAtIncome()
    {
        var result = PriorYearLossCalculator.Apply(
            [new PriorYearLoss(2022, 700m), new PriorYearLoss(2020, 400m)],
            2025,
            600m);

        Assert.Equal(600m, result.TotalAppliedPln);
        var oldest = result.Lines.Single(l => l.Year == 2020);
        Assert.Equal(400m, oldest.AppliedPln);
        Assert.Equal(0m, oldest.UnusedPln);
        var newer = result.Lines.Single(l => l.Year == 2022);
        Assert.Equal(200m, newer.AppliedPln);
        Assert.Equal(500m, newer.UnusedPln);
        Assert.False(newer.ExpiresAfterThisYear);
        Assert.Contains(newer.Notes, n => n.Contains("until tax year 2027"));
        Assert.Contains(result.Warnings, w => w.Contains("exceed this year's income"));
    }

    [Fact]
    public void Apply_AccountsForAmountsDeductedEarlier()
    {
        var result = PriorYearLossCalculator.Apply([new PriorYearLoss(2021, 1_000m, DeductedPln: 800m)], 2025, 10_000m);

        var line = Assert.Single(result.Lines);
        Assert.Equal(200m, line.RemainingPln);
        Assert.Equal(200m, line.AppliedPln);

        var exhausted = PriorYearLossCalculator.Apply([new PriorYearLoss(2021, 1_000m, DeductedPln: 1_000m)], 2025, 10_000m);
        Assert.Equal(0m, exhausted.TotalAppliedPln);
        Assert.Contains(exhausted.Lines[0].Notes, n => n.Contains("already been fully deducted"));
    }

    [Fact]
    public void Apply_WarnsWhenThereIsNoIncomeToOffset()
    {
        var result = PriorYearLossCalculator.Apply([new PriorYearLoss(2024, 1_000m)], 2025, 0m);

        Assert.Equal(0m, result.TotalAppliedPln);
        Assert.Equal(1_000m, result.Lines[0].UnusedPln);
        Assert.Contains(result.Warnings, w => w.Contains("No capital-gains income"));
    }

    [Fact]
    public void Apply_LimitsOneOffDeductionToFiveMillion()
    {
        var result = PriorYearLossCalculator.Apply([new PriorYearLoss(2023, 12_000_000m)], 2025, 20_000_000m);

        var line = Assert.Single(result.Lines);
        // max(50% = 6 000 000, one-off 5 000 000) = 6 000 000
        Assert.Equal(6_000_000m, line.MaxDeductiblePln);
        Assert.Equal(6_000_000m, line.AppliedPln);

        var smaller = PriorYearLossCalculator.Apply([new PriorYearLoss(2023, 8_000_000m)], 2025, 20_000_000m);
        // max(50% = 4 000 000, one-off 5 000 000) = 5 000 000
        Assert.Equal(5_000_000m, smaller.Lines[0].MaxDeductiblePln);
    }
}
