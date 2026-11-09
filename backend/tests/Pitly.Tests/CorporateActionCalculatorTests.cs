using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

public class CorporateActionCalculatorTests
{
    private static CapitalGainsTaxCalculator NewCalculator(decimal usdRate = 1m)
        => new(new TestRateService(new Dictionary<string, decimal> { ["USD"] = usdRate }));

    private static Trade Buy(string symbol, string isin, DateTime when, decimal qty, decimal price)
        => new(symbol, "USD", when, qty, price, qty * price, 0m, "USD", 0m, TradeType.Buy, isin);

    private static Trade Sell(string symbol, string isin, DateTime when, decimal qty, decimal price)
        => new(symbol, "USD", when, qty, price, qty * price, 0m, "USD", 0m, TradeType.Sell, isin);

    [Fact]
    public async Task ReverseSplitWithNewIsin_ReKeysAndScalesLots()
    {
        // 20 BOIL bought at 10 under the old ISIN; 1-for-5 reverse split issues a new ISIN; 4 new shares sold at 60.
        var statement = new ParsedStatement(
            Trades:
            [
                Buy("BOIL", "US74347Y7638", new DateTime(2024, 2, 7, 15, 0, 0), 20m, 10m),
                Sell("BOIL", "US74347Y7489", new DateTime(2025, 3, 1, 10, 0, 0), 4m, 60m)
            ],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions:
            [
                new CorporateAction(
                    Symbol: "BOIL",
                    DateTime: new DateTime(2024, 11, 6, 20, 25, 0),
                    Type: CorporateActionType.StockSplit,
                    Numerator: 1m,
                    Denominator: 5m,
                    Isin: "US74347Y7489",
                    SourceSymbol: "BOIL.OLD",
                    SourceIsin: "US74347Y7638")
            ]);

        var results = await NewCalculator().CalculateAsync(statement, TaxPeriod.FullYear(2025));

        var sell = Assert.Single(results);
        Assert.Equal(240m, sell.ProceedsPln);
        Assert.Equal(200m, sell.CostPln); // whole original cost (20 x 10) now sits in 4 shares
        Assert.Equal(40m, sell.GainLossPln);
    }

    [Fact]
    public async Task IsinChange_MovesLotsWithoutChangingCost()
    {
        var statement = new ParsedStatement(
            Trades:
            [
                Buy("IPL", "AU000000IPL1", new DateTime(2021, 3, 1), 100m, 3m),
                Sell("IPL", "AU0000390544", new DateTime(2025, 2, 1), 100m, 4m)
            ],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions:
            [
                new CorporateAction("IPL", new DateTime(2021, 6, 1), CorporateActionType.IsinChange, 1m, 1m,
                    Isin: "AU0000390544", SourceSymbol: "IPL.OLD", SourceIsin: "AU000000IPL1")
            ]);

        var results = await NewCalculator().CalculateAsync(statement, TaxPeriod.FullYear(2025));

        var sell = Assert.Single(results);
        Assert.Equal(400m, sell.ProceedsPln);
        Assert.Equal(300m, sell.CostPln);
    }

    [Fact]
    public async Task CashMerger_IsReportedAsSaleOfAllShares()
    {
        var statement = new ParsedStatement(
            Trades: [Buy("ATVI", "US00507V1098", new DateTime(2023, 1, 10), 15m, 75m)],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions:
            [
                new CorporateAction("ATVI", new DateTime(2023, 10, 13, 20, 25, 0), CorporateActionType.CashMerger, 1m, 1m,
                    Isin: "US00507V1098", Quantity: 15m, CashProceeds: 1425m, Currency: "USD")
            ]);

        var results = await NewCalculator(usdRate: 4m).CalculateAsync(statement, TaxPeriod.FullYear(2023));

        Assert.Equal(2, results.Count);
        var sale = results.Single(r => r.Type == TradeType.Sell);
        Assert.Equal("ATVI", sale.Symbol);
        Assert.Equal(15m, sale.Quantity);
        Assert.Equal(95m, sale.PriceOriginal);
        Assert.Equal(1425m * 4m, sale.ProceedsPln);
        Assert.Equal(1125m * 4m, sale.CostPln);
        Assert.Equal(300m * 4m, sale.GainLossPln);
        Assert.Contains("Cash merger", sale.Note);
    }

    [Fact]
    public async Task StockMerger_CarriesCostOverAtExchangeRatio()
    {
        // 10 OLDCO at 50 become 5 NEWCO; selling 5 NEWCO at 150 realises 750 - 500.
        var statement = new ParsedStatement(
            Trades:
            [
                Buy("OLDCO", "US1111111111", new DateTime(2022, 3, 1), 10m, 50m),
                Sell("NEWCO", "US2222222222", new DateTime(2025, 5, 5), 5m, 150m)
            ],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions:
            [
                new CorporateAction("NEWCO", new DateTime(2022, 9, 1), CorporateActionType.StockMerger, 1m, 2m,
                    Isin: "US2222222222", SourceSymbol: "OLDCO", SourceIsin: "US1111111111", Quantity: 5m)
            ]);

        var results = await NewCalculator().CalculateAsync(statement, TaxPeriod.FullYear(2025));

        var sell = Assert.Single(results);
        Assert.Equal(750m, sell.ProceedsPln);
        Assert.Equal(500m, sell.CostPln);
        Assert.Equal(250m, sell.GainLossPln);
    }

    [Fact]
    public async Task SpinOff_AddsZeroCostLotAndParentKeepsItsCost()
    {
        var statement = new ParsedStatement(
            Trades:
            [
                Buy("GE", "US3696043013", new DateTime(2022, 5, 2), 30m, 80m),
                Sell("GEHC", "US36266G1076", new DateTime(2023, 6, 1), 10m, 70m),
                Sell("GE", "US3696043013", new DateTime(2023, 7, 1), 30m, 100m)
            ],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions:
            [
                new CorporateAction("GEHC", new DateTime(2023, 1, 3, 20, 25, 0), CorporateActionType.SpinOff, 1m, 3m,
                    Isin: "US36266G1076", SourceSymbol: "GE", SourceIsin: "US3696043013", Quantity: 10m, Currency: "USD")
            ]);

        var results = await NewCalculator().CalculateAsync(statement, TaxPeriod.FullYear(2023));

        Assert.Equal(3, results.Count);
        var received = results.Single(r => r.Type == TradeType.Buy);
        Assert.Equal("GEHC", received.Symbol);
        Assert.Equal(0m, received.CostPln);
        Assert.Contains("Spin-off", received.Note);

        var childSale = results.Single(r => r.Symbol == "GEHC" && r.Type == TradeType.Sell);
        Assert.Equal(700m, childSale.ProceedsPln);
        Assert.Equal(0m, childSale.CostPln);

        var parentSale = results.Single(r => r.Symbol == "GE" && r.Type == TradeType.Sell);
        Assert.Equal(3000m, parentSale.ProceedsPln);
        Assert.Equal(2400m, parentSale.CostPln);
    }

    [Fact]
    public async Task CashInLieu_SellsTheFractionWhenLotsCoverIt()
    {
        // 7 shares, 1-for-2 reverse split -> 3.5 shares; the 0.5 fraction is paid out as 25 USD.
        var statement = new ParsedStatement(
            Trades:
            [
                Buy("XYZ", "US4444444444", new DateTime(2024, 1, 10), 7m, 20m),
                Sell("XYZ", "US4444444444", new DateTime(2025, 3, 1), 3m, 50m)
            ],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions:
            [
                new CorporateAction("XYZ", new DateTime(2025, 2, 1), CorporateActionType.StockSplit, 1m, 2m, Isin: "US4444444444"),
                new CorporateAction("XYZ", new DateTime(2025, 2, 1, 0, 0, 1), CorporateActionType.CashInLieu, 1m, 1m,
                    Isin: "US4444444444", Quantity: 0.5m, CashProceeds: 25m, Currency: "USD")
            ]);

        var results = await NewCalculator().CalculateAsync(statement, TaxPeriod.FullYear(2025));

        Assert.Equal(2, results.Count);
        var fraction = results.Single(r => r.Quantity == 0.5m);
        Assert.Equal(25m, fraction.ProceedsPln);
        Assert.Equal(20m, fraction.CostPln); // 0.5 post-split shares carry 0.5 x 40 of cost
        Assert.Contains("Cash in lieu", fraction.Note);

        var sale = results.Single(r => r.Quantity == 3m);
        Assert.Equal(150m, sale.ProceedsPln);
        Assert.Equal(120m, sale.CostPln);
    }
}
