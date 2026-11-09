using System.Globalization;
using Pitly.Core.Models;

namespace Pitly.Core.Tax;

/// <summary>
/// Applies prior-year capital losses to this year's art. 30b income under art. 9 ust. 3 updof:
/// a loss may be deducted in the five consecutive tax years that follow the loss year, at most 50% of the loss
/// per year (pkt 1) — or, for losses incurred from 2019 onwards, in one go up to 5 000 000 PLN, with any
/// remainder deducted in the remaining years under the 50% rule (pkt 2). The total deduction can never exceed
/// the income it reduces. Losses are consumed oldest first, because the oldest expire first.
/// </summary>
public static class PriorYearLossCalculator
{
    public const int CarryForwardYears = 5;
    public const decimal OneOffDeductionLimitPln = 5_000_000m;
    public const int OneOffDeductionFirstLossYear = 2019;
    public const decimal PerYearShare = 0.5m;

    public static PriorYearLossResult Apply(IReadOnlyList<PriorYearLoss>? losses, int taxYear, decimal incomePln)
    {
        var lines = new List<PriorYearLossLine>();
        var warnings = new List<string>();
        if (losses is null || losses.Count == 0)
            return new PriorYearLossResult(lines, 0m, warnings);

        var income = Math.Max(incomePln, 0m);
        var budget = income;
        var firstEligibleYear = taxYear - CarryForwardYears;
        var lastEligibleYear = taxYear - 1;
        var totalEligible = 0m;

        foreach (var loss in losses.OrderBy(l => l.Year))
        {
            var notes = new List<string>();
            var lossPln = Math.Max(loss.LossPln, 0m);
            var deducted = Math.Min(Math.Max(loss.DeductedPln, 0m), lossPln);
            var remaining = Math.Round(lossPln - deducted, 2);
            var halfCap = Math.Round(lossPln * PerYearShare, 2);

            var eligible = loss.Year >= firstEligibleYear && loss.Year <= lastEligibleYear;
            var lastYearOfWindow = loss.Year == firstEligibleYear;

            decimal maxDeductible;
            if (!eligible)
            {
                maxDeductible = 0m;
                notes.Add(loss.Year >= taxYear
                    ? $"Loss year {loss.Year} is not earlier than the tax year {taxYear}; only losses from earlier years can be deducted."
                    : $"The 5-year carry-forward window for a {loss.Year} loss ended with tax year {loss.Year + CarryForwardYears}; it can no longer be deducted in {taxYear}.");
            }
            else if (remaining <= 0m)
            {
                maxDeductible = 0m;
                notes.Add("This loss has already been fully deducted in earlier years.");
            }
            else if (loss.Year >= OneOffDeductionFirstLossYear && loss.OneOffUsed)
            {
                // The one-off deduction was already taken in an earlier year: only the 50% rule remains.
                maxDeductible = Math.Min(remaining, halfCap);
                if (remaining > halfCap)
                    notes.Add("One-off deduction already used for this loss: at most 50% of the loss (" + Fmt(halfCap) + ") may be deducted in a single year (art. 9 ust. 3 pkt 2).");
            }
            else if (loss.Year >= OneOffDeductionFirstLossYear)
            {
                // Pkt 2: the whole remainder in one go, up to 5 000 000 PLN. Above that limit pkt 1 still applies,
                // i.e. a single year is capped at 50% of the loss (only matters for losses above 10 000 000 PLN).
                maxDeductible = Math.Min(remaining, Math.Max(halfCap, OneOffDeductionLimitPln));
                if (remaining > OneOffDeductionLimitPln)
                    notes.Add($"Only {Fmt(maxDeductible)} of the remaining {Fmt(remaining)} can be deducted this year: the one-off deduction is limited to 5 000 000 zl, otherwise 50% of the loss per year (art. 9 ust. 3 pkt 2).");
                else if (remaining > halfCap)
                    notes.Add("Deducting more than 50% of this loss in one year relies on the one-off deduction for losses from 2019 onwards (art. 9 ust. 3 pkt 2), available once per loss. If you already used it in an earlier year, limit this year to 50% of the loss.");
            }
            else
            {
                // Pkt 1 only: pre-2019 losses are limited to 50% of the loss per year.
                maxDeductible = Math.Min(remaining, halfCap);
                if (remaining > halfCap)
                    notes.Add($"Pre-2019 loss: at most 50% of the loss ({Fmt(halfCap)}) may be deducted in a single year (art. 9 ust. 3 pkt 1).");
            }

            totalEligible += maxDeductible;
            var applied = Math.Round(Math.Min(maxDeductible, budget), 2);
            budget -= applied;
            var unused = Math.Round(remaining - applied, 2);
            var expiresAfterThisYear = eligible && lastYearOfWindow && unused > 0m;

            if (eligible && unused > 0m)
            {
                if (lastYearOfWindow)
                {
                    warnings.Add($"{Fmt(unused)} of the {loss.Year} loss stays undeducted and expires after tax year {taxYear}; it cannot be used later.");
                    notes.Add($"Last year this loss can be used: {Fmt(unused)} will be forfeited.");
                }
                else
                {
                    notes.Add($"{Fmt(unused)} carries forward and can be deducted until tax year {loss.Year + CarryForwardYears}.");
                }
            }

            lines.Add(new PriorYearLossLine(
                Year: loss.Year,
                LossPln: lossPln,
                DeductedPln: deducted,
                RemainingPln: remaining,
                MaxDeductiblePln: maxDeductible,
                AppliedPln: applied,
                UnusedPln: unused,
                Eligible: eligible,
                ExpiresAfterThisYear: expiresAfterThisYear,
                Notes: notes));
        }

        var totalApplied = Math.Round(lines.Sum(l => l.AppliedPln), 2);

        if (lines.Any(l => !l.Eligible))
            warnings.Add($"Only losses from tax years {firstEligibleYear}-{lastEligibleYear} can be deducted in {taxYear}; losses outside this window were ignored.");

        if (totalEligible > 0m && income <= 0m)
            warnings.Add($"No capital-gains income in {taxYear} (poz. 26 = 0), so prior-year losses cannot be deducted this year. They keep carrying forward within their 5-year windows.");
        else if (totalEligible > income)
            warnings.Add($"Deductible losses ({Fmt(totalEligible)}) exceed this year's income ({Fmt(income)}); only {Fmt(totalApplied)} is entered in poz. 30. The deduction cannot create or increase a loss.");

        if (lines.Any(l => l.AppliedPln > 0m))
            warnings.Add("Only losses from the same source count: capital gains under art. 30b reported in PIT-38 of the loss year. Losses cannot reduce the flat tax on dividends and interest (art. 30a, poz. 47).");

        return new PriorYearLossResult(lines, totalApplied, warnings);
    }

    private static string Fmt(decimal value)
        => value.ToString("#,0.00", CultureInfo.InvariantCulture) + " zl";
}
