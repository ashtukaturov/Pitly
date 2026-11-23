using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>Which country a row is attributed to for PIT/ZG: the one the statement names, else the ISIN prefix.</summary>
public class CountryBreakdownTests
{
    [Theory]
    [InlineData(null, "US0378331005", "US")]
    [InlineData("JP", "US8923313071", "JP")]   // a depositary receipt: the named country wins over the ISIN
    [InlineData("ch", null, "CH")]              // interest without an ISIN, country named by the statement
    [InlineData(null, null, "??")]
    [InlineData("", "DE0007164600", "DE")]
    [InlineData("USA", "GB0002634946", "GB")]   // not a two-letter code: ignored, the ISIN decides
    public void ResolveCountryCode_PrefersTheNamedCountryOverTheIsinPrefix(string? country, string? isin, string expected)
    {
        Assert.Equal(expected, CountryBreakdown.ResolveCountryCode(country, isin));
    }

    [Fact]
    public void Build_GroupsByTheNamedCountryAndFallsBackToTheIsin()
    {
        var sales = new List<TradeResult>
        {
            new("AAPL", new DateTime(2024, 6, 20), TradeType.Sell, 10m, 175m, 1750m, 1.75m, "USD", 4m, 7000m, 6013m, 987m, Isin: "US0378331005", CountryCode: "US"),
            new("TM", new DateTime(2024, 7, 1), TradeType.Sell, 1m, 100m, 100m, 0m, "USD", 4m, 400m, 300m, 100m, Isin: "US8923313071", CountryCode: "JP"),
            new("SAP", new DateTime(2024, 8, 1), TradeType.Sell, 1m, 100m, 100m, 0m, "EUR", 4.5m, 450m, 450m, 0m, Isin: "DE0007164600")
        };
        var income = new List<Dividend>
        {
            new("SAVINGS-EUR", "EUR", new DateTime(2024, 12, 31), IncomeKind.Interest, 120m, 22.8m, 540m, 102.6m, 102.6m, null, 4.5m, CountryCode: "CH"),
            new("AAPL", "USD", new DateTime(2024, 5, 10), IncomeKind.Dividend, 25m, 3.75m, 100m, 15m, 15m, 0.15m, 4m, Isin: "US0378331005")
        };

        var countries = CountryBreakdown.Build(sales, income).ToDictionary(c => c.CountryCode);

        Assert.Equal(["CH", "DE", "JP", "US"], countries.Keys.Order());
        Assert.Equal(987m, countries["US"].IncomePln);
        Assert.Equal(100m, countries["US"].DividendsPln);
        Assert.Equal(100m, countries["JP"].IncomePln);        // not under US although the ISIN is American
        Assert.Equal(0m, countries["DE"].IncomePln);
        Assert.Equal(540m, countries["CH"].DividendsPln);     // without the country column this would be "??"
        Assert.Equal("Szwajcaria", countries["CH"].CountryName);
        Assert.Equal(1, countries["CH"].DividendCount);
    }
}
