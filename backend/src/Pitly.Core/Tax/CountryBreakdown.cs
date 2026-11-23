using Pitly.Core.Models;

namespace Pitly.Core.Tax;

/// <summary>
/// Groups the tax-period results by source country for the PIT/ZG annex: the country the statement names when it
/// has one (generic CSV "country" column), otherwise the issuer's country from the ISIN prefix.
/// </summary>
public static class CountryBreakdown
{
    public const string UnknownCountryCode = "??";

    // Names as they are written on PIT/ZG; the code is shown next to the name in the UI.
    private static readonly Dictionary<string, string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "Stany Zjednoczone Ameryki",
        ["CA"] = "Kanada",
        ["GB"] = "Wielka Brytania",
        ["IE"] = "Irlandia",
        ["DE"] = "Niemcy",
        ["FR"] = "Francja",
        ["NL"] = "Niderlandy",
        ["BE"] = "Belgia",
        ["LU"] = "Luksemburg",
        ["CH"] = "Szwajcaria",
        ["AT"] = "Austria",
        ["ES"] = "Hiszpania",
        ["IT"] = "Włochy",
        ["PT"] = "Portugalia",
        ["SE"] = "Szwecja",
        ["DK"] = "Dania",
        ["NO"] = "Norwegia",
        ["FI"] = "Finlandia",
        ["CZ"] = "Czechy",
        ["JP"] = "Japonia",
        ["KR"] = "Korea Południowa",
        ["CN"] = "Chiny",
        ["IN"] = "Indie",
        ["SG"] = "Singapur",
        ["IL"] = "Izrael",
        ["AU"] = "Australia",
        ["NZ"] = "Nowa Zelandia",
        ["HK"] = "Hongkong",
        ["TW"] = "Tajwan",
        ["BR"] = "Brazylia",
        ["MX"] = "Meksyk",
        ["ZA"] = "Republika Południowej Afryki",
        ["PL"] = "Polska",
        ["BM"] = "Bermudy",
        ["KY"] = "Kajmany",
        ["JE"] = "Jersey",
        ["GG"] = "Guernsey",
        ["VG"] = "Brytyjskie Wyspy Dziewicze",
        ["LI"] = "Liechtenstein",
        ["GR"] = "Grecja",
        ["HU"] = "Węgry",
        ["TR"] = "Turcja",
        [UnknownCountryCode] = "Nieznany kraj (brak ISIN)"
    };

    public static string GetCountryName(string code)
        => CountryNames.TryGetValue(code, out var name) ? name : code;

    public static List<CountryIncome> Build(IReadOnlyList<TradeResult> tradeResults, IReadOnlyList<Dividend> dividends)
    {
        var byCountry = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var sale in tradeResults.Where(t => t.Type == TradeType.Sell))
        {
            var acc = Get(byCountry, sale.CountryCode, sale.Isin);
            acc.Proceeds += sale.ProceedsPln;
            acc.Cost += sale.CostPln;
            acc.Sales++;
        }

        foreach (var dividend in dividends)
        {
            var acc = Get(byCountry, dividend.CountryCode, dividend.Isin);
            acc.Dividends += dividend.AmountPln;
            acc.Withholding += dividend.WithholdingTaxPln;
            acc.Creditable += dividend.CreditableWithholdingPln;
            acc.DividendCount++;
        }

        return byCountry
            .Select(kv =>
            {
                var acc = kv.Value;
                var result = acc.Proceeds - acc.Cost;
                return new CountryIncome(
                    CountryCode: kv.Key,
                    CountryName: GetCountryName(kv.Key),
                    ProceedsPln: Math.Round(acc.Proceeds, 2),
                    CostPln: Math.Round(acc.Cost, 2),
                    IncomePln: Math.Round(Math.Max(result, 0m), 2),
                    LossPln: Math.Round(Math.Max(-result, 0m), 2),
                    TaxPaidAbroadPln: 0m,
                    SaleCount: acc.Sales,
                    DividendsPln: Math.Round(acc.Dividends, 2),
                    WithholdingPln: Math.Round(acc.Withholding, 2),
                    CreditableWithholdingPln: Math.Round(acc.Creditable, 2),
                    DividendCount: acc.DividendCount);
            })
            .OrderByDescending(c => c.IncomePln)
            .ThenByDescending(c => c.DividendsPln)
            .ThenBy(c => c.CountryCode)
            .ToList();
    }

    /// <summary>
    /// The country a row is attributed to: the one the statement names, otherwise the ISIN prefix, otherwise
    /// <see cref="UnknownCountryCode"/>. The treaty cap on the credit is not affected: it follows the ISIN.
    /// </summary>
    public static string ResolveCountryCode(string? countryCode, string? isin)
    {
        var named = countryCode?.Trim();
        if (!string.IsNullOrEmpty(named) && named.Length == 2 && named.All(char.IsLetter))
            return named.ToUpperInvariant();
        return TreatyWithholdingRates.GetCountryCode(isin) ?? UnknownCountryCode;
    }

    private static Accumulator Get(Dictionary<string, Accumulator> byCountry, string? countryCode, string? isin)
    {
        var code = ResolveCountryCode(countryCode, isin);
        if (!byCountry.TryGetValue(code, out var acc))
        {
            acc = new Accumulator();
            byCountry[code] = acc;
        }

        return acc;
    }

    private sealed class Accumulator
    {
        public decimal Proceeds;
        public decimal Cost;
        public int Sales;
        public decimal Dividends;
        public decimal Withholding;
        public decimal Creditable;
        public int DividendCount;
    }
}
