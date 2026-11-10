using Pitly.Core.Models;
using Pitly.Core.Parsing;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>How tax withheld abroad is tied to the payment it was withheld on.</summary>
public class WithholdingAttributionTests
{
    private const string Aapl = "US0378331005";
    private static readonly DateTime Day = new(2025, 5, 15);

    private static DividendTaxCalculator NewCalculator()
        => new(new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m, ["EUR"] = 5m, ["PLN"] = 1m }));

    [Fact]
    public async Task WithholdingReportedInTheDividendRowStaysWithThatRow()
    {
        // Two Trading 212 payments of the same instrument on one day, each with its own withholding.
        var dividends = new List<RawDividend>
        {
            new("AAPL", "USD", Day, 100m, Aapl, WithheldAmount: 15m, WithheldCurrency: "USD"),
            new("AAPL", "USD", Day, 10m, Aapl, WithheldAmount: 3m, WithheldCurrency: "USD")
        };

        var results = await NewCalculator().CalculateAsync(dividends, []);

        Assert.Equal(60m, results[0].WithholdingTaxPln);
        Assert.Equal(60m, results[0].CreditableWithholdingPln);  // exactly the 15% treaty rate
        Assert.Equal(12m, results[1].WithholdingTaxPln);
        Assert.Equal(6m, results[1].CreditableWithholdingPln);   // 30% withheld on the small one: only 15% of 40 PLN
    }

    [Fact]
    public async Task WithholdingInAnotherCurrencyIsConvertedAtItsOwnRate()
    {
        var dividends = new List<RawDividend> { new("SAP", "EUR", Day, 100m, "DE0007164600", WithheldAmount: 20m, WithheldCurrency: "USD") };

        var result = Assert.Single(await NewCalculator().CalculateAsync(dividends, []));

        Assert.Equal(500m, result.AmountPln);
        Assert.Equal(80m, result.WithholdingTaxPln);       // 20 USD x 4
        Assert.Equal(16m, result.WithholdingTaxOriginal);  // shown in the dividend's currency: 80 PLN / 5
    }

    [Fact]
    public async Task SeparateWithholdingRowsNeverCrossAccounts()
    {
        var dividends = new List<RawDividend>
        {
            new("AAPL", "USD", Day, 100m, Aapl, AccountId: "U1"),
            new("AAPL", "USD", Day, 100m, Aapl, AccountId: "U2")
        };
        // Only the first account had tax withheld.
        var taxes = new List<RawWithholdingTax> { new("AAPL", "USD", Day, 15m, Aapl, AccountId: "U1") };

        var results = await NewCalculator().CalculateAsync(dividends, taxes);

        Assert.Equal(60m, results.Single(r => r.WithholdingTaxPln > 0).WithholdingTaxPln);
        Assert.Equal(60m, results.Sum(r => r.CreditableWithholdingPln)); // not split 30/30 between the accounts
        Assert.Equal(0m, results[1].WithholdingTaxPln);
    }

    [Fact]
    public async Task SeparateWithholdingRowPrefersThePaymentInItsOwnCurrency()
    {
        var dividends = new List<RawDividend>
        {
            new("XYZ", "USD", Day, 100m),
            new("XYZ", "EUR", Day, 100m)
        };
        var taxes = new List<RawWithholdingTax> { new("XYZ", "EUR", Day, 10m) };
        var warnings = new List<string>();

        var results = await NewCalculator().CalculateAsync(dividends, taxes, warnings);

        Assert.Equal(0m, results[0].WithholdingTaxPln);
        Assert.Equal(50m, results[1].WithholdingTaxPln);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task AmbiguousWithholdingIsSplitByPlnValueAndReported()
    {
        // The withholding is in GBP-like third currency (PLN here): neither payment is in that currency.
        var dividends = new List<RawDividend>
        {
            new("XYZ", "USD", Day, 100m),  // 400 PLN
            new("XYZ", "EUR", Day, 20m)    // 100 PLN
        };
        var taxes = new List<RawWithholdingTax> { new("XYZ", "PLN", Day, 50m) };
        var warnings = new List<string>();

        var results = await NewCalculator().CalculateAsync(dividends, taxes, warnings);

        // Split 4:1 by PLN value, not 100:20 by the raw amounts in different currencies.
        Assert.Equal(40m, results[0].WithholdingTaxPln);
        Assert.Equal(10m, results[1].WithholdingTaxPln);
        Assert.Equal(50m, results.Sum(r => r.WithholdingTaxPln));
        var warning = Assert.Single(warnings);
        Assert.Contains("could not be tied to one payment", warning);
    }

    [Fact]
    public async Task WithholdingWithoutAPaymentIsReportedInsteadOfSilentlyDropped()
    {
        var warnings = new List<string>();

        var results = await NewCalculator().CalculateAsync(
            [new RawDividend("AAPL", "USD", Day, 100m, Aapl)],
            [new RawWithholdingTax("MSFT", "USD", Day, 15m, "US5949181045")],
            warnings);

        Assert.Equal(0m, Assert.Single(results).CreditableWithholdingPln);
        Assert.Contains("has no matching dividend row", Assert.Single(warnings));
    }

    [Fact]
    public async Task TotalCreditNeverExceedsTheTaxActuallyWithheld()
    {
        var august = new DateTime(2025, 8, 15);
        var dividends = new List<RawDividend>
        {
            new("AAPL", "USD", Day, 100m, Aapl),
            new("AAPL", "USD", august, 100m, Aapl),
            new("MSFT", "USD", august, 100m, "US5949181045")
        };
        var taxes = new List<RawWithholdingTax>
        {
            new("AAPL", "USD", Day, 15m, Aapl),
            new("AAPL", "USD", Day, -40m, Aapl),   // refund larger than everything withheld on AAPL
            new("AAPL", "USD", august, 15m, Aapl),
            new("MSFT", "USD", august, 15m, "US5949181045")
        };
        var warnings = new List<string>();

        var results = await NewCalculator().CalculateAsync(dividends, taxes, warnings);

        var actuallyWithheldPln = taxes.Sum(t => t.Amount) * 4m; // 5 USD = 20 PLN
        Assert.Equal(20m, actuallyWithheldPln);
        Assert.Equal(20m, results.Sum(r => r.WithholdingTaxPln));
        Assert.True(results.Sum(r => r.CreditableWithholdingPln) <= actuallyWithheldPln);
        Assert.All(results, r => Assert.True(r.WithholdingTaxPln >= 0m && r.CreditableWithholdingPln >= 0m));
        Assert.Contains("refund for AAPL", Assert.Single(warnings));
    }

    [Fact]
    public void Merge_TreatsFilesWithoutAccountNumberAsOneAccountOnlyUntilTheUserSaysOtherwise()
    {
        RawDividend Div() => new("AAPL", "USD", Day, 10m, Aapl);
        ParsedStatement Statement() => new([], [Div()], [], StatementYear: 2025, Broker: "Trading 212");

        // No answer: one account is assumed, the repeated row is skipped and the assumption is reported.
        var assumedWarnings = new List<string>();
        var assumed = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("a.csv", Statement()), new StatementMerger.NamedStatement("b.csv", Statement())],
            2025, assumedWarnings);
        Assert.Single(assumed.Dividends);
        Assert.Contains(assumedWarnings, w => w.StartsWith(StatementMerger.AccountCheckPrefix) && w.Contains("a.csv") && w.Contains("b.csv"));

        // Marked as different accounts: both rows are income, and each row knows its account.
        var separateWarnings = new List<string>();
        var separate = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("a.csv", Statement(), "1"), new StatementMerger.NamedStatement("b.csv", Statement(), "2")],
            2025, separateWarnings);
        Assert.Equal(2, separate.Dividends.Count);
        Assert.Equal(2, separate.Dividends.Select(d => d.AccountId).Distinct().Count());
        Assert.Empty(separateWarnings);

        // A statement of another broker is never the same account, whatever the labels say.
        var otherBroker = new ParsedStatement([], [Div()], [], StatementYear: 2025, Broker: "Other broker");
        var brokers = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("a.csv", Statement(), "1"), new StatementMerger.NamedStatement("c.csv", otherBroker, "1")],
            2025, []);
        Assert.Equal(2, brokers.Dividends.Count);
    }

    [Fact]
    public void Merge_UsesTheBrokersOperationIdWhenItHasOne()
    {
        // Same operation exported twice with a different rounding of the amount: the id says it is one row.
        var first = new ParsedStatement([], [new RawDividend("AAPL", "USD", Day, 10.00m, Aapl, TransactionId: "EOF123")], [], StatementYear: 2025, AccountId: "U1");
        var second = new ParsedStatement([], [new RawDividend("AAPL", "USD", Day, 10.004m, Aapl, TransactionId: "EOF123")], [], StatementYear: 2025, AccountId: "U1");
        // Two genuinely different payments that look identical: different ids keep both.
        var third = new ParsedStatement([], [new RawDividend("AAPL", "USD", Day, 10.00m, Aapl, TransactionId: "EOF999")], [], StatementYear: 2025, AccountId: "U1");

        var merged = StatementMerger.Merge(
            [new StatementMerger.NamedStatement("a.csv", first), new StatementMerger.NamedStatement("b.csv", second), new StatementMerger.NamedStatement("c.csv", third)],
            2025, []);

        Assert.Equal(2, merged.Dividends.Count);
        Assert.Equal(["EOF123", "EOF999"], merged.Dividends.Select(d => d.TransactionId).OrderBy(id => id));
    }

    [Fact]
    public async Task CapitalGains_IgnoresEverythingAfterTheSelectedYear()
    {
        var calculator = new CapitalGainsTaxCalculator(new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m }));
        var statement = new ParsedStatement(
            Trades:
            [
                new Trade("MSFT", "USD", new DateTime(2024, 2, 1), 1m, 25m, 25m, 0m, "USD", 0m, TradeType.Buy),
                new Trade("MSFT", "USD", new DateTime(2024, 12, 31, 23, 59, 0), 1m, 30m, 30m, 0m, "USD", 0m, TradeType.Sell),
                // 2025: a sale with no purchase and a split, neither of which may touch 2024.
                new Trade("AAPL", "USD", new DateTime(2025, 1, 1), 1m, 50m, 50m, 0m, "USD", 0m, TradeType.Sell)
            ],
            Dividends: [],
            WithholdingTaxes: [],
            CorporateActions: [new CorporateAction("MSFT", new DateTime(2025, 3, 1), CorporateActionType.StockSplit, 2m, 1m)]);

        var results = await calculator.CalculateAsync(statement, TaxPeriod.FullYear(2024));

        var sale = Assert.Single(results, r => r.Type == TradeType.Sell);
        Assert.Equal(20m, sale.GainLossPln);

        // The same upload for 2025 cannot be calculated, and says which sale lacks history.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => calculator.CalculateAsync(statement, TaxPeriod.FullYear(2025)));
        Assert.Contains("Cannot sell 1 shares of AAPL on 2025-01-01", error.Message);
    }
}
