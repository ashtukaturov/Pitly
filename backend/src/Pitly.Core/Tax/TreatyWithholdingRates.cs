namespace Pitly.Core.Tax;

/// <summary>
/// Maximum dividend withholding rate that a Polish tax resident may credit against the 19% flat tax
/// (art. 30a ust. 9 ustawy o PIT), as limited by the double-tax treaty with the issuer's country.
/// Tax withheld above the treaty rate (e.g. 30% US withholding without a W-8BEN, or 26.375% German
/// withholding) is not creditable in Poland and has to be reclaimed from the source country instead.
///
/// The issuer country is derived from the ISIN prefix. This is a reasonable proxy for ordinary shares and
/// ETFs, but not for depositary receipts (an ADR carries a US ISIN while the dividend is taxed in the
/// underlying company's country). Rates are portfolio (non-substantial holding) rates from the respective
/// Poland treaties; verify against the current treaty text before relying on them for edge cases.
/// </summary>
public static class TreatyWithholdingRates
{
    private static readonly Dictionary<string, decimal> PortfolioDividendRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = 0.15m, // Poland–USA (1974)
        ["CA"] = 0.15m, // Poland–Canada (2012)
        ["GB"] = 0.10m, // Poland–United Kingdom (2006)
        ["IE"] = 0.15m, // Poland–Ireland (1995)
        ["DE"] = 0.15m, // Poland–Germany (2003)
        ["FR"] = 0.15m, // Poland–France (1975)
        ["NL"] = 0.15m, // Poland–Netherlands (2002)
        ["BE"] = 0.10m, // Poland–Belgium (2001)
        ["LU"] = 0.15m, // Poland–Luxembourg (1995, protocol 2012)
        ["CH"] = 0.15m, // Poland–Switzerland (1991, protocol 2010)
        ["AT"] = 0.15m, // Poland–Austria (2004)
        ["ES"] = 0.15m, // Poland–Spain (1979)
        ["IT"] = 0.10m, // Poland–Italy (1985)
        ["PT"] = 0.15m, // Poland–Portugal (1995)
        ["SE"] = 0.15m, // Poland–Sweden (2004)
        ["DK"] = 0.15m, // Poland–Denmark (2001)
        ["NO"] = 0.15m, // Poland–Norway (2009)
        ["FI"] = 0.15m, // Poland–Finland (2009)
        ["CZ"] = 0.05m, // Poland–Czech Republic (2011)
        ["JP"] = 0.10m, // Poland–Japan (1980)
        ["KR"] = 0.10m, // Poland–South Korea (1991)
        ["CN"] = 0.10m, // Poland–China (1988)
        ["IN"] = 0.10m, // Poland–India (1989, protocol 2013)
        ["SG"] = 0.10m, // Poland–Singapore (2012)
        ["IL"] = 0.10m, // Poland–Israel (1991)
        ["AU"] = 0.15m, // Poland–Australia (1991)
    };

    /// <summary>
    /// Returns the treaty cap for the issuer country encoded in the ISIN, or null when the ISIN is
    /// missing or the country has no treaty rate on file. Callers fall back to the statutory 19% cap.
    /// </summary>
    public static decimal? GetMaxCreditableRate(string? isin)
    {
        var country = GetCountryCode(isin);
        if (country is null)
            return null;

        return PortfolioDividendRates.TryGetValue(country, out var rate) ? rate : null;
    }

    public static string? GetCountryCode(string? isin)
    {
        if (string.IsNullOrWhiteSpace(isin) || isin.Length < 2)
            return null;

        var prefix = isin.Trim()[..2];
        return prefix.All(char.IsLetter) ? prefix.ToUpperInvariant() : null;
    }
}
