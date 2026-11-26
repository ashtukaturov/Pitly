namespace Pitly.Core.Models;

/// <summary>
/// A payment the user classified as income from other sources (art. 20 ust. 1 ustawy o PIT): it is declared in
/// PIT-36 at the tax scale, not in PIT-38, so it is left out of section G and listed here with the foreign tax
/// withheld on it. Pitly does not compute the tax at the scale.
/// </summary>
public record OtherSourceIncome(
    string Symbol,
    string Currency,
    DateTime Date,
    IncomeKind Kind,
    decimal AmountOriginal,
    decimal AmountPln,
    decimal WithholdingTaxPln,
    decimal ExchangeRate,
    string? Isin = null,
    string? CountryCode = null,
    string? AccountId = null,
    string? SourceFile = null);
