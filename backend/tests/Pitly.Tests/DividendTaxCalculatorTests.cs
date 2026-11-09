using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

public class DividendTaxCalculatorTests
{
    [Fact]
    public async Task CalculateAsync_SumsMatchingWithholdingRowsForSameDividend()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 4m
        });
        var calculator = new DividendTaxCalculator(rateService);

        var dividends = new List<RawDividend>
        {
            new("META", "USD", new DateTime(2025, 12, 23), 10m, "US30303M1027")
        };
        var withholdingTaxes = new List<RawWithholdingTax>
        {
            new("META", "USD", new DateTime(2025, 12, 23), 1.0m, "US30303M1027"),
            new("META", "USD", new DateTime(2025, 12, 23), 0.5m, "US30303M1027")
        };

        var results = await calculator.CalculateAsync(dividends, withholdingTaxes);
        var dividend = Assert.Single(results);

        Assert.Equal(10m, dividend.AmountOriginal);
        Assert.Equal(1.5m, dividend.WithholdingTaxOriginal);
        Assert.Equal(40m, dividend.AmountPln);
        Assert.Equal(6m, dividend.WithholdingTaxPln);
        // 15% of 40 PLN = 6 PLN: fully creditable under the PL-US treaty.
        Assert.Equal(6m, dividend.CreditableWithholdingPln);
        Assert.Equal(0.15m, dividend.MaxCreditableRate);
    }

    [Fact]
    public async Task CalculateAsync_MatchesWithholdingByIncomeKind()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 4m
        });
        var calculator = new DividendTaxCalculator(rateService);

        var date = new DateTime(2025, 6, 14);
        var dividends = new List<RawDividend>
        {
            new("AAPL", "USD", date, 10m, "US0378331005", IncomeKind.Dividend),
            new("AAPL", "USD", date, 4m, "US0378331005", IncomeKind.PaymentInLieu),
            new("USD interest", "USD", new DateTime(2025, 3, 5), 5m, null, IncomeKind.Interest)
        };
        var withholdingTaxes = new List<RawWithholdingTax>
        {
            new("AAPL", "USD", date, 1.5m, "US0378331005", IncomeKind.Dividend),
            new("AAPL", "USD", date, 1.2m, "US0378331005", IncomeKind.PaymentInLieu)
        };

        var results = await calculator.CalculateAsync(dividends, withholdingTaxes);

        var dividend = Assert.Single(results, d => d.Kind == IncomeKind.Dividend);
        Assert.Equal(1.5m, dividend.WithholdingTaxOriginal);
        Assert.Equal(6m, dividend.CreditableWithholdingPln); // 15% of 40 PLN

        var pil = Assert.Single(results, d => d.Kind == IncomeKind.PaymentInLieu);
        Assert.Equal(1.2m, pil.WithholdingTaxOriginal);
        Assert.Equal(4.8m, pil.WithholdingTaxPln);
        Assert.Equal(2.4m, pil.CreditableWithholdingPln); // 30% withheld, only 15% of 16 PLN creditable

        var interest = Assert.Single(results, d => d.Kind == IncomeKind.Interest);
        Assert.Equal(20m, interest.AmountPln);
        Assert.Equal(0m, interest.WithholdingTaxPln);
        Assert.Null(interest.MaxCreditableRate);
    }

    [Fact]
    public async Task CalculateAsync_CapsCreditableWithholdingAtTreatyRate()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 4m,
            ["EUR"] = 4m
        });
        var calculator = new DividendTaxCalculator(rateService);

        var dividends = new List<RawDividend>
        {
            // US stock withheld at 30% (no W-8BEN on file)
            new("AAPL", "USD", new DateTime(2025, 5, 15), 100m, "US0378331005"),
            // German stock withheld at 26.375%
            new("SAP", "EUR", new DateTime(2025, 5, 20), 100m, "DE0007164600")
        };
        var withholdingTaxes = new List<RawWithholdingTax>
        {
            new("AAPL", "USD", new DateTime(2025, 5, 15), 30m, "US0378331005"),
            new("SAP", "EUR", new DateTime(2025, 5, 20), 26.375m, "DE0007164600")
        };

        var results = await calculator.CalculateAsync(dividends, withholdingTaxes);

        var apple = Assert.Single(results, d => d.Symbol == "AAPL");
        Assert.Equal(120m, apple.WithholdingTaxPln);
        Assert.Equal(60m, apple.CreditableWithholdingPln); // 15% of 400 PLN
        Assert.Equal(0.15m, apple.MaxCreditableRate);

        var sap = Assert.Single(results, d => d.Symbol == "SAP");
        Assert.Equal(105.5m, sap.WithholdingTaxPln);
        Assert.Equal(60m, sap.CreditableWithholdingPln); // 15% of 400 PLN
        Assert.Equal(0.15m, sap.MaxCreditableRate);
    }

    [Fact]
    public async Task CalculateAsync_FallsBackToStatutoryCapWithoutTreatyRate()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 1m
        });
        var calculator = new DividendTaxCalculator(rateService);

        var dividends = new List<RawDividend>
        {
            // No ISIN at all: fall back to the statutory 19% cap (art. 30a ust. 9)
            new("XYZ", "USD", new DateTime(2025, 5, 15), 100m),
            // Bermuda ISIN: no treaty with Poland, statutory cap applies
            new("BMCO", "USD", new DateTime(2025, 5, 16), 100m, "BMG000000000")
        };
        var withholdingTaxes = new List<RawWithholdingTax>
        {
            new("XYZ", "USD", new DateTime(2025, 5, 15), 25m),
            new("BMCO", "USD", new DateTime(2025, 5, 16), 10m, "BMG000000000")
        };

        var results = await calculator.CalculateAsync(dividends, withholdingTaxes);

        var noIsin = Assert.Single(results, d => d.Symbol == "XYZ");
        Assert.Equal(19m, noIsin.CreditableWithholdingPln);
        Assert.Null(noIsin.MaxCreditableRate);

        var bermuda = Assert.Single(results, d => d.Symbol == "BMCO");
        Assert.Equal(10m, bermuda.CreditableWithholdingPln);
        Assert.Null(bermuda.MaxCreditableRate);
    }

    [Fact]
    public async Task CalculateAsync_NetsNegativeWithholdingCorrectionsAgainstTheOriginal()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m });
        var calculator = new DividendTaxCalculator(rateService);
        var date = new DateTime(2025, 2, 5);

        var dividends = new List<RawDividend> { new("BND", "USD", date, 44.61m, "US9219378356") };
        var taxes = new List<RawWithholdingTax>
        {
            new("BND", "USD", date, 6.69m, "US9219378356"),   // withheld, from the 2025 statement
            new("BND", "USD", date, -6.16m, "US9219378356")   // reversal and re-charge, from the 2026 statement
        };

        var result = Assert.Single(await calculator.CalculateAsync(dividends, taxes));

        Assert.Equal(0.53m, result.WithholdingTaxOriginal);
        Assert.Equal(2.12m, result.WithholdingTaxPln);
        Assert.Equal(2.12m, result.CreditableWithholdingPln);

        // A correction larger than the original cannot produce a negative credit.
        var refunded = Assert.Single(await calculator.CalculateAsync(
            dividends,
            [new("BND", "USD", date, 6.69m, "US9219378356"), new("BND", "USD", date, -6.69m, "US9219378356")]));
        Assert.Equal(0m, refunded.WithholdingTaxPln);
    }
}
