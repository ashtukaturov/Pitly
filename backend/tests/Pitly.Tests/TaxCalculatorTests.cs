using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

public class TaxCalculatorTests
{
    [Fact]
    public async Task CalculateAsync_UsesHistoricalTradesButOnlyCountsTargetYear()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 1m
        });
        var calculator = new TaxCalculator(
            new CapitalGainsTaxCalculator(rateService),
            new DividendTaxCalculator(rateService));

        var statement = new ParsedStatement(
            Trades:
            [
                new(
                    Symbol: "ABC",
                    Currency: "USD",
                    DateTime: new DateTime(2024, 12, 20, 10, 0, 0),
                    Quantity: 10m,
                    Price: 10m,
                    Proceeds: 100m,
                    Commission: 0m,
                    CommissionCurrency: "USD",
                    RealizedPnL: 0m,
                    Type: TradeType.Buy,
                    Isin: "US1111111111"),
                new(
                    Symbol: "ABC",
                    Currency: "USD",
                    DateTime: new DateTime(2025, 3, 10, 10, 0, 0),
                    Quantity: 5m,
                    Price: 20m,
                    Proceeds: 100m,
                    Commission: 0m,
                    CommissionCurrency: "USD",
                    RealizedPnL: 0m,
                    Type: TradeType.Sell,
                    Isin: "US1111111111")
            ],
            Dividends:
            [
                new("ABC", "USD", new DateTime(2024, 6, 1), 1m, "US1111111111"),
                new("ABC", "USD", new DateTime(2025, 6, 1), 2m, "US1111111111")
            ],
            WithholdingTaxes:
            [
                new("ABC", "USD", new DateTime(2024, 6, 1), 0.1m, "US1111111111"),
                new("ABC", "USD", new DateTime(2025, 6, 1), 0.3m, "US1111111111")
            ]);

        var summary = await calculator.CalculateAsync(statement, TaxPeriod.FullYear(2025));

        Assert.Equal(2025, summary.Year);
        Assert.Equal(new DateTime(2025, 1, 1), summary.TaxableFrom);
        Assert.Equal(new DateTime(2025, 12, 31), summary.TaxableTo);
        Assert.Equal(0.3m, summary.TotalCreditableWithholdingPln);
        Assert.Equal(100m, summary.TotalProceedsPln);
        Assert.Equal(50m, summary.TotalCostPln);
        Assert.Equal(50m, summary.CapitalGainPln);
        Assert.Equal(9.5m, summary.CapitalGainTaxPln);
        Assert.Equal(2m, summary.TotalDividendsPln);
        Assert.Equal(0.3m, summary.TotalWithholdingPln);
        Assert.Equal(0.08m, summary.DividendTaxOwedPln);

        var sell = Assert.Single(summary.TradeResults);
        Assert.Equal(TradeType.Sell, sell.Type);
        Assert.Single(summary.Dividends);
    }

    [Fact]
    public async Task CalculateAsync_ReplaysPreResidencyHistoryButReportsOnlyTaxableWindow()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 1m
        });
        var calculator = new TaxCalculator(
            new CapitalGainsTaxCalculator(rateService),
            new DividendTaxCalculator(rateService));

        var statement = new ParsedStatement(
            Trades:
            [
                new(
                    Symbol: "ABC",
                    Currency: "USD",
                    DateTime: new DateTime(2025, 1, 10, 10, 0, 0),
                    Quantity: 10m,
                    Price: 10m,
                    Proceeds: 100m,
                    Commission: 0m,
                    CommissionCurrency: "USD",
                    RealizedPnL: 0m,
                    Type: TradeType.Buy,
                    Isin: "US1111111111"),
                new(
                    Symbol: "ABC",
                    Currency: "USD",
                    DateTime: new DateTime(2025, 3, 15, 10, 0, 0),
                    Quantity: 5m,
                    Price: 12m,
                    Proceeds: 60m,
                    Commission: 0m,
                    CommissionCurrency: "USD",
                    RealizedPnL: 0m,
                    Type: TradeType.Buy,
                    Isin: "US1111111111"),
                new(
                    Symbol: "ABC",
                    Currency: "USD",
                    DateTime: new DateTime(2025, 4, 2, 10, 0, 0),
                    Quantity: 8m,
                    Price: 15m,
                    Proceeds: 120m,
                    Commission: 0m,
                    CommissionCurrency: "USD",
                    RealizedPnL: 0m,
                    Type: TradeType.Sell,
                    Isin: "US1111111111"),
                new(
                    Symbol: "ABC",
                    Currency: "USD",
                    DateTime: new DateTime(2025, 7, 8, 10, 0, 0),
                    Quantity: 7m,
                    Price: 20m,
                    Proceeds: 140m,
                    Commission: 0m,
                    CommissionCurrency: "USD",
                    RealizedPnL: 0m,
                    Type: TradeType.Sell,
                    Isin: "US1111111111")
            ],
            Dividends:
            [
                new("ABC", "USD", new DateTime(2025, 4, 5), 1m, "US1111111111"),
                new("ABC", "USD", new DateTime(2025, 8, 5), 2m, "US1111111111")
            ],
            WithholdingTaxes:
            [
                new("ABC", "USD", new DateTime(2025, 4, 5), 0.15m, "US1111111111"),
                new("ABC", "USD", new DateTime(2025, 8, 5), 0.3m, "US1111111111")
            ]);

        var summary = await calculator.CalculateAsync(
            statement,
            new TaxPeriod(2025, new DateTime(2025, 6, 1), new DateTime(2025, 12, 31)));

        Assert.Equal(2025, summary.Year);
        Assert.Equal(new DateTime(2025, 6, 1), summary.TaxableFrom);
        Assert.Equal(new DateTime(2025, 12, 31), summary.TaxableTo);
        Assert.Equal(140m, summary.TotalProceedsPln);
        Assert.Equal(80m, summary.TotalCostPln);
        Assert.Equal(60m, summary.CapitalGainPln);
        Assert.Equal(11.4m, summary.CapitalGainTaxPln);
        Assert.Equal(2m, summary.TotalDividendsPln);
        Assert.Equal(0.3m, summary.TotalWithholdingPln);
        Assert.Equal(0.08m, summary.DividendTaxOwedPln);

        var sell = Assert.Single(summary.TradeResults);
        Assert.Equal(TradeType.Sell, sell.Type);
        Assert.Equal(new DateTime(2025, 7, 8, 10, 0, 0), sell.DateTime);

        var dividend = Assert.Single(summary.Dividends);
        Assert.Equal(new DateTime(2025, 8, 5), dividend.Date);
    }

    [Fact]
    public async Task CalculateAsync_OffsetsOnlyTreatyCappedWithholdingAgainstDividendTax()
    {
        var rateService = new TestRateService(new Dictionary<string, decimal>
        {
            ["USD"] = 1m
        });
        var calculator = new TaxCalculator(
            new CapitalGainsTaxCalculator(rateService),
            new DividendTaxCalculator(rateService));

        var statement = new ParsedStatement(
            Trades: [],
            Dividends:
            [
                new("AAPL", "USD", new DateTime(2025, 5, 15), 100m, "US0378331005")
            ],
            WithholdingTaxes:
            [
                // 30% withheld without a W-8BEN: only 15% is creditable
                new("AAPL", "USD", new DateTime(2025, 5, 15), 30m, "US0378331005")
            ]);

        var summary = await calculator.CalculateAsync(statement, TaxPeriod.FullYear(2025));

        Assert.Equal(100m, summary.TotalDividendsPln);
        Assert.Equal(30m, summary.TotalWithholdingPln);
        Assert.Equal(15m, summary.TotalCreditableWithholdingPln);
        // 19 PLN Polish tax - 15 PLN creditable = 4 PLN owed (not 0 as a naive 19% cap would give)
        Assert.Equal(4m, summary.DividendTaxOwedPln);

        var pit38 = Pit38Fields.FromSummary(summary);
        Assert.Equal(19m, pit38.ZryczaltowanyPodatek);
        Assert.Equal(15m, pit38.PodatekZaplaconyZaGranica);
        Assert.Equal(4m, pit38.Roznica);
    }

    [Fact]
    public void Pit38Fields_DeductsPriorYearLossCappedAtIncome()
    {
        var summary = BuildSummary(proceeds: 1000m, cost: 400m, priorYearLoss: 200m);

        var pit38 = Pit38Fields.FromSummary(summary);

        Assert.Equal(600m, pit38.Dochod);
        Assert.Equal(200m, pit38.StratyZLatUbieglych);
        Assert.Equal(400m, pit38.PodstawaObliczenia);
        Assert.Equal(76m, pit38.Podatek);
        Assert.Equal(76m, pit38.PodatekNalezny);
        Assert.Equal(0m, pit38.DaninaSolidarnosciowaPln);

        // A loss larger than this year's income is capped: the base cannot go negative.
        var capped = Pit38Fields.FromSummary(BuildSummary(proceeds: 1000m, cost: 400m, priorYearLoss: 5000m));
        Assert.Equal(600m, capped.StratyZLatUbieglych);
        Assert.Equal(0m, capped.PodstawaObliczenia);
        Assert.Equal(0m, capped.PodatekDoZaplaty);
    }

    [Fact]
    public void Pit38Fields_ComputesSolidarityLevyAboveOneMillion()
    {
        var summary = BuildSummary(proceeds: 2_600_000m, cost: 1_000_000m, priorYearLoss: 100_000m);

        var pit38 = Pit38Fields.FromSummary(summary);

        // 1 600 000 income - 100 000 loss = 1 500 000 base; excess over 1 000 000 = 500 000; 4% = 20 000
        Assert.Equal(1_500_000m, pit38.PodstawaObliczenia);
        Assert.Equal(500_000m, pit38.DaninaSolidarnosciowaNadwyzka);
        Assert.Equal(20_000m, pit38.DaninaSolidarnosciowaPln);
    }

    private static TaxSummary BuildSummary(decimal proceeds, decimal cost, decimal priorYearLoss)
    {
        var gain = proceeds - cost;
        return new TaxSummary(
            TotalProceedsPln: proceeds,
            TotalCostPln: cost,
            CapitalGainPln: gain,
            CapitalGainTaxPln: gain > 0 ? Math.Round(gain * 0.19m, 2) : 0m,
            TotalDividendsPln: 0m,
            TotalWithholdingPln: 0m,
            TotalCreditableWithholdingPln: 0m,
            DividendTaxOwedPln: 0m,
            Year: 2025,
            TaxableFrom: new DateTime(2025, 1, 1),
            TaxableTo: new DateTime(2025, 12, 31),
            TradeResults: [],
            Dividends: [],
            PriorYearLosses: priorYearLoss > 0 ? [new PriorYearLoss(2023, priorYearLoss)] : []);
    }

    [Fact]
    public async Task CalculateAsync_BreaksDownIncomeByIssuerCountryForPitZg()
    {
        var rates = new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m });
        var calculator = new TaxCalculator(new CapitalGainsTaxCalculator(rates), new DividendTaxCalculator(rates));
        var statement = new ParsedStatement(
            Trades:
            [
                new Trade("AAPL", "USD", new DateTime(2025, 1, 10), 10m, 100m, 1000m, 0m, "USD", 0m, TradeType.Buy, "US0378331005"),
                new Trade("AAPL", "USD", new DateTime(2025, 6, 10), 10m, 150m, 1500m, 0m, "USD", 0m, TradeType.Sell, "US0378331005"),
                new Trade("SAP", "USD", new DateTime(2025, 2, 10), 5m, 200m, 1000m, 0m, "USD", 0m, TradeType.Buy, "DE0007164600"),
                new Trade("SAP", "USD", new DateTime(2025, 7, 10), 5m, 180m, 900m, 0m, "USD", 0m, TradeType.Sell, "DE0007164600")
            ],
            Dividends: [new RawDividend("AAPL", "USD", new DateTime(2025, 5, 15), 10m, "US0378331005")],
            WithholdingTaxes: [new RawWithholdingTax("AAPL", "USD", new DateTime(2025, 5, 15), 1.5m, "US0378331005")]);

        var summary = await calculator.CalculateAsync(statement, TaxPeriod.FullYear(2025));
        var pitZg = Pit38Fields.FromSummary(summary).PitZg;

        Assert.Equal(2, pitZg.Count);
        var us = pitZg.Single(c => c.CountryCode == "US");
        Assert.Equal("Stany Zjednoczone Ameryki", us.CountryName);
        Assert.Equal(6000m, us.ProceedsPln);
        Assert.Equal(4000m, us.CostPln);
        Assert.Equal(2000m, us.IncomePln);
        Assert.Equal(0m, us.LossPln);
        Assert.Equal(40m, us.DividendsPln);
        Assert.Equal(6m, us.WithholdingPln);
        var de = pitZg.Single(c => c.CountryCode == "DE");
        Assert.Equal(0m, de.IncomePln);
        Assert.Equal(400m, de.LossPln);
    }
}
