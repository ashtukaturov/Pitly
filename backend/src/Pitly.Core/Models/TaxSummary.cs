namespace Pitly.Core.Models;

public record TaxSummary(
    decimal TotalProceedsPln,
    decimal TotalCostPln,
    decimal CapitalGainPln,
    decimal CapitalGainTaxPln,
    decimal TotalDividendsPln,
    decimal TotalWithholdingPln,
    decimal TotalCreditableWithholdingPln,
    decimal DividendTaxOwedPln,
    int Year,
    DateTime TaxableFrom,
    DateTime TaxableTo,
    List<TradeResult> TradeResults,
    List<Dividend> Dividends,
    // Prior-year capital losses entered by the user; checked and applied to poz. 30 by PriorYearLossCalculator.
    IReadOnlyList<PriorYearLoss>? PriorYearLosses = null,
    // Parser/calculation warnings the user should review (corporate actions treated conservatively, ignored rows).
    IReadOnlyList<string>? Warnings = null,
    // Income by issuer country for the PIT/ZG annex.
    IReadOnlyList<CountryIncome>? Countries = null);
