using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.Generic;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Broker.Revolut;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>
/// Regression tests for the defects found in the recheck of version b26854d (23 November 2026). Every case is
/// synthetic and every expected value was computed by hand; the comment of each test is the computation.
/// </summary>
public class RecheckTests
{
    private const string Syn = "US0000000001";

    private static List<JsonElement> Diagnostics(PitlyApp.Response result)
        => result.Pit38.GetProperty("diagnostics").EnumerateArray().ToList();

    private static List<string> Codes(PitlyApp.Response result)
        => Diagnostics(result).Select(d => d.GetProperty("code").GetString() ?? string.Empty).ToList();

    private static bool Ready(PitlyApp.Response result) => result.Pit38.GetProperty("readyToFile").GetBoolean();

    private static InteractiveBrokersFlexParser FlexParser()
        => new(NullLogger<InteractiveBrokersFlexParser>.Instance, new InteractiveBrokersStatementParser(NullLogger<InteractiveBrokersStatementParser>.Instance));

    // ── P1-01: generic CSV with an account_id column ──

    private const string TradesHeaderWithAccount =
        "broker,account_id,tx_id,direction,symbol,isin,country,currency,price,quantity,amount,commission,operation_datetime,settlement_date\n";

    private static string GenericA(string account)
        => TradesHeaderWithAccount + $"XB,{account},T1,BUY,SYN,{Syn},US,PLN,100,1,100,0,2024-03-04 10:00:00,2024-03-06\n";

    private static string GenericB(string account)
        => TradesHeaderWithAccount +
           $"XB,{account},T2,BUY,SYN,{Syn},US,PLN,300,1,300,0,2025-03-03 10:00:00,2025-03-05\n" +
           $"XB,{account},T3,SELL,SYN,{Syn},US,PLN,350,1,350,0,2025-06-02 10:00:00,2025-06-04\n";

    [Fact]
    public void Generic_AccountIdColumnMakesOneStatementPerBrokerAndAccount()
    {
        var csv = GenericA("A") + GenericB("B").Split('\n', 2)[1];
        var statements = new GenericCsvStatementParser(NullLogger<GenericCsvStatementParser>.Instance).ParseAll(csv);

        Assert.Equal(2, statements.Count);
        Assert.Equal(["A", "B"], statements.Select(s => s.AccountId));
        Assert.All(statements, s => Assert.Equal("XB", s.Broker));
        Assert.Single(statements[0].Trades);
        Assert.Equal(2, statements[1].Trades.Count);
    }

    [Fact]
    public void Generic_WithoutAccountIdTheBrokerNameIsNotAConfirmedAccount()
    {
        var statement = new GenericCsvStatementParser(NullLogger<GenericCsvStatementParser>.Instance).Parse(new GenericTradesCsv()
            .Buy("XB", "T1", "2025-03-03 10:00:00", "SYN", Syn, 1m, 300m, currency: "PLN").Build());

        Assert.Null(statement.AccountId);
        Assert.Equal("XB", statement.Broker);
    }

    [Fact]
    public async Task Import_GenericFilesWithExplicitAccountsKeepTheirFifoApart()
    {
        // Account A bought 1 at 100 in 2024 and still holds it. Account B bought 1 at 300 and sold it at 350 in 2025:
        // przychód 350, koszty 300, dochód 50, tax 9.5 -> 10. A's lot never serves B's sale.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a.csv", GenericA("A")), ("b.csv", GenericB("B"))], taxYear: 2025);

        result.AssertOk();
        Assert.Equal(350m, result.Field("innePrzychody"));
        Assert.Equal(300m, result.Field("inneKoszty"));
        Assert.Equal(50m, result.Field("dochod"));
        Assert.Equal(10m, result.Field("podatekNalezny"));
        Assert.True(Ready(result));
        Assert.DoesNotContain("ACCOUNT_ASSUMED", Codes(result));

        // The same two accounts in one file give the same result.
        var oneFile = await app.ImportAsync([("both.csv", GenericA("A") + GenericB("B").Split('\n', 2)[1])], taxYear: 2025);
        oneFile.AssertOk();
        Assert.Equal(300m, oneFile.Field("inneKoszty"));
        Assert.Equal(2, oneFile.Body.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task Import_GenericFilesWithoutAccountIdAreNotMergedSilently()
    {
        // Two files of broker XB without account ids: Pitly cannot know whether they are one account. They are
        // treated as one (the usual case of yearly exports) but the result is flagged and not ready to file.
        var a = new GenericTradesCsv().Buy("XB", "T1", "2024-03-04 10:00:00", "SYN", Syn, 1m, 100m, currency: "PLN").Build();
        var b = new GenericTradesCsv().Buy("XB", "T2", "2025-03-03 10:00:00", "SYN", Syn, 1m, 300m, currency: "PLN")
            .Sell("XB", "T3", "2025-06-02 10:00:00", "SYN", Syn, 1m, 350m, currency: "PLN").Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("same.csv", a), ("same.csv", b)], taxYear: 2025);

        result.AssertOk();
        Assert.Contains("ACCOUNT_ASSUMED", Codes(result));
        Assert.False(Ready(result));
        var files = result.Body.GetProperty("files").EnumerateArray().ToList();
        Assert.All(files, f => Assert.False(f.GetProperty("accountConfirmed").GetBoolean()));
        Assert.Equal(2, files.Select(f => f.GetProperty("name").GetString()).Distinct().Count());   // same upload name, told apart

        // Labelled as different accounts, the sale of the second file uses only its own lot: costs 300, not 100.
        var names = files.Select(f => f.GetProperty("name").GetString()!).ToList();
        var labels = JsonSerializer.Serialize(new Dictionary<string, string> { [names[0]] = "first", [names[1]] = "second" });
        var separated = await app.ImportAsync([("same.csv", a), ("same.csv", b)], taxYear: 2025, accountLabelsJson: labels);
        separated.AssertOk();
        Assert.Equal(300m, separated.Field("inneKoszty"));
        Assert.True(Ready(separated));
    }

    [Fact]
    public async Task Import_GenericAccountUploadedTwiceIsCountedOnce()
    {
        using var app = new PitlyApp();
        var once = await app.ImportAsync([("b.csv", GenericB("B"))], taxYear: 2025);
        var twice = await app.ImportAsync([("b.csv", GenericB("B")), ("b-again.csv", GenericB("B"))], taxYear: 2025);

        twice.AssertOk();
        Assert.Equal(once.Field("innePrzychody"), twice.Field("innePrzychody"));
        Assert.Equal(once.Field("inneKoszty"), twice.Field("inneKoszty"));
        Assert.Contains("OVERLAP_SKIPPED", Codes(twice));
    }

    // ── P1-02: one Flex ActionID carrying a dividend and a payment in lieu ──

    private static string FlexDividendAndPaymentInLieu() => new IbFlexCsv()
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20250624", 80m, "Dividends",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share (Ordinary Dividend)", actionId: "ACT1", transactionId: "100", currency: "PLN")
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20250624", 20m, "Payment In Lieu Of Dividends",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share (Payment in Lieu of Dividends)", actionId: "ACT1", transactionId: "101", currency: "PLN")
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20250624", -12m, "Withholding Tax",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share - US Tax", actionId: "ACT1", transactionId: "102", currency: "PLN")
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20250624", -3m, "Withholding Tax",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share - US Tax (Payment in Lieu of Dividends)", actionId: "ACT1", transactionId: "103", currency: "PLN")
        .Build();

    [Fact]
    public async Task Import_FlexActionIdSharedByDividendAndPaymentInLieuKeepsEachWithholdingWithItsKind()
    {
        // Dividend 80 with 12 withheld, payment in lieu 20 with 3 withheld, all under one ActionID. Income 100 PLN,
        // flat tax 19, credit 12 + 3 = 15 (both within the 15% US treaty cap: 12/80, 3/20), difference 4.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", FlexDividendAndPaymentInLieu())]);

        result.AssertOk();
        Assert.Equal(100m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(15m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(4m, result.Field("roznica"));
        Assert.DoesNotContain("AMBIGUOUS_MATCH", Codes(result));
        Assert.DoesNotContain("UNMATCHED_WHT", Codes(result));

        var rows = result.Body.GetProperty("dividends").EnumerateArray().ToList();
        Assert.Equal(12m, rows.Single(r => r.GetProperty("kind").GetString() == "Dividend").GetProperty("withholdingTaxPln").GetDecimal());
        Assert.Equal(3m, rows.Single(r => r.GetProperty("kind").GetString() == "PaymentInLieu").GetProperty("withholdingTaxPln").GetDecimal());

        // The legal classification of the payment in lieu is a separate, open question: shown, never silently decided.
        var classification = Assert.Single(Diagnostics(result), d => d.GetProperty("code").GetString() == "CLASSIFICATION_UNVERIFIED" && d.GetProperty("message").GetString()!.Contains("lieu", StringComparison.OrdinalIgnoreCase));
        Assert.True(classification.GetProperty("affectsAmounts").GetBoolean());
        Assert.False(Ready(result));
    }

    [Fact]
    public void Matcher_ActionIdWithPaymentsOfTwoKindsNarrowsByKindBeforeSharing()
    {
        var day = new DateTime(2025, 6, 24);
        var dividend = new RawDividend("SYN", "PLN", day, 80m, Syn, IncomeKind.Dividend, AccountId: "A", ActionId: "ACT1");
        var inLieu = new RawDividend("SYN", "PLN", day, 20m, Syn, IncomeKind.PaymentInLieu, AccountId: "A", ActionId: "ACT1");
        var events = new List<RawWithholdingTax>
        {
            new("SYN", "PLN", day, 12m, Syn, IncomeKind.Dividend, AccountId: "A", ActionId: "ACT1", TransactionId: "102"),
            new("SYN", "PLN", day, 3m, Syn, IncomeKind.PaymentInLieu, AccountId: "A", ActionId: "ACT1", TransactionId: "103")
        };

        var match = WithholdingMatcher.Match([dividend, inLieu], events);

        Assert.Empty(match.Unresolved);
        Assert.Empty(match.Shared);
        Assert.Equal(12m, Assert.Single(match.Payments.Single(p => p.Payment == dividend).Links).Event.Amount);
        Assert.Equal(3m, Assert.Single(match.Payments.Single(p => p.Payment == inLieu).Links).Event.Amount);
    }

    [Fact]
    public void Matcher_ActionIdWithTwoPaymentsOfTheRightKindThatAreNotOneDistributionStaysAmbiguous()
    {
        var one = new RawDividend("SYN", "PLN", new DateTime(2025, 6, 24), 80m, Syn, IncomeKind.Dividend, AccountId: "A", ActionId: "ACT1");
        var other = new RawDividend("SYN", "PLN", new DateTime(2025, 9, 24), 80m, Syn, IncomeKind.Dividend, AccountId: "A", ActionId: "ACT1");
        var tax = new RawWithholdingTax("SYN", "PLN", new DateTime(2025, 6, 24), 12m, Syn, IncomeKind.Dividend, AccountId: "A", ActionId: "ACT1");

        var match = WithholdingMatcher.Match([one, other], [tax]);

        var unresolved = Assert.Single(match.Unresolved);
        Assert.Equal(DiagnosticCodes.AmbiguousMatch, unresolved.Code);
    }

    // ── P1-03: a Flex refund dated back to the payment but reported in the next year ──

    private static string FlexLateRefund() => new IbFlexCsv()
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20250624", 100m, "Dividends",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share (Ordinary Dividend)", actionId: "ACT1", transactionId: "100", currency: "PLN")
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20250624", -15m, "Withholding Tax",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share - US Tax", actionId: "ACT1", transactionId: "101", currency: "PLN")
        .CashRow("SYN-A", "SYN", Syn, "20250624;202000", "20250624", "20260203", 5m, "Withholding Tax",
            "SYN(US0000000001) Cash Dividend PLN 1.00 per Share - US Tax", actionId: "ACT1", transactionId: "102", currency: "PLN")
        .Build();

    [Fact]
    public void Flex_KeepsTheTransactionSettlementAndReportDatesApart()
    {
        var statement = FlexParser().Parse(FlexLateRefund());

        var refund = Assert.Single(statement.WithholdingTaxes, t => t.Amount < 0m);
        Assert.Equal(new DateTime(2025, 6, 24), refund.Date);
        Assert.Equal(new DateTime(2025, 6, 24), refund.BookingDate);
        Assert.Equal(new DateTime(2025, 6, 24), refund.SettleDate);
        Assert.Equal(new DateTime(2026, 2, 3), refund.ReportDate);
        Assert.Equal(2026, refund.BookingYear);
        Assert.Contains("report", refund.BookingYearEvidence, StringComparison.OrdinalIgnoreCase);
        // The rate stays with the day the broker dated the money to; the report date is not a payment date.
        Assert.Equal(new DateTime(2025, 6, 24), refund.EffectiveDate);
    }

    [Fact]
    public async Task Import_FlexRefundReportedNextYearIsFlaggedForBothYearsAndNotReadyToFile()
    {
        // 100 PLN dividend, 15 withheld, 5 refunded (reported 2026-02-03): credit 10, difference 9 under the
        // attribution to the 2025 payment, but the tax year of the refund's effect is open, so the result is not final.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", FlexLateRefund())], taxYear: 2025);

        result.AssertOk();
        Assert.Equal(10m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(9m, result.Field("roznica"));
        var crossYear = Assert.Single(Diagnostics(result), d => d.GetProperty("code").GetString() == "CROSS_YEAR_ADJUSTMENT");
        Assert.Equal([2025, 2026], crossYear.GetProperty("affectedYears").EnumerateArray().Select(y => y.GetInt32()));
        Assert.Contains("report", crossYear.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(Ready(result));

        var events = result.Body.GetProperty("withholdingEvents").EnumerateArray().ToList();
        var refund = Assert.Single(events, e => e.GetProperty("amount").GetDecimal() < 0m);
        Assert.Equal("2026-02-03", refund.GetProperty("reportDate").GetString()![..10]);
    }

    [Fact]
    public async Task Import_FxDateOfAdditionalTaxFollowsItsOwnDayNotTheReportDate()
    {
        // Dividend 100 USD on 2025-05-15 at 4.00 = 400 PLN. Tax 15 USD withheld the same day = 60 PLN. Additional
        // tax of 2 USD booked (Date/Time) on 2025-07-01 at 4.20 = 8.40 PLN, reported in 2026: credit 68.40 PLN.
        // The report date decides the year flag, the booking day decides the rate (art. 11a ust. 3).
        var csv = new IbFlexCsv()
            .Dividend("U1", "AAPL", "US0378331005", "20250515", 100m, actionId: "A1")
            .Withholding("U1", "AAPL", "US0378331005", "20250515", 15m, actionId: "A1")
            .CashRow("U1", "AAPL", "US0378331005", "20250701;202000", "20250701", "20260110", -2m, "Withholding Tax",
                "AAPL(US0378331005) CASH DIVIDEND USD 0.25 PER SHARE - US TAX", actionId: "A1")
            .Build();

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["USD@2025-07-01"] = 4.2m });
        var result = await app.ImportAsync([("flex.csv", csv)], taxYear: 2025);

        result.AssertOk();
        Assert.Equal(68.4m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());
        Assert.Contains("RATE_DATE", Codes(result));
        Assert.Contains("CROSS_YEAR_ADJUSTMENT", Codes(result));
    }

    // ── P1-04: Revolut layouts seen in real exports ──

    private const string RevolutMoneyInOut =
        "Transactions for Savings Accounts - EUR\n" +
        "Date,Description,Money out,Money in,Balance\n" +
        "\"Nov 30, 2025\",Depositing savings,,€100,€100\n" +
        "\"Dec 3, 2025\",Interest earned,,€0.01,€100.01\n" +
        "\"Dec 5, 2025\",Withdrawing savings,€50,,€50.01\n";

    private const string RevolutVerticalSummary =
        "Summary for Savings Accounts - PLN\n" +
        "Opening balance,0 PLN\n" +
        "Total interest earned,100 PLN\n" +
        "Closing balance,100 PLN\n" +
        "\n" +
        "Transactions for Savings Accounts - PLN\n" +
        "Date,Description,Amount,Balance\n" +
        "\"Dec 3, 2025\",Interest earned,1 PLN,1 PLN\n";

    [Fact]
    public void Revolut_MoneyInAndMoneyOutColumnsAreReadAndMovementsNeedNoAmount()
    {
        var statement = new RevolutSavingsStatementParser(NullLogger<RevolutSavingsStatementParser>.Instance).Parse(RevolutMoneyInOut);

        var interest = Assert.Single(statement.Dividends);
        Assert.Equal(("EUR", 0.01m, new DateTime(2025, 12, 3)), (interest.Currency, interest.Amount, interest.Date));
        Assert.Empty(statement.Diagnostics!);
    }

    [Fact]
    public async Task Import_RevolutMoneyInOutLayoutGivesTheInterestOnly()
    {
        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR"] = 10.51m });
        var result = await app.ImportAsync([("savings.csv", RevolutMoneyInOut)]);

        result.AssertOk();
        Assert.Equal(0.1051m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
    }

    [Fact]
    public async Task Import_RevolutVerticalSummaryThatDisagreesWithTheRowsBlocksFiling()
    {
        // Summary says 100 PLN of interest, the rows add up to 1 PLN: the rows are declared (gross 1) and the
        // mismatch is a blocking finding.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("savings.csv", RevolutVerticalSummary)]);

        result.AssertOk();
        Assert.Equal(1m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Contains("SUMMARY_MISMATCH", Codes(result));
        Assert.False(Ready(result));
    }

    [Fact]
    public void Revolut_NegativeInterestRowIsABlockingFindingNotJustAWarning()
    {
        var csv = new RevolutSavingsCsv().Deposit("EUR", "Jan 5, 2025", 100m).Interest("EUR", "Jan 31, 2025", 0.05m).Build()
            .Replace("\"Jan 31, 2025\",\"Interest earned\",\"Interest\",\"€0.05\"", "\"Jan 31, 2025\",\"Interest earned\",\"Interest\",\"€0.05\"\r\n\"Feb 1, 2025\",\"Interest adjustment\",\"Interest\",\"-€0.02\"");

        var statement = new RevolutSavingsStatementParser(NullLogger<RevolutSavingsStatementParser>.Instance).Parse(csv);

        Assert.Equal(0.05m, Assert.Single(statement.Dividends).Amount);
        var finding = Assert.Single(statement.Diagnostics!, d => d.Message.Contains("-0.02"));
        Assert.True(finding.BlocksFiling);
    }

    [Fact]
    public void Revolut_ReadsSeveralCurrenciesWithBomAndCrlf()
    {
        var csv = "﻿" + RevolutMoneyInOut.Replace("\n", "\r\n") + "\r\n" + RevolutVerticalSummary.Replace("\n", "\r\n");
        var statement = new RevolutSavingsStatementParser(NullLogger<RevolutSavingsStatementParser>.Instance).Parse(csv);

        Assert.Equal(["EUR", "PLN"], statement.Dividends.Select(d => d.Currency).OrderBy(c => c));
        Assert.Contains(statement.Diagnostics!, d => d.Code == DiagnosticCodes.SummaryMismatch);
    }

    // ── P1-05: a full Activity history next to the Flex Query of the same account ──

    private static IbStatement ActivityHistory() => new IbStatement("U1111111", 2025).Instrument("AAPL", "US0378331005")
        // One order of 10 shares filled in two executions (4 at 20, 6 at 30): the statement shows the order.
        .Buy("AAPL", "2024-02-03, 10:00:00", 10m, 26m)
        .Sell("AAPL", "2025-06-03, 10:00:00", 10m, 40m);

    private static IbFlexCsv FlexHistory() => new IbFlexCsv()
        .Buy("U1111111", "AAPL", "US0378331005", "20240203;100000", 4m, 20m)
        .Buy("U1111111", "AAPL", "US0378331005", "20240203;100000", 6m, 30m)
        .Sell("U1111111", "AAPL", "US0378331005", "20250603;100000", 10m, 40m);

    [Fact]
    public async Task Import_ActivityOrdersAndFlexExecutionsOfTheSameAccountReconcileAtOrderLevel()
    {
        // Bought 10 for 260 USD (1040 PLN), sold for 400 USD (1600 PLN): dochód 560, tax 106.4 -> 106, in every combination.
        using var app = new PitlyApp();
        var activity = await app.ImportAsync([("activity.csv", ActivityHistory().Build())], taxYear: 2025);
        var flex = await app.ImportAsync([("flex.csv", FlexHistory().Build())], taxYear: 2025);
        var both = await app.ImportAsync([("activity.csv", ActivityHistory().Build()), ("flex.csv", FlexHistory().Build())], taxYear: 2025);
        var reversed = await app.ImportAsync([("flex.csv", FlexHistory().Build()), ("activity.csv", ActivityHistory().Build())], taxYear: 2025);

        foreach (var result in new[] { activity, flex, both, reversed })
        {
            result.AssertOk();
            Assert.Equal(1600m, result.Field("innePrzychody"));
            Assert.Equal(1040m, result.Field("inneKoszty"));
            Assert.Equal(106m, result.Field("podatekNalezny"));
        }

        Assert.Contains("OVERLAP_SKIPPED", Codes(both));
        Assert.True(Ready(both));
    }

    [Fact]
    public async Task Import_ActivityAndFlexThatDisagreeOnAmountsAreNotDoubledAndBlockFiling()
    {
        // The Flex executions add up to 10 shares like the Activity order, but to 270 USD instead of 260. The merger
        // takes the fuller file first (the Flex Query, three rows), so the Activity order is the row that is not
        // added (adding it would double the position): costs 1080, one sale, and the conflict is a blocking finding.
        var flex = new IbFlexCsv()
            .Buy("U1111111", "AAPL", "US0378331005", "20240203;100000", 4m, 20m)
            .Buy("U1111111", "AAPL", "US0378331005", "20240203;100000", 6m, 31.6667m)
            .Sell("U1111111", "AAPL", "US0378331005", "20250603;100000", 10m, 40m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", ActivityHistory().Build()), ("flex.csv", flex.Build())], taxYear: 2025);

        result.AssertOk();
        Assert.Equal(1600m, result.Field("innePrzychody"));   // one sale, not two
        Assert.Equal(1080m, result.Field("inneKoszty"));      // one purchase, the fuller file's figures
        Assert.Contains("SOURCE_CONFLICT", Codes(result));
        Assert.False(Ready(result));
    }

    [Fact]
    public async Task Import_ActivityNettedWithholdingAndFlexSingleRowsOfTheSameCorrectionCountOnce()
    {
        // 2024: dividend 100 USD, 15 USD withheld. In 2025 the tax is reclassified: 15 refunded and 10 re-charged,
        // both dated back to the 2024 payment. The 2025 Activity Statement nets them into one row (-5); the 2025
        // Flex Query lists two rows (-15, +10, reported in 2025). Together they are one correction: credit 10 USD
        // = 40 PLN for 2024, no excess refund, and the same figures as either format alone.
        var activity2024 = new IbStatement("U1111111", 2024).Dividend("AAPL", Aapl, "2024-02-06", 100m).Withholding("AAPL", Aapl, "2024-02-06", 15m);
        var activity2025 = new IbStatement("U1111111", 2025).Withholding("AAPL", Aapl, "2024-02-06", -15m).Withholding("AAPL", Aapl, "2024-02-06", 10m)
            .Dividend("MSFT", Msft, "2025-05-15", 50m).Withholding("MSFT", Msft, "2025-05-15", 7.5m);
        var flex2024 = new IbFlexCsv().Dividend("U1111111", "AAPL", Aapl, "20240206", 100m, actionId: "A1").Withholding("U1111111", "AAPL", Aapl, "20240206", 15m, actionId: "A1");
        var flex2025 = new IbFlexCsv()
            .CashRow("U1111111", "AAPL", Aapl, "20240206;202000", "20240206", "20250204", 15m, "Withholding Tax", "AAPL(US0378331005) CASH DIVIDEND USD 0.25 PER SHARE - US TAX", actionId: "A1")
            .CashRow("U1111111", "AAPL", Aapl, "20240206;202000", "20240206", "20250204", -10m, "Withholding Tax", "AAPL(US0378331005) CASH DIVIDEND USD 0.25 PER SHARE - US TAX", actionId: "A1")
            .Dividend("U1111111", "MSFT", Msft, "20250515", 50m, actionId: "A2").Withholding("U1111111", "MSFT", Msft, "20250515", 7.5m, actionId: "A2");

        using var app = new PitlyApp();
        var activity = await app.ImportAsync([("a2024.csv", activity2024.Build()), ("a2025.csv", activity2025.Build())], taxYear: 2024);
        var flex = await app.ImportAsync([("f2024.csv", flex2024.Build()), ("f2025.csv", flex2025.Build())], taxYear: 2024);
        var mixed = await app.ImportAsync([("a2024.csv", activity2024.Build()), ("a2025.csv", activity2025.Build()), ("f2024.csv", flex2024.Build()), ("f2025.csv", flex2025.Build())], taxYear: 2024);
        var flexAllYears = await app.ImportAsync([("a2024.csv", activity2024.Build()), ("a2025.csv", activity2025.Build()), ("flex.csv", flex2024.Build() + flex2025.Build())], taxYear: 2024);

        foreach (var (label, result) in new[] { ("activity", activity), ("flex", flex), ("mixed", mixed), ("flexAllYears", flexAllYears) })
        {
            result.AssertOk();
            Assert.True(40m == result.Summary.GetProperty("totalWithholdingPln").GetDecimal(), label + ": " + result.Summary.GetProperty("totalWithholdingPln").GetDecimal() + " " + string.Join(",", Codes(result)));
            Assert.Equal(40m, result.Field("podatekZaplaconyZaGranica"));
            Assert.DoesNotContain("EXCESS_REFUND", Codes(result));
            Assert.DoesNotContain("SOURCE_CONFLICT", Codes(result));
            Assert.Contains("CROSS_YEAR_ADJUSTMENT", Codes(result));
        }
    }

    [Fact]
    public async Task Import_FlexCorrectionOnAnotherDayThanTheActivityStatementIsAConflictNotADoubleCount()
    {
        // The Activity Statement dates the 5 USD refund back to the payment; the Flex Query books it on 2025-02-04.
        // The two formats disagree on the same account's withholding, so the second file's row is not added and
        // the result is blocked; the credit is never the sum of both refunds.
        var activity2024 = new IbStatement("U1111111", 2024).Dividend("AAPL", Aapl, "2024-02-06", 100m).Withholding("AAPL", Aapl, "2024-02-06", 15m);
        var activity2025 = new IbStatement("U1111111", 2025).Withholding("AAPL", Aapl, "2024-02-06", -5m)
            .Dividend("MSFT", Msft, "2025-05-15", 50m).Withholding("MSFT", Msft, "2025-05-15", 7.5m);
        var flex2024 = new IbFlexCsv().Dividend("U1111111", "AAPL", Aapl, "20240206", 100m, actionId: "A1").Withholding("U1111111", "AAPL", Aapl, "20240206", 15m, actionId: "A1");
        var flex2025 = new IbFlexCsv().Withholding("U1111111", "AAPL", Aapl, "20250204", -5m, actionId: "A1")
            .Dividend("U1111111", "MSFT", Msft, "20250515", 50m, actionId: "A2").Withholding("U1111111", "MSFT", Msft, "20250515", 7.5m, actionId: "A2");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a2024.csv", activity2024.Build()), ("a2025.csv", activity2025.Build()), ("f2024.csv", flex2024.Build()), ("f2025.csv", flex2025.Build())], taxYear: 2024);

        result.AssertOk();
        Assert.Equal(40m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());   // 15 - 5, once
        Assert.Contains("SOURCE_CONFLICT", Codes(result));
        Assert.False(Ready(result));
    }

    private const string Aapl = "US0378331005";
    private const string Msft = "US5949181045";

    [Fact]
    public async Task Import_AnotherBrokersFileNeverCountsAsCoverageOfAnIbAccount()
    {
        // An IB Activity Statement next to a Revolut statement: different accounts and formats. Nothing of either
        // is a duplicate or a conflict of the other; the Revolut interest (1.50 PLN) is added to the IB income.
        var activity = new IbStatement("U1111111", 2025).Dividend("AAPL", Aapl, "2025-05-15", 100m).Withholding("AAPL", Aapl, "2025-05-15", 15m);
        var revolut = new RevolutSavingsCsv().Deposit("PLN", "Mar 3, 2025", 1000m).Interest("PLN", "Mar 31, 2025", 1.5m).Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", activity.Build()), ("revolut.csv", revolut)]);

        result.AssertOk();
        Assert.Equal(401.5m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.DoesNotContain("SOURCE_CONFLICT", Codes(result));
        Assert.DoesNotContain("OVERLAP_SKIPPED", Codes(result));
    }

    // ── P1-06: two withholdings on one payment, each on its own day ──

    [Fact]
    public async Task Import_EachWithholdingOnOnePaymentIsValuedAtItsOwnRate()
    {
        // 100 USD dividend. 10 USD withheld on 2025-05-15 at 4.00 = 40 PLN; 5 USD withheld on 2025-06-20 at 5.00 = 25 PLN.
        // Tax withheld 65 PLN whichever row comes first; never 60 (all at 4) or 75 (all at 5).
        IbFlexCsv Build(bool secondFirst)
        {
            var csv = new IbFlexCsv().Dividend("U1", "AAPL", "US0378331005", "20250515", 100m, actionId: "A1");
            if (secondFirst)
                csv.Withholding("U1", "AAPL", "US0378331005", "20250620", 5m, actionId: "A1");
            csv.Withholding("U1", "AAPL", "US0378331005", "20250515", 10m, actionId: "A1");
            if (!secondFirst)
                csv.Withholding("U1", "AAPL", "US0378331005", "20250620", 5m, actionId: "A1");
            return csv;
        }

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["USD@2025-06-20"] = 5m });
        var a = await app.ImportAsync([("flex.csv", Build(false).Build())]);
        var b = await app.ImportAsync([("flex.csv", Build(true).Build())]);

        foreach (var result in new[] { a, b })
        {
            result.AssertOk();
            Assert.Equal(65m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());
            Assert.Equal(60m, result.Field("podatekZaplaconyZaGranica"));   // capped at 15% of 400 PLN
        }
    }

    [Fact]
    public async Task Import_RefundIsNettedAgainstTheWithholdingItReversesAndTheRestKeepsItsOwnRate()
    {
        // 10 USD at 4.00 on the pay date, 5 USD at 5.00 on 2025-06-20, then a 5 USD refund on 2025-08-01: the refund
        // reverses the 5 USD withholding (same amount), so 10 USD at 4.00 = 40 PLN remain. A full refund of 15 USD
        // leaves 0 and no excess.
        IbFlexCsv Build(decimal refund) => new IbFlexCsv()
            .Dividend("U1", "AAPL", "US0378331005", "20250515", 100m, actionId: "A1")
            .Withholding("U1", "AAPL", "US0378331005", "20250515", 10m, actionId: "A1")
            .Withholding("U1", "AAPL", "US0378331005", "20250620", 5m, actionId: "A1")
            .Withholding("U1", "AAPL", "US0378331005", "20250801", -refund, actionId: "A1");

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["USD@2025-06-20"] = 5m, ["USD@2025-08-01"] = 4.5m });
        var partial = await app.ImportAsync([("flex.csv", Build(5m).Build())]);
        var full = await app.ImportAsync([("flex.csv", Build(15m).Build())]);

        Assert.Equal(40m, partial.Summary.GetProperty("totalWithholdingPln").GetDecimal());
        Assert.Equal(0m, full.Summary.GetProperty("totalWithholdingPln").GetDecimal());
        Assert.DoesNotContain("EXCESS_REFUND", Codes(full));
    }

    [Fact]
    public async Task Import_WithholdingInAnotherCurrencyThanThePaymentIsValuedAtItsOwnRate()
    {
        // 100 USD dividend (400 PLN); 2 EUR of tax on it at 4.50 = 9 PLN.
        var csv = new IbFlexCsv()
            .Dividend("U1", "AAPL", "US0378331005", "20250515", 100m, actionId: "A1")
            .Withholding("U1", "AAPL", "US0378331005", "20250515", 2m, actionId: "A1", currency: "EUR")
            .Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", csv)]);

        Assert.Equal(9m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());
    }

    // ── P1-07: interest rounding per art. 63 § 1a O.p. and the precision of the form fields ──

    [Fact]
    public async Task Import_InterestBaseAndTaxAreBothRoundedUpToTheGrosz()
    {
        // 0.01 EUR at 10.51 = 0.1051 PLN. Art. 63 § 1a O.p.: the base is rounded up to the grosz (0.11), the tax
        // too: 0.11 x 19% = 0.0209 -> 0.03 PLN. Not 0.02 (19% of the unrounded base, rounded up).
        var generic = new GenericIncomeCsv().Interest("XB", "I1", "2025-01-31", "EUR interest", "LT", 0.01m, 0m, "EUR").Build();

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR"] = 10.51m });
        var result = await app.ImportAsync([("income.csv", generic)]);

        result.AssertOk();
        Assert.Equal(0.1051m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());   // the audit keeps the exact gross
        Assert.Equal(0.03m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(0.03m, result.Field("roznica"));
        Assert.Equal(0.03m, result.Field("podatekDoZaplaty"));
    }

    [Fact]
    public async Task Import_DividendBaseIsNotRoundedUpToTheGrosz()
    {
        // The grosz-upward rule is for art. 30a ust. 1 pkt 1-3 only. A dividend of 0.1051 PLN: 19% = 0.019969,
        // entered as 0.02 (zł, gr), difference rounded to full PLN = 0.
        var generic = new GenericIncomeCsv().Dividend("XB", "D1", "2025-01-31", "SYN", Syn, 0.01m, 0m, "EUR").Build();

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR"] = 10.51m });
        var result = await app.ImportAsync([("income.csv", generic)]);

        Assert.Equal(0.02m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(0m, result.Field("roznica"));
    }

    [Fact]
    public async Task Import_FormFieldsNeverCarryFractionsOfAGrosz()
    {
        // 100 PLN of interest with 0.015 PLN of creditable foreign tax (a converted amount). Tax 19.00; the credit
        // entered in zł, gr is 0.02 (half up), the difference 19.00 - 0.02 = 18.98 (already full groszy).
        var activity = new IbStatement("U1111111", 2025)
            .Interest("2025-02-05", 100m, "PLN", "PLN Credit Interest for Jan-2025")
            .InterestWithholding("2025-02-05", 0.015m, "PLN", "WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", activity.Build())]);

        result.AssertOk();
        Assert.Equal(0.015m, result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal());   // exact in the summary
        Assert.Equal(0.02m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(19m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(18.98m, result.Field("roznica"));
        Assert.Equal(18.98m, result.Field("podatekDoZaplaty"));

        // The stored session gives the same fields back.
        var reloaded = await app.GetAsync("/api/session/" + result.Body.GetProperty("sessionId").GetString() + "/pit38");
        reloaded.AssertOk();
        foreach (var field in new[] { "zryczaltowanyPodatek", "podatekZaplaconyZaGranica", "roznica", "podatekDoZaplaty" })
            Assert.Equal(result.Field(field), reloaded.Body.GetProperty(field).GetDecimal());
    }

    [Fact]
    public async Task Import_InterestRoundingIsAppliedPerPayment()
    {
        // Two interest payments of 0.1051 PLN each (0.01 EUR at 10.51). Per payment: base 0.11, tax 0.03, twice = 0.06.
        // Rounding the sum instead (0.2102 -> 0.22 x 19% = 0.0418 -> 0.05) would differ; the policy says per payment.
        var generic = new GenericIncomeCsv()
            .Interest("XB", "I1", "2025-01-31", "EUR interest", "LT", 0.01m, 0m, "EUR")
            .Interest("XB", "I2", "2025-02-28", "EUR interest", "LT", 0.01m, 0m, "EUR").Build();

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR"] = 10.51m });
        var result = await app.ImportAsync([("income.csv", generic)]);

        Assert.Equal(0.06m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(0.06m, result.Field("podatekDoZaplaty"));

        // A reopened session rounds the same way: from the stored rows, not from the yearly total (0.05).
        var id = result.Body.GetProperty("sessionId").GetString();
        var pit38 = await app.GetAsync($"/api/session/{id}/pit38");
        var summary = await app.GetAsync($"/api/session/{id}/summary");
        Assert.Equal(0.06m, pit38.Body.GetProperty("podatekDoZaplaty").GetDecimal());
        Assert.Equal(0.06m, summary.Body.GetProperty("dividendTaxOwedPln").GetDecimal());
    }

    // ── P2-08: an issuer country without a treaty rate on file ──

    [Fact]
    public async Task Import_UnknownTreatyRateDoesNotFallBackToTheStatutoryCapSilently()
    {
        // Dividend 100 PLN from country ZZ with 15 PLN withheld. Under the treaty cap no rate is on file for ZZ, so the
        // tax is not credited and the result is blocked with the reason; the statutory cap stays an explicit choice.
        var generic = new GenericIncomeCsv().Dividend("XB", "D1", "2025-01-15", "SYN", "ZZ0000000001", 100m, 15m, "PLN", country: "ZZ").Build();

        using var app = new PitlyApp();
        var treaty = await app.ImportAsync([("income.csv", generic)]);
        var statutory = await app.ImportAsync([("income.csv", generic)], creditMethod: "StatutoryCap");

        treaty.AssertOk();
        Assert.Equal(0m, treaty.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(19m, treaty.Field("roznica"));
        var finding = Assert.Single(Diagnostics(treaty), d => d.GetProperty("code").GetString() == "TREATY_RATE_UNKNOWN");
        Assert.Contains("ZZ", finding.GetProperty("message").GetString());
        Assert.Equal(15m, finding.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.False(Ready(treaty));

        Assert.Equal(15m, statutory.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(4m, statutory.Field("roznica"));
        Assert.DoesNotContain("TREATY_RATE_UNKNOWN", Codes(statutory));
    }

    [Fact]
    public void TreatyRates_TellApartAKnownRateAnUnknownCountryAndAMissingIsin()
    {
        Assert.Equal(TreatyRateStatus.Known, TreatyWithholdingRates.Lookup("US0378331005").Status);
        Assert.Equal(0.15m, TreatyWithholdingRates.Lookup("US0378331005").Rate);
        Assert.Equal(TreatyRateStatus.Unknown, TreatyWithholdingRates.Lookup("ZZ0000000001").Status);
        Assert.Equal(TreatyRateStatus.NoIsin, TreatyWithholdingRates.Lookup(null).Status);
        Assert.Equal(TreatyRateStatus.InvalidCountry, TreatyWithholdingRates.Lookup("1A0000000001").Status);
    }

    [Fact]
    public async Task Import_DividendWithoutIsinAndWithForeignTaxIsBlockedUnderTheTreatyCap()
    {
        var generic = new GenericIncomeCsv().Dividend("XB", "D1", "2025-01-15", "SYN", "", 100m, 15m, "PLN", country: "US").Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("income.csv", generic)]);

        result.AssertOk();
        Assert.Contains("TREATY_RATE_UNKNOWN", Codes(result));
        Assert.False(Ready(result));
    }

    // ── P2-09: securities-lending income and payments in lieu need an explicit classification ──

    [Fact]
    public async Task Import_SecuritiesLendingIncomeIsShownButBlocksFilingUntilClassified()
    {
        // 10 USD (40 PLN) of stock yield enhancement income among 100 USD (400 PLN) of credit interest. Both are in
        // the amounts (nothing is hidden), and the lending income carries a blocking classification finding.
        var activity = new IbStatement("U1111111", 2025)
            .Interest("2025-02-05", 100m, "USD", "USD Credit Interest for Jan-2025")
            .Interest("2025-02-05", 10m, "USD", "USD SYEP Interest for Jan-2025");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", activity.Build())]);

        result.AssertOk();
        Assert.Equal(440m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        var finding = Assert.Single(Diagnostics(result), d => d.GetProperty("code").GetString() == "CLASSIFICATION_UNVERIFIED");
        Assert.Equal(40m, finding.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.True(finding.GetProperty("affectsAmounts").GetBoolean());
        Assert.False(Ready(result));
    }

    [Fact]
    public async Task Import_PlainCreditInterestStaysReadyToFile()
    {
        var activity = new IbStatement("U1111111", 2025)
            .Interest("2025-02-05", 100m, "PLN", "PLN Credit Interest for Jan-2025")
            .InterestWithholding("2025-02-05", 10m, "PLN", "WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", activity.Build())]);

        Assert.Equal(9m, result.Field("roznica"));
        Assert.True(Ready(result));
    }
}
