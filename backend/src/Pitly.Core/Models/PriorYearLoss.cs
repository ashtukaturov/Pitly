namespace Pitly.Core.Models;

/// <summary>
/// A capital-gains loss (art. 30b source) reported in PIT-38 of an earlier year, as entered by the user.
/// </summary>
/// <param name="Year">Tax year in which the loss arose (the PIT-38 it was reported on).</param>
/// <param name="LossPln">The loss as reported that year (poz. "Strata"), positive number.</param>
/// <param name="DeductedPln">Part of that loss already deducted in the years between then and now.</param>
public record PriorYearLoss(int Year, decimal LossPln, decimal DeductedPln = 0m);

/// <summary>Result of applying one prior-year loss to this year's income.</summary>
public record PriorYearLossLine(
    int Year,
    decimal LossPln,
    decimal DeductedPln,
    // What is still left of the loss before this year's deduction
    decimal RemainingPln,
    // The most that art. 9 ust. 3 allows to deduct from this loss this year (before the income cap)
    decimal MaxDeductiblePln,
    // What Pitly actually deducts in poz. 30 from this loss
    decimal AppliedPln,
    // What stays undeducted after this year (0 when the loss is used up)
    decimal UnusedPln,
    // Whether the loss can still be deducted in this tax year at all (5-year window)
    bool Eligible,
    // Whether the undeducted remainder is lost for good after this tax year
    bool ExpiresAfterThisYear,
    IReadOnlyList<string> Notes);

public record PriorYearLossResult(
    IReadOnlyList<PriorYearLossLine> Lines,
    decimal TotalAppliedPln,
    IReadOnlyList<string> Warnings);
