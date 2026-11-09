namespace Pitly.Core.Models;

/// <summary>
/// Income attributed to one foreign country, as needed for the PIT/ZG annex of PIT-38 (section for art. 30b
/// income: dochód and tax paid abroad per country). The country is the issuer's country taken from the ISIN
/// prefix. Dividend figures are informational: for PIT-38 they are not entered in PIT/ZG, the foreign
/// withholding goes straight to poz. 48.
/// </summary>
public record CountryIncome(
    // ISO 3166-1 alpha-2 code from the ISIN prefix; "??" when the instrument had no ISIN
    string CountryCode,
    // Country name as written on the Polish form
    string CountryName,
    // Art. 30b (capital gains) figures for sales in the tax period
    decimal ProceedsPln,
    decimal CostPln,
    // Positive result -> dochód to enter in PIT/ZG; negative -> strata (not entered, shown for reference)
    decimal IncomePln,
    decimal LossPln,
    // Capital-gains tax withheld abroad on these sales (brokers in treaty countries normally withhold none)
    decimal TaxPaidAbroadPln,
    int SaleCount,
    // Art. 30a figures (dividends, interest, payments in lieu) for reference
    decimal DividendsPln,
    decimal WithholdingPln,
    decimal CreditableWithholdingPln,
    int DividendCount);
