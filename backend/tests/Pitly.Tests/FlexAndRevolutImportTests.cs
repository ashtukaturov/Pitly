using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Broker.Revolut;
using Pitly.Core.Models;

namespace Pitly.Tests;

/// <summary>
/// The two formats added for the 2025 return: the Interactive Brokers Flex Query CSV and the Revolut savings
/// statement. Parser-level checks of signs and sections, then the same files through the real API, next to the
/// formats Pitly already read, so that an event arriving in two formats is counted once.
/// </summary>
public class FlexAndRevolutImportTests
{
    private const string Aapl = "US0378331005";
    private const string Msft = "US5949181045";

    private static InteractiveBrokersFlexParser FlexParser()
        => new(NullLogger<InteractiveBrokersFlexParser>.Instance, new InteractiveBrokersStatementParser(NullLogger<InteractiveBrokersStatementParser>.Instance));

    private static List<JsonElement> Diagnostics(PitlyApp.Response result)
        => result.Pit38.GetProperty("diagnostics").EnumerateArray().ToList();

    private static List<string> Codes(PitlyApp.Response result)
        => Diagnostics(result).Select(d => d.GetProperty("code").GetString() ?? string.Empty).ToList();

    // ── Flex Query CSV: sections, signs, accounts ──

    [Fact]
    public void Flex_ReadsEverySectionWithTheRightSignsAndKeepsAccountsApart()
    {
        var csv = new IbFlexCsv()
            .Buy("U1111111", "AAPL", Aapl, "20250203;100000", 10m, 25m, commission: 1m)
            .Sell("U1111111", "AAPL", Aapl, "20250603;100000", 10m, 30m, commission: 1m)
            .Dividend("U1111111", "AAPL", Aapl, "20250515", 100m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20250515", 15m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20260210", -15m, actionId: "A1")   // refund booked next year
            .Interest("U1111111", "20250205", 100m, "PLN", "JAN-2025")
            .InterestWithholding("U1111111", "20250205", 10m, "PLN", "JAN-2025")
            .OtherCash("U1111111", "20250110", 5000m, "Deposits/Withdrawals")
            .OtherCash("U1111111", "20250110", -3m, "Other Fees")
            .Split("U1111111", "AAPL", Aapl, "20250701;202500", 2, 1, creditedShares: 10m, actionId: "S1")
            .Buy("U2222222", "MSFT", Msft, "20250204;100000", 1m, 400m, tradeId: "77")
            .Build();

        var statements = FlexParser().ParseAll(csv);

        Assert.Equal(["U1111111", "U2222222"], statements.Select(s => s.AccountId));
        var first = statements[0];
        Assert.Equal(InteractiveBrokersStatementParser.BrokerName, first.Broker);
        Assert.Equal("ib-flex-csv/1", first.ParserVersion);

        var buy = Assert.Single(first.Trades, t => t.Type == TradeType.Buy);
        Assert.Equal((10m, 25m, 250m, 1m, "US"), (buy.Quantity, buy.Price, buy.Proceeds, buy.Commission, buy.CountryCode));
        Assert.Equal(new DateTime(2025, 2, 3, 10, 0, 0), buy.DateTime);
        var sell = Assert.Single(first.Trades, t => t.Type == TradeType.Sell);
        Assert.Equal((10m, 300m), (sell.Quantity, sell.Proceeds));

        var dividend = Assert.Single(first.Dividends, d => d.Kind == IncomeKind.Dividend);
        Assert.Equal((100m, "A1", new DateTime(2025, 5, 15), "US"), (dividend.Amount, dividend.ActionId, dividend.Date, dividend.CountryCode));
        var interest = Assert.Single(first.Dividends, d => d.Kind == IncomeKind.Interest);
        Assert.Equal(("PLN", 100m, "JAN-2025"), (interest.Currency, interest.Amount, interest.Reference));

        // Withheld tax is positive, the refund negative, each with its own booking date and action id.
        var taxes = first.WithholdingTaxes.Where(t => t.Kind == IncomeKind.Dividend).OrderBy(t => t.Date).ToList();
        Assert.Equal([15m, -15m], taxes.Select(t => t.Amount));
        Assert.All(taxes, t => Assert.Equal("A1", t.ActionId));
        Assert.Equal(2026, taxes[1].BookingYear);
        var interestTax = Assert.Single(first.WithholdingTaxes, t => t.Kind == IncomeKind.Interest);
        Assert.Equal((10m, "PLN", "JAN-2025", null), (interestTax.Amount, interestTax.Currency, interestTax.Reference, interestTax.Isin));

        var split = Assert.Single(first.CorporateActions!);
        Assert.Equal((CorporateActionType.StockSplit, 2m, 1m), (split.Type, split.Numerator, split.Denominator));

        // The second account has only its own trade, and the rows of other types were only counted.
        Assert.Equal("77", Assert.Single(statements[1].Trades).TransactionId);
        Assert.Empty(statements[1].Dividends);
        Assert.DoesNotContain(first.Warnings!, w => w.Contains("Deposits")); // deposits and fees are known non-income rows: silent
    }

    [Fact]
    public void Flex_ReportsRowsOfUnknownKindsInsteadOfGuessing()
    {
        var csv = new IbFlexCsv()
            .Dividend("U1111111", "AAPL", Aapl, "20250515", 100m, actionId: "A1")
            .OtherCash("U1111111", "20250110", 12m, "Mystery Credit")
            .Build();

        var statement = FlexParser().Parse(csv);

        Assert.Single(statement.Dividends);
        var warning = Assert.Single(statement.Warnings!);
        Assert.Contains("Mystery Credit", warning);
        var diagnostic = Assert.Single(statement.Diagnostics!);
        Assert.Equal(DiagnosticCodes.UnsupportedRow, diagnostic.Code);
    }

    [Fact]
    public async Task Import_FlexQueryAloneGivesTheFormFields()
    {
        // 10 AAPL bought at 25 USD (1000 PLN) with 1 USD fee (4 PLN), sold at 30 USD (1200 PLN) with 1 USD fee:
        // przychód 1200, koszty 1008, dochód 192, tax 36.48 -> 36. Dividend 100 USD (400 PLN) with 15 USD (60 PLN)
        // withheld; PLN 100 of interest with 10 PLN withheld.
        var csv = new IbFlexCsv()
            .Buy("U1111111", "AAPL", Aapl, "20250203;100000", 10m, 25m, commission: 1m)
            .Sell("U1111111", "AAPL", Aapl, "20250603;100000", 10m, 30m, commission: 1m)
            .Dividend("U1111111", "AAPL", Aapl, "20250515", 100m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20250515", 15m, actionId: "A1")
            .Interest("U1111111", "20250205", 100m, "PLN", "JAN-2025")
            .InterestWithholding("U1111111", "20250205", 10m, "PLN", "JAN-2025")
            .Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", csv)]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(1200m, result.Field("innePrzychody"));
        Assert.Equal(1008m, result.Field("inneKoszty"));
        Assert.Equal(36m, result.Field("podatekNalezny"));
        Assert.Equal(500m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(70m, result.Field("podatekZaplaconyZaGranica"));     // 60 + 10
        Assert.Equal(25m, result.Field("roznica"));                        // (76 - 60) -> 16, (19 - 10) -> 9
        Assert.True(result.Pit38.GetProperty("readyToFile").GetBoolean());
        var file = Assert.Single(result.Body.GetProperty("files").EnumerateArray());
        Assert.Equal(("U1111111", true, "ib-flex-csv/1"), (file.GetProperty("account").GetString(), file.GetProperty("accountKnown").GetBoolean(), file.GetProperty("format").GetString()));
    }

    [Fact]
    public async Task Import_FlexAndActivityOfTheSameAccountCountEveryEventOnce()
    {
        // The same account exported twice: the Activity Statement (no row ids) and the Flex Query (row ids).
        var activity = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-02-03, 10:00:00", 10m, 25m)
            .Split("AAPL", Aapl, "2025-07-01, 20:25:00", 2, 1, creditedShares: 10m)
            .Sell("AAPL", "2025-08-01, 10:00:00", 20m, 15m)
            .Dividend("AAPL", Aapl, "2025-05-15", 100m)
            .Withholding("AAPL", Aapl, "2025-05-15", 15m);
        var flex = new IbFlexCsv()
            .Buy("U1111111", "AAPL", Aapl, "20250203;100000", 10m, 25m)
            .Split("U1111111", "AAPL", Aapl, "20250701;202500", 2, 1, creditedShares: 10m, actionId: "S1")
            .Sell("U1111111", "AAPL", Aapl, "20250801;100000", 20m, 15m)
            .Dividend("U1111111", "AAPL", Aapl, "20250515", 100m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20250515", 15m, actionId: "A1");

        using var app = new PitlyApp();
        var alone = await app.ImportAsync([("activity.csv", activity.Build())]);
        var both = await app.ImportAsync([("activity.csv", activity.Build()), ("flex.csv", flex.Build())]);
        var reversed = await app.ImportAsync([("flex.csv", flex.Build()), ("activity.csv", activity.Build())]);

        foreach (var result in new[] { alone, both, reversed })
        {
            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.Equal(1200m, result.Field("innePrzychody"));   // 20 x 15 USD once, not twice
            Assert.Equal(1000m, result.Field("inneKoszty"));      // one purchase, one split
            Assert.Equal(400m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
            Assert.Equal(60m, result.Field("podatekZaplaconyZaGranica"));
            Assert.Single(result.Body.GetProperty("dividends").EnumerateArray());
        }

        Assert.Contains(both.Strings(both.Summary.GetProperty("warnings")), w => w.Contains("only repeats data") || w.Contains("overlaps"));
        Assert.Contains("OVERLAP_SKIPPED", Codes(both));
    }

    [Fact]
    public async Task Import_PartialFlexNextToActivityKeepsTheOtherAccountAndTheEventsItDoesNotCover()
    {
        var accountA = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 100m).Withholding("AAPL", Aapl, "2025-05-15", 15m)
            .Dividend("AAPL", Aapl, "2025-08-15", 100m).Withholding("AAPL", Aapl, "2025-08-15", 15m);
        var accountB = new IbStatement("U2222222", 2025)
            .Dividend("MSFT", Msft, "2025-05-15", 100m).Withholding("MSFT", Msft, "2025-05-15", 15m);
        // A Flex Query of account A that covers only May.
        var partialFlex = new IbFlexCsv()
            .Dividend("U1111111", "AAPL", Aapl, "20250515", 100m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20250515", 15m, actionId: "A1");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a.csv", accountA.Build()), ("b.csv", accountB.Build()), ("flex-may.csv", partialFlex.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(3, result.Body.GetProperty("dividends").GetArrayLength());   // May A, August A, May B
        Assert.Equal(1200m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(180m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Contains(result.Strings(result.Summary.GetProperty("warnings")), w => w.StartsWith("'flex-may.csv' only repeats data"));
    }

    [Fact]
    public async Task Import_SameTransactionIdOnTwoAccountsIsTwoEvents()
    {
        var csv = new IbFlexCsv()
            .Dividend("U1111111", "AAPL", Aapl, "20250515", 100m, actionId: "A1", transactionId: "T1")
            .Withholding("U1111111", "AAPL", Aapl, "20250515", 15m, actionId: "A1", transactionId: "T2")
            .Dividend("U2222222", "AAPL", Aapl, "20250515", 100m, actionId: "A1", transactionId: "T1")
            .Withholding("U2222222", "AAPL", Aapl, "20250515", 15m, actionId: "A1", transactionId: "T2")
            .Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", csv)]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(2, result.Body.GetProperty("dividends").GetArrayLength());
        Assert.Equal(800m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(120m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(2, result.Body.GetProperty("files").GetArrayLength());   // one upload, two statements
    }

    [Fact]
    public async Task Import_FlexRefundOfLastYearsTaxIsLinkedByActionIdAndFlaggedForBothYears()
    {
        var csv = new IbFlexCsv()
            .Dividend("U1111111", "AAPL", Aapl, "20240515", 100m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20240515", 15m, actionId: "A1")
            .Withholding("U1111111", "AAPL", Aapl, "20250210", -15m, actionId: "A1")   // refund, booked and dated in 2025
            .Dividend("U1111111", "MSFT", Msft, "20250515", 100m, actionId: "A2")
            .Withholding("U1111111", "MSFT", Msft, "20250515", 15m, actionId: "A2")
            .Build();

        using var app = new PitlyApp();
        var year2025 = await app.ImportAsync([("flex.csv", csv)], taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, year2025.Status);
        Assert.Equal(60m, year2025.Field("podatekZaplaconyZaGranica"));           // MSFT untouched
        Assert.DoesNotContain("UNMATCHED_WHT", Codes(year2025));
        var crossYear = Assert.Single(Diagnostics(year2025), d => d.GetProperty("code").GetString() == "CROSS_YEAR_ADJUSTMENT");
        Assert.Equal([2024, 2025], crossYear.GetProperty("affectedYears").EnumerateArray().Select(y => y.GetInt32()));

        var year2024 = await app.ImportAsync([("flex.csv", csv)], taxYear: 2024);
        Assert.Equal(0m, year2024.Field("podatekZaplaconyZaGranica"));            // the 2024 tax was refunded
        Assert.Equal(76m, year2024.Field("roznica"));                               // 19% of 400 PLN, nothing to credit
    }

    // ── Revolut savings statement ──

    [Fact]
    public void Revolut_ReadsOnlyInterestAcrossCurrenciesAndChecksTheSummary()
    {
        var csv = new RevolutSavingsCsv()
            .Deposit("EUR", "Jan 5, 2025", 100m)
            .Interest("EUR", "Jan 31, 2025", 0.01m)
            .Withdrawal("EUR", "Feb 1, 2025", 100.01m)
            .Deposit("PLN", "Mar 3, 2025", 1000m)
            .Interest("PLN", "Mar 31, 2025", 1.5m)
            .Interest("PLN", "Apr 30, 2025", 1234.56m)   // "1,234.56 zł": thousands separator and suffix
            .Build();

        Assert.StartsWith("﻿", csv);
        Assert.Contains("\r\n", csv);
        Assert.True(RevolutSavingsStatementParser.Detect(csv));

        var statement = new RevolutSavingsStatementParser(NullLogger<RevolutSavingsStatementParser>.Instance).Parse(csv);

        Assert.Equal(RevolutSavingsStatementParser.BrokerName, statement.Broker);
        Assert.Null(statement.AccountId);
        Assert.Empty(statement.Trades);
        Assert.Empty(statement.WithholdingTaxes);
        var rows = statement.Dividends.OrderBy(d => d.Date).ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, d => Assert.Equal(IncomeKind.Interest, d.Kind));
        Assert.All(rows, d => Assert.Null(d.CountryCode));   // never derived from the currency
        Assert.Equal(("EUR", 0.01m, new DateTime(2025, 1, 31)), (rows[0].Currency, rows[0].Amount, rows[0].Date));
        Assert.Equal(("PLN", 1.5m), (rows[1].Currency, rows[1].Amount));
        Assert.Equal(("PLN", 1234.56m), (rows[2].Currency, rows[2].Amount));
        Assert.Empty(statement.Diagnostics!);
    }

    [Fact]
    public void Revolut_ReportsUnknownRowsAndASummaryThatDoesNotAddUp()
    {
        var csv = new RevolutSavingsCsv()
            .Deposit("EUR", "Jan 5, 2025", 100m)
            .Interest("EUR", "Jan 31, 2025", 0.01m)
            .Other("EUR", "Feb 2, 2025", "Bonus credited", 5m)
            .SummaryInterest(0.03m)
            .Build();

        var statement = new RevolutSavingsStatementParser(NullLogger<RevolutSavingsStatementParser>.Instance).Parse(csv);

        Assert.Equal(0.01m, Assert.Single(statement.Dividends).Amount);   // the summary never creates income
        Assert.Contains(statement.Diagnostics!, d => d.Code == DiagnosticCodes.UnsupportedRow && d.Message.Contains("Bonus credited"));
        Assert.Contains(statement.Diagnostics!, d => d.Code == DiagnosticCodes.SummaryMismatch && d.Message.Contains("0.03") && d.Message.Contains("0.01"));
    }

    [Fact]
    public async Task Import_RevolutGivesTheSameResultAsTheSameRowsInTheGenericIncomeTemplate()
    {
        // Deposit 100 EUR, interest 0.01 EUR, withdrawal 100.01 EUR: the income is 0.01 EUR, not 100.01 and not 200.02.
        var revolut = new RevolutSavingsCsv()
            .Deposit("EUR", "Jan 5, 2025", 100m)
            .Interest("EUR", "Jan 31, 2025", 0.01m)
            .Withdrawal("EUR", "Feb 1, 2025", 100.01m)
            .Deposit("PLN", "Mar 3, 2025", 1000m)
            .Interest("PLN", "Mar 31, 2025", 1.5m)
            .Build();
        // The same two interest rows prepared by hand in the generic income template.
        var generic = new GenericIncomeCsv()
            .Interest("REVOLUT", "R1", "2025-01-31", "EUR interest", "LT", 0.01m, 0m, "EUR")
            .Interest("REVOLUT", "R2", "2025-03-31", "PLN interest", "LT", 1.5m, 0m, "PLN");

        // 0.01 EUR at a synthetic 10.51 PLN/EUR = 0.1051 PLN and 1.50 PLN: per payment (art. 63 § 1a O.p., recheck P1-07)
        // base 0.11 -> tax 0.0209 -> 0.03, and base 1.50 -> tax 0.285 -> 0.29; together 0.32 PLN. (The earlier 0.31 came
        // from 19% of the unrounded yearly total; the base is rounded per payment now.)
        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR"] = 10.51m });
        var fromRevolut = await app.ImportAsync([("revolut.csv", revolut)]);
        var fromGeneric = await app.ImportAsync([("income.csv", generic.Build())]);

        Assert.Equal(HttpStatusCode.OK, fromRevolut.Status);
        Assert.Equal(HttpStatusCode.OK, fromGeneric.Status);
        Assert.Equal(1.6051m, fromRevolut.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(fromGeneric.Summary.GetProperty("totalDividendsPln").GetDecimal(), fromRevolut.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(0.32m, fromRevolut.Field("zryczaltowanyPodatek"));
        Assert.Equal(0.32m, fromRevolut.Field("podatekDoZaplaty"));
        Assert.Equal(fromGeneric.Field("podatekDoZaplaty"), fromRevolut.Field("podatekDoZaplaty"));

        var revolutRows = fromRevolut.Body.GetProperty("dividends").EnumerateArray().Select(d => (d.GetProperty("currency").GetString(), d.GetProperty("amountOriginal").GetDecimal(), d.GetProperty("date").GetString())).OrderBy(x => x.Item3).ToList();
        var genericRows = fromGeneric.Body.GetProperty("dividends").EnumerateArray().Select(d => (d.GetProperty("currency").GetString(), d.GetProperty("amountOriginal").GetDecimal(), d.GetProperty("date").GetString())).OrderBy(x => x.Item3).ToList();
        Assert.Equal(genericRows, revolutRows);

        // Without a country column the Revolut rows fall under the unknown country in PIT/ZG; the generic file named one.
        Assert.Equal("??", Assert.Single(fromRevolut.Pit38.GetProperty("pitZg").EnumerateArray()).GetProperty("countryCode").GetString());
        Assert.Equal("LT", Assert.Single(fromGeneric.Pit38.GetProperty("pitZg").EnumerateArray()).GetProperty("countryCode").GetString());
        Assert.True(fromRevolut.Pit38.GetProperty("readyToFile").GetBoolean());

        // The file is attributed to Revolut without an account number, which the user may label.
        var file = Assert.Single(fromRevolut.Body.GetProperty("files").EnumerateArray());
        Assert.Equal(("Revolut", false), (file.GetProperty("broker").GetString(), file.GetProperty("accountKnown").GetBoolean()));
    }

    [Fact]
    public async Task Import_RevolutUploadedTwiceAndInAnotherOrderCountsInterestOnce()
    {
        var january = new RevolutSavingsCsv().Deposit("EUR", "Jan 5, 2025", 100m).Interest("EUR", "Jan 31, 2025", 0.01m).Build();
        var fullYear = new RevolutSavingsCsv().Deposit("EUR", "Jan 5, 2025", 100m).Interest("EUR", "Jan 31, 2025", 0.01m).Interest("EUR", "Feb 28, 2025", 0.02m).Build();

        using var app = new PitlyApp();
        var a = await app.ImportAsync([("jan.csv", january), ("year.csv", fullYear)], accountLabelsJson: """{"jan.csv":"savings","year.csv":"savings"}""");
        var b = await app.ImportAsync([("year.csv", fullYear), ("jan.csv", january)], accountLabelsJson: """{"jan.csv":"savings","year.csv":"savings"}""");

        Assert.Equal(HttpStatusCode.OK, a.Status);
        Assert.Equal(0.135m, a.Summary.GetProperty("totalDividendsPln").GetDecimal());   // 0.03 EUR x 4.50
        Assert.Equal(a.Summary.GetProperty("totalDividendsPln").GetDecimal(), b.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(a.Field("podatekDoZaplaty"), b.Field("podatekDoZaplaty"));
    }
}
