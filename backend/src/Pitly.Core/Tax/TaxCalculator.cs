using Pitly.Core.Models;
using Pitly.Core.Parsing;

namespace Pitly.Core.Tax;

public interface ITaxCalculator
{
    Task<TaxSummary> CalculateAsync(ParsedStatement statement, TaxPeriod taxPeriod, CreditMethod creditMethod = CreditMethod.TreatyCap, TaxDecisions? decisions = null);
}

/// <summary>
/// Runs the two calculators over the merged history and assembles the summary. The order matters: every
/// withholding event is matched over the whole history first, the tax year is selected afterwards, and nothing is
/// rounded here, so that <see cref="Pit38Fields.FromSummary"/> works from exact amounts.
/// </summary>
public class TaxCalculator : ITaxCalculator
{
    private readonly ICapitalGainsTaxCalculator _capitalGainsCalculator;
    private readonly IDividendTaxCalculator _dividendTaxCalculator;

    public TaxCalculator(
        ICapitalGainsTaxCalculator capitalGainsCalculator,
        IDividendTaxCalculator dividendTaxCalculator)
    {
        _capitalGainsCalculator = capitalGainsCalculator;
        _dividendTaxCalculator = dividendTaxCalculator;
    }

    public async Task<TaxSummary> CalculateAsync(ParsedStatement statement, TaxPeriod taxPeriod, CreditMethod creditMethod = CreditMethod.TreatyCap, TaxDecisions? decisions = null)
    {
        decisions ??= TaxDecisions.Default;
        // The two calculators run concurrently, so each gets its own list. What they report puts the result itself
        // in doubt (history that does not add up, withholding that could not be attributed) and is therefore kept
        // apart from the informational statement warnings: it is repeated on PIT-38 and in the printed report.
        var capitalGainWarnings = new List<string>();
        var tradeDiagnostics = new List<TaxDiagnostic>();
        var dividendDiagnostics = new List<TaxDiagnostic>();
        var tradeResultsTask = _capitalGainsCalculator.CalculateAsync(statement, taxPeriod, capitalGainWarnings, tradeDiagnostics, decisions.DateBasis);
        var dividendsTask = _dividendTaxCalculator.CalculateAsync(
            statement.Dividends, statement.WithholdingTaxes, taxPeriod, creditMethod, dividendDiagnostics, decisions);
        await Task.WhenAll(tradeResultsTask, dividendsTask);
        var tradeResults = tradeResultsTask.Result;
        var dividendResult = dividendsTask.Result;
        var dividends = dividendResult.Dividends;

        var diagnostics = new List<TaxDiagnostic>();
        foreach (var warning in capitalGainWarnings)
        {
            diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.PositionCheck, DiagnosticSeverity.Warning, warning,
                AffectedYears: [taxPeriod.Year], AffectsAmounts: true));
        }
        diagnostics.AddRange(tradeDiagnostics);
        diagnostics.AddRange(dividendDiagnostics);
        // Findings of the parsers and the merger that name the years they can change (a conflict on a purchase of
        // a later year, an unsupported row of another year) are shown only for those years; one without years is
        // shown for every year.
        diagnostics.AddRange((statement.Diagnostics ?? []).Where(d => d.AffectedYears is not { Count: > 0 } years || years.Contains(taxPeriod.Year)));
        foreach (var warning in (statement.Warnings ?? []).Where(w => w.StartsWith(StatementMerger.AccountCheckPrefix, StringComparison.Ordinal)))
        {
            if (diagnostics.All(d => d.Message != warning))
                diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.AccountAssumed, DiagnosticSeverity.Warning, warning, AffectsAmounts: true));
        }

        var interest = dividends.Where(d => d.Kind == IncomeKind.Interest).ToList();
        var totalDividendsPln = dividends.Sum(d => d.AmountPln);
        var totalInterestPln = interest.Sum(d => d.AmountPln);
        if (totalInterestPln > 0m && totalDividendsPln - totalInterestPln > 0m)
        {
            diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.MixedStreamRounding, DiagnosticSeverity.Info,
                "This year has both dividends and interest. The form has one field for both; each stream is rounded by its own rule " +
                "(full PLN for dividends, grosz upward for interest) and the two results are added, because the Ministry gives no rule for the mix. " +
                "Rounding policy " + RoundingPolicy.Current.Version + ".",
                AffectedYears: [taxPeriod.Year]));
        }

        var resultWarnings = diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.Message)
            .Distinct()
            .ToList();
        var warnings = (statement.Warnings ?? []).Concat(resultWarnings).Distinct().ToList();

        var sellResults = tradeResults.Where(t => t.Type == TradeType.Sell).ToList();
        var totalProceedsPln = sellResults.Sum(t => t.ProceedsPln);
        var totalCostPln = sellResults.Sum(t => t.CostPln);
        var capitalGain = totalProceedsPln - totalCostPln;
        var capitalGainTax = capitalGain > 0 ? capitalGain * TaxConstants.TaxRate : 0;

        var totalWithholdingPln = dividends.Sum(d => d.WithholdingTaxPln);
        var totalCreditableWithholdingPln = dividends.Sum(d => d.CreditableWithholdingPln);
        var polishDividendTax = totalDividendsPln * TaxConstants.TaxRate;
        var withholdingCredit = Math.Min(totalCreditableWithholdingPln, polishDividendTax);
        var dividendTaxOwed = Math.Max(polishDividendTax - withholdingCredit, 0);

        var countries = CountryBreakdown.Build(tradeResults, dividends);

        // Full precision everywhere: the form's rounding is applied once, in Pit38Fields.FromSummary.
        return new TaxSummary(
            TotalProceedsPln: totalProceedsPln,
            TotalCostPln: totalCostPln,
            CapitalGainPln: capitalGain,
            CapitalGainTaxPln: capitalGainTax,
            TotalDividendsPln: totalDividendsPln,
            TotalWithholdingPln: totalWithholdingPln,
            TotalCreditableWithholdingPln: totalCreditableWithholdingPln,
            DividendTaxOwedPln: dividendTaxOwed,
            Year: taxPeriod.Year,
            TaxableFrom: taxPeriod.TaxableFrom,
            TaxableTo: taxPeriod.TaxableTo,
            TradeResults: tradeResults,
            Dividends: dividends,
            Warnings: warnings,
            Countries: countries,
            TotalInterestPln: totalInterestPln,
            TotalInterestCreditableWithholdingPln: interest.Sum(d => d.CreditableWithholdingPln),
            ResultWarnings: resultWarnings,
            Diagnostics: diagnostics,
            WithholdingEvents: dividendResult.Events,
            CreditMethod: creditMethod,
            Decisions: decisions,
            OtherSourcesIncome: dividendResult.OtherSources ?? []);
    }
}
