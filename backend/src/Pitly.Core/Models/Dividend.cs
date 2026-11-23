namespace Pitly.Core.Models;

public record Dividend(
    string Symbol,
    string Currency,
    DateTime Date,
    IncomeKind Kind,
    decimal AmountOriginal,
    decimal WithholdingTaxOriginal,
    decimal AmountPln,
    decimal WithholdingTaxPln,
    // Part of WithholdingTaxPln that may actually be credited in Poland: capped at the treaty rate
    // for the issuer's country (art. 30a ust. 9 + relevant DTT), or at 19% when no treaty rate is known.
    decimal CreditableWithholdingPln,
    // Treaty cap applied (e.g. 0.15 for US), or null when the statutory 19% cap was used.
    decimal? MaxCreditableRate,
    decimal ExchangeRate,
    bool RateUnavailable = false,
    // Issuer ISIN, used to attribute the income to a country for PIT/ZG
    string? Isin = null,
    // Country named by the statement itself; takes precedence over the ISIN prefix for PIT/ZG
    string? CountryCode = null);
