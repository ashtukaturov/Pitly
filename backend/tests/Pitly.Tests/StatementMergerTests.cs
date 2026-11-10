using Pitly.Core.Models;
using Pitly.Core.Parsing;

namespace Pitly.Tests;

public class StatementMergerTests
{
    private static Trade Buy(string symbol, DateTime when, decimal qty, decimal price)
        => new(symbol, "USD", when, qty, price, qty * price, 1m, "USD", 0m, TradeType.Buy, "US0000000001");

    private static RawDividend Dividend(DateTime when, decimal amount)
        => new("AAPL", "USD", when, amount, "US0378331005");

    [Fact]
    public void Merge_SkipsRowsThatAnotherFileAlreadyContributed()
    {
        var full = new ParsedStatement(
            Trades: [Buy("AAPL", new DateTime(2026, 2, 3, 10, 0, 0), 5m, 100m), Buy("AAPL", new DateTime(2026, 8, 3, 10, 0, 0), 5m, 120m)],
            Dividends: [Dividend(new DateTime(2026, 2, 15), 2.4m), Dividend(new DateTime(2026, 8, 15), 2.5m)],
            WithholdingTaxes: [],
            CorporateActions: [new CorporateAction("AAPL", new DateTime(2026, 6, 1), CorporateActionType.StockSplit, 4m, 1m, "US0378331005")],
            CarryInPositions: [new CarryInPosition("AAPL", 10m, 2026, "US0378331005")],
            StatementYear: 2026, AccountId: "U1234567");
        var partial = new ParsedStatement(
            Trades: [Buy("AAPL", new DateTime(2026, 2, 3, 10, 0, 0), 5m, 100m)],
            Dividends: [Dividend(new DateTime(2026, 2, 15), 2.4m)],
            WithholdingTaxes: [],
            CorporateActions: [new CorporateAction("AAPL", new DateTime(2026, 6, 1), CorporateActionType.StockSplit, 4m, 1m, "US0378331005")],
            CarryInPositions: [new CarryInPosition("AAPL", 10m, 2026, "US0378331005")],
            StatementYear: 2026, AccountId: "U1234567");

        var warnings = new List<string>();
        var merged = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("jan-jun.csv", partial), new StatementMerger.NamedStatement("full-year.csv", full)],
            2026,
            warnings);

        Assert.Equal(2, merged.Trades.Count);
        Assert.Equal(2, merged.Dividends.Count);
        Assert.Single(merged.CorporateActions!);
        Assert.Single(merged.CarryInPositions!);
        var warning = Assert.Single(warnings);
        Assert.Contains("jan-jun.csv", warning);
        Assert.Contains("only repeats data", warning);
        Assert.Contains("1 trade", warning);
        Assert.Contains("1 corporate action", warning);
    }

    [Fact]
    public void Merge_KeepsIdenticalFillsWithinOneFile()
    {
        var when = new DateTime(2026, 3, 3, 10, 0, 0);
        var statement = new ParsedStatement(
            Trades: [Buy("AAPL", when, 1m, 100m), Buy("AAPL", when, 1m, 100m)],
            Dividends: [],
            WithholdingTaxes: [],
            StatementYear: 2026, AccountId: "U1234567");

        var warnings = new List<string>();
        var merged = StatementMerger.Merge([new StatementMerger.NamedStatement("a.csv", statement)], 2026, warnings);

        Assert.Equal(2, merged.Trades.Count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Merge_ReportsPartialOverlapWithoutDroppingNewRows()
    {
        var first = new ParsedStatement(
            Trades: [Buy("AAPL", new DateTime(2025, 2, 3, 10, 0, 0), 5m, 100m)],
            Dividends: [],
            WithholdingTaxes: [],
            StatementYear: 2025, AccountId: "U1234567");
        var second = new ParsedStatement(
            Trades: [Buy("AAPL", new DateTime(2025, 2, 3, 10, 0, 0), 5m, 100m), Buy("MSFT", new DateTime(2025, 2, 4, 10, 0, 0), 1m, 400m)],
            Dividends: [],
            WithholdingTaxes: [],
            StatementYear: 2025, AccountId: "U1234567");

        var warnings = new List<string>();
        var merged = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("a.csv", first), new StatementMerger.NamedStatement("b.csv", second)],
            2025,
            warnings);

        Assert.Equal(2, merged.Trades.Count);
        // The fuller file (b.csv) is taken first, so the one-row file is the one reported as repeating data.
        var warning = Assert.Single(warnings);
        Assert.Contains("a.csv", warning);
        Assert.Contains("1 trade", warning);
    }
}
