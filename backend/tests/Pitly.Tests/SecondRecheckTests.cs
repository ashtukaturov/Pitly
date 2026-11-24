using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.Generic;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>
/// Regression tests for the findings of the recheck of version 62c013c (24 November 2026): a refund that an
/// Activity Statement books in the next year under the text of the distribution it corrects, a false source
/// conflict after a partial row-level match, the year scope of a source conflict, the level at which interest
/// is rounded, and the dates a trade is recognised and converted on. Every case is synthetic and every expected
/// value was computed by hand; the comment of each test is the computation.
/// </summary>
public class SecondRecheckTests
{
    private const string Syn = "US0000000001";
    private const string Account = "U1111111";

    private static List<JsonElement> Diagnostics(PitlyApp.Response result)
        => result.Pit38.GetProperty("diagnostics").EnumerateArray().ToList();

    private static List<JsonElement> Of(PitlyApp.Response result, string code)
        => Diagnostics(result).Where(d => d.GetProperty("code").GetString() == code).ToList();

    private static List<string> Codes(PitlyApp.Response result)
        => Diagnostics(result).Select(d => d.GetProperty("code").GetString() ?? string.Empty).Distinct().ToList();

    private static bool Ready(PitlyApp.Response result) => result.Pit38.GetProperty("readyToFile").GetBoolean();

    private static List<int> Years(JsonElement diagnostic)
        => diagnostic.GetProperty("affectedYears").EnumerateArray().Select(y => y.GetInt32()).ToList();

    private static InteractiveBrokersFlexParser FlexParser()
        => new(NullLogger<InteractiveBrokersFlexParser>.Instance, new InteractiveBrokersStatementParser(NullLogger<InteractiveBrokersStatementParser>.Instance));

    // ── P1: a refund of an earlier year's tax, booked by the Activity Statement under its own day ──

    // 2024: a dividend of 6.20 USD on 2024-02-07 (USD 0.31 per share) with 0.93 USD withheld. USD at 4:
    // income 24.80 PLN, withheld 3.72 PLN.
    private static string Activity2024() => new IbStatement(Account, 2024).Instrument("SYN", Syn)
        .Dividend("SYN", Syn, "2024-02-07", 6.20m, perShare: "0.31")
        .Withholding("SYN", Syn, "2024-02-07", 0.93m, perShare: "0.31")
        .Build();

    // 2025: the refund of the whole 0.93 USD, dated 2025-02-13 (the day it was made) and carrying the text of the
    // 2024 distribution, as the real statement does.
    private static string Activity2025Refund() => new IbStatement(Account, 2025).Instrument("SYN", Syn)
        .Withholding("SYN", Syn, "2025-02-13", -0.93m, perShare: "0.31")
        .Build();

    [Fact]
    public void DistributionReference_IsTheCurrencyAndAmountPerShareOfTheBrokersText()
    {
        Assert.Equal("USD 0.310122 PER SHARE", DistributionReference.Parse("TLT(US4642874329) Cash Dividend USD 0.310122 per Share - US Tax"));
        Assert.Equal("USD 0.25 PER SHARE", DistributionReference.Parse("SYN(US0000000001) CASH DIVIDEND USD 0.250 PER SHARE (Ordinary Dividend)"));
        Assert.Null(DistributionReference.Parse("SYN(US0000000001) Cash Dividend - US Tax"));
        Assert.Null(DistributionReference.Parse("WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025"));
    }

    [Fact]
    public void Activity_TheTextOfTheDistributionIsKeptOnPaymentsAndTaxRows()
    {
        var statement = new InteractiveBrokersStatementParser(NullLogger<InteractiveBrokersStatementParser>.Instance).Parse(Activity2024());

        Assert.Equal("USD 0.31 PER SHARE", Assert.Single(statement.Dividends).Distribution);
        Assert.Equal("USD 0.31 PER SHARE", Assert.Single(statement.WithholdingTaxes).Distribution);
    }

    [Fact]
    public async Task Activity_RefundBookedNextYearIsTiedToItsPaymentByTheBrokersReferenceOfTheDistribution()
    {
        // The refund reverses the whole withholding: credit 0 on the 2024 payment. Section G 2024: flat tax
        // 24.80 x 19% = 4.712 -> 4.71 zł, gr; paid abroad 0; difference 4.712 -> 5 PLN. The refund moved in 2025, so
        // both years carry CROSS_YEAR_ADJUSTMENT and neither is ready until the year of its effect is decided.
        // Before the fix the refund matched nothing (UNMATCHED_WHT for 2025 only) and 2024 kept a credit of 3.72.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2024.csv", Activity2024()), ("2025.csv", Activity2025Refund())], taxYear: 2024);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(4.71m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(0m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(5m, result.Field("roznica"));
        var cross = Assert.Single(Of(result, DiagnosticCodes.CrossYearAdjustment));
        Assert.Equal([2024, 2025], Years(cross));
        Assert.DoesNotContain(DiagnosticCodes.UnmatchedWithholding, Codes(result));
        Assert.False(Ready(result));

        var events = result.Body.GetProperty("withholdingEvents").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("amount").GetDecimal() == -0.93m && e.GetProperty("status").GetString() == "Matched");
    }

    [Fact]
    public async Task Activity_FlexAndTheirMixGiveTheSameFieldsForTheSameLateRefund()
    {
        // The Flex Query of the same account ties the refund by its ActionID. Whichever format, or both in either
        // order, the 2024 fields are the same: 4.71 / 0 / 5.
        var flex2024 = new IbFlexCsv()
            .Dividend(Account, "SYN", Syn, "20240207", 6.20m, actionId: "A1", perShare: "0.31")
            .Withholding(Account, "SYN", Syn, "20240207", 0.93m, actionId: "A1", perShare: "0.31")
            .Build();
        var flex2025 = new IbFlexCsv()
            .CashRow(Account, "SYN", Syn, "20250213;202000", "20250213", "20250213", 0.93m, "Withholding Tax",
                $"SYN({Syn}) CASH DIVIDEND USD 0.31 PER SHARE - US TAX", actionId: "A1")
            .Build();
        var activity = new[] { ("a2024.csv", Activity2024()), ("a2025.csv", Activity2025Refund()) };
        var flex = new[] { ("f2024.csv", flex2024), ("f2025.csv", flex2025) };

        foreach (var files in new[] { activity, flex, activity.Concat(flex).ToArray(), flex.Concat(activity).ToArray() })
        {
            using var app = new PitlyApp();
            var result = await app.ImportAsync(files, taxYear: 2024);

            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.Equal(4.71m, result.Field("zryczaltowanyPodatek"));
            Assert.Equal(0m, result.Field("podatekZaplaconyZaGranica"));
            Assert.Equal(5m, result.Field("roznica"));
            Assert.Contains(DiagnosticCodes.CrossYearAdjustment, Codes(result));
            Assert.DoesNotContain(DiagnosticCodes.UnmatchedWithholding, Codes(result));
            Assert.DoesNotContain(DiagnosticCodes.SourceConflict, Codes(result));
        }
    }

    [Fact]
    public async Task Activity_RefundOnThePayDayOfAnotherDistributionFollowsTheBrokersReferenceNotTheDate()
    {
        // 2025 has its own dividend on 2025-02-13 (USD 0.29 per share, 5.80 USD, 0.87 withheld). The refund of
        // 0.93 USD dated the same day carries the 2024 text (0.31 per share), so it belongs to 2024, not to the
        // 2025 payment. 2025: income 23.20 PLN, withheld 3.48 PLN; flat tax 4.408 -> 4.41, paid abroad 3.48,
        // difference 0.928 -> 1 PLN. 2024: credit 0 (as above).
        var activity2025 = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Dividend("SYN", Syn, "2025-02-13", 5.80m, perShare: "0.29")
            .Withholding("SYN", Syn, "2025-02-13", 0.87m, perShare: "0.29")
            .Withholding("SYN", Syn, "2025-02-13", -0.93m, perShare: "0.31")
            .Build();
        using var app = new PitlyApp();
        var files = new[] { ("2024.csv", Activity2024()), ("2025.csv", activity2025) };

        var y2025 = await app.ImportAsync(files, taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, y2025.Status);
        Assert.Equal(4.41m, y2025.Field("zryczaltowanyPodatek"));
        Assert.Equal(3.48m, y2025.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(1m, y2025.Field("roznica"));

        var y2024 = await app.ImportAsync(files, taxYear: 2024);
        Assert.Equal(0m, y2024.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(5m, y2024.Field("roznica"));
        Assert.Contains(DiagnosticCodes.CrossYearAdjustment, Codes(y2024));
    }

    [Fact]
    public async Task Activity_RefundWhoseReferenceFitsSeveralPaymentsStaysUnresolvedAndFlagsTheirYear()
    {
        // Two 2024 distributions print the same text (USD 0.25 per share): 6.20 USD on 2024-02-07 and on
        // 2024-05-07, 0.93 USD withheld on each (7.44 PLN together). The 2025 refund of 0.93 USD with that text
        // fits both: the text names a distribution, not a payment, so nothing is attributed (AMBIGUOUS_MATCH) and
        // the 2024 return shows the open question with 3.72 PLN unresolved. 2024 credit stays 7.44: flat tax
        // 49.60 x 19% = 9.424 -> 9.42, difference 1.984 -> 2 PLN.
        var activity2024 = new IbStatement(Account, 2024).Instrument("SYN", Syn)
            .Dividend("SYN", Syn, "2024-02-07", 6.20m).Withholding("SYN", Syn, "2024-02-07", 0.93m)
            .Dividend("SYN", Syn, "2024-05-07", 6.20m).Withholding("SYN", Syn, "2024-05-07", 0.93m)
            .Build();
        var activity2025 = new IbStatement(Account, 2025).Instrument("SYN", Syn).Withholding("SYN", Syn, "2025-02-13", -0.93m).Build();
        using var app = new PitlyApp();

        var y2024 = await app.ImportAsync([("2024.csv", activity2024), ("2025.csv", activity2025)], taxYear: 2024);
        Assert.Equal(HttpStatusCode.OK, y2024.Status);
        Assert.Equal(9.42m, y2024.Field("zryczaltowanyPodatek"));
        Assert.Equal(7.44m, y2024.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(2m, y2024.Field("roznica"));
        var ambiguous = Assert.Single(Of(y2024, DiagnosticCodes.AmbiguousMatch));
        Assert.Equal([2024, 2025], Years(ambiguous));
        Assert.Equal(3.72m, ambiguous.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.Contains("Flex Query", ambiguous.GetProperty("message").GetString());
        Assert.False(Ready(y2024));

        var y2025 = await app.ImportAsync([("2024.csv", activity2024), ("2025.csv", activity2025)], taxYear: 2025);
        Assert.Single(Of(y2025, DiagnosticCodes.AmbiguousMatch));
        Assert.False(Ready(y2025));
    }

    [Fact]
    public async Task Activity_RefundWithoutTheBrokersReferenceFlagsEveryEarlierYearOfTheInstrument()
    {
        // The refund's text names no amount per share, so nothing links it to a distribution. It is not applied
        // (UNMATCHED_WHT) and, because it may reverse tax withheld on any earlier SYN payment, the finding covers
        // 2024 as well as 2025: the 2024 return is not ready and keeps its credit of 3.72 PLN until the Flex Query
        // or a manual check settles it.
        var activity2025 = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Withholding("SYN", Syn, "2025-02-13", -0.93m, description: $"SYN({Syn}) Cash Dividend - US Tax")
            .Build();
        using var app = new PitlyApp();

        var y2024 = await app.ImportAsync([("2024.csv", Activity2024()), ("2025.csv", activity2025)], taxYear: 2024);
        Assert.Equal(HttpStatusCode.OK, y2024.Status);
        Assert.Equal(3.72m, y2024.Field("podatekZaplaconyZaGranica"));
        var unmatched = Assert.Single(Of(y2024, DiagnosticCodes.UnmatchedWithholding));
        Assert.Equal([2024, 2025], Years(unmatched));
        Assert.Equal(3.72m, unmatched.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.False(Ready(y2024));
    }

    [Fact]
    public async Task Activity_RefundWhosePaymentIsNotUploadedFlagsOnlyItsOwnYear()
    {
        // The 2025 refund carries the text of a distribution (0.33 per share) that no uploaded payment has: the
        // statement with that payment is missing. The 2024 payment (0.31 per share) is not the one it corrects,
        // so 2024 stays ready with its credit of 3.72 PLN; 2025 carries the unresolved refund.
        var activity2025 = new IbStatement(Account, 2025).Instrument("SYN", Syn).Withholding("SYN", Syn, "2025-02-13", -0.93m, perShare: "0.33").Build();
        using var app = new PitlyApp();

        var y2024 = await app.ImportAsync([("2024.csv", Activity2024()), ("2025.csv", activity2025)], taxYear: 2024);
        Assert.Equal(3.72m, y2024.Field("podatekZaplaconyZaGranica"));
        Assert.Empty(Of(y2024, DiagnosticCodes.UnmatchedWithholding));
        Assert.True(Ready(y2024));

        var y2025 = await app.ImportAsync([("2024.csv", Activity2024()), ("2025.csv", activity2025)], taxYear: 2025);
        var unmatched = Assert.Single(Of(y2025, DiagnosticCodes.UnmatchedWithholding));
        Assert.Equal([2025], Years(unmatched));
        Assert.Contains("not uploaded", unmatched.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Activity_TwoDistributionsOfOneDayStayApartSoTheTreatyCapAppliesToEach()
    {
        // The same instrument pays two distributions on one day with different texts: 100 USD "0.25 per Share"
        // with 15.01 USD withheld (the broker's rounding puts it a cent above 15%), and 50 USD "1 per Share" with
        // nothing withheld. Netted into one row of 150 USD the cap would be 22.50 and the whole 15.01 creditable
        // (60.04 PLN); kept apart, the first is capped at 15.00 USD and the second has nothing: credit 60 PLN.
        // Flat tax 600 x 19% = 114, difference 54.
        var activity = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Dividend("SYN", Syn, "2025-05-05", 100m, perShare: "0.25").Withholding("SYN", Syn, "2025-05-05", 15.01m, perShare: "0.25")
            .Dividend("SYN", Syn, "2025-05-05", 50m, perShare: "1")
            .Build();
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", activity)], taxYear: 2025);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(2, result.Body.GetProperty("dividends").GetArrayLength());
        Assert.Equal(600m, result.Field("totalDividendsPln"));
        Assert.Equal(60m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(54m, result.Field("roznica"));
        Assert.True(Ready(result));
    }

    // ── P2: a source conflict after a partial row-level match ──

    // Activity: two orders of SYN on 2025-03-03, 2000 and 2500 shares at 10 USD; the sale of all 4500 at 12 USD on
    // 2025-06-03. Flex: the same day as three executions, 2000, 1500 and 1000 shares at 10 USD; the same sale.
    // The first execution is byte for byte the first order (same second, quantity, price), so the row-level step
    // recognises it; the other two add up to the second order. Nothing is doubled and nothing conflicts:
    // proceeds 4500 x 12 x 4 = 216 000, costs 4500 x 10 x 4 = 180 000, gain 36 000, tax 6 840.
    private static string ActivityOrders(bool withThirdOrder = false)
    {
        var s = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Buy("SYN", "2025-03-03, 10:00:00", 2000m, 10m)
            .Buy("SYN", "2025-03-03, 10:05:00", 2500m, 10m);
        if (withThirdOrder)
            s.Buy("SYN", "2025-03-03, 10:10:00", 3000m, 10m);
        return s.Sell("SYN", "2025-06-03, 10:00:00", 4500m, 12m).Build();
    }

    private static string FlexExecutions() => new IbFlexCsv()
        .Buy(Account, "SYN", Syn, "20250303;100000", 2000m, 10m)
        .Buy(Account, "SYN", Syn, "20250303;100500", 1500m, 10m)
        .Buy(Account, "SYN", Syn, "20250303;100700", 1000m, 10m)
        .Sell(Account, "SYN", Syn, "20250603;100000", 4500m, 12m)
        .Build();

    [Fact]
    public async Task Mixed_ExecutionsLeftAfterARowLevelMatchAreReconciledWithTheRestOfTheOrdersNotTheWholeDay()
    {
        var activity = ("activity.csv", ActivityOrders());
        var flex = ("flex.csv", FlexExecutions());
        foreach (var files in new[] { new[] { activity, flex }, new[] { flex, activity }, new[] { activity }, new[] { flex } })
        {
            using var app = new PitlyApp();
            var result = await app.ImportAsync(files, taxYear: 2025);

            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.Equal(216000m, result.Field("innePrzychody"));
            Assert.Equal(180000m, result.Field("inneKoszty"));
            Assert.Equal(6840m, result.Field("podatekNalezny"));
            Assert.DoesNotContain(DiagnosticCodes.SourceConflict, Codes(result));
            Assert.True(Ready(result));
        }
    }

    [Fact]
    public async Task Mixed_AnOrderTheOtherFormatDoesNotExplainIsStillAConflictInEitherOrder()
    {
        // Activity has a third order of 3000 shares that the Flex Query, which covers that day, does not list.
        // Whichever file comes first, the result is blocked with SOURCE_CONFLICT and the sale still costs 180 000
        // (the two agreed orders are consumed first); the third order is never doubled.
        var activity = ("activity.csv", ActivityOrders(withThirdOrder: true));
        var flex = ("flex.csv", FlexExecutions());
        foreach (var files in new[] { new[] { activity, flex }, new[] { flex, activity } })
        {
            using var app = new PitlyApp();
            var result = await app.ImportAsync(files, taxYear: 2025);

            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.Equal(180000m, result.Field("inneKoszty"));
            var conflict = Assert.Single(Of(result, DiagnosticCodes.SourceConflict));
            Assert.Equal([2025], Years(conflict));
            Assert.False(Ready(result));
        }
    }

    [Fact]
    public async Task SourceConflict_OnAPurchaseOfALaterYearDoesNotBlockAnEarlierYear()
    {
        // 2025 is complete and agreed: buy 10 at 100, sell 10 at 120 (gain 800 PLN, tax 152). The 2026 purchase is
        // 5 shares in the Activity Statement and 4 in the Flex Query of the same day: a conflict of 2026, which
        // cannot change the 2025 return.
        var activity2025 = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Buy("SYN", "2025-03-03, 10:00:00", 10m, 100m).Sell("SYN", "2025-06-03, 10:00:00", 10m, 120m).Build();
        var activity2026 = new IbStatement(Account, 2026).Instrument("SYN", Syn).Buy("SYN", "2026-02-02, 10:00:00", 5m, 100m).Build();
        var flex2026 = new IbFlexCsv().Buy(Account, "SYN", Syn, "20260202;100000", 4m, 100m).Build();
        using var app = new PitlyApp();
        var files = new[] { ("a2025.csv", activity2025), ("a2026.csv", activity2026), ("f2026.csv", flex2026) };

        var y2025 = await app.ImportAsync(files, taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, y2025.Status);
        Assert.Equal(152m, y2025.Field("podatekNalezny"));
        Assert.DoesNotContain(DiagnosticCodes.SourceConflict, Codes(y2025));
        Assert.True(Ready(y2025));

        var y2026 = await app.ImportAsync(files, taxYear: 2026);
        var conflict = Assert.Single(Of(y2026, DiagnosticCodes.SourceConflict));
        Assert.Equal([2026], Years(conflict));
        Assert.False(Ready(y2026));
    }

    // ── The level at which interest is rounded ──

    [Fact]
    public async Task Interest_WhenTheYearlyTotalWouldRoundDifferentlyTheResultSaysSoWithoutBlocking()
    {
        // Two payments of 0.01 EUR at 10.51: 0.1051 PLN each. Per payment: base 0.11, tax 0.0209 -> 0.03, twice
        // = 0.06. On the yearly total: 0.2102 -> 0.22 x 19% = 0.0418 -> 0.05. The form gets 0.06 (Pitly's reading,
        // each payment a separate base) and a finding names both figures; it does not block, because the choice
        // is a recorded policy, not missing data. A single payment gives no such finding.
        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR@2025-02-05"] = 10.51m, ["EUR@2025-03-05"] = 10.51m });
        var two = new IbStatement(Account, 2025).Interest("2025-02-05", 0.01m, "EUR").Interest("2025-03-05", 0.01m, "EUR").Build();
        var result = await app.ImportAsync([("two.csv", two)], taxYear: 2025);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(0.06m, result.Field("zryczaltowanyPodatek"));
        var note = Assert.Single(Of(result, DiagnosticCodes.InterestRoundingLevel));
        var message = note.GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("0.06", message);
        Assert.Contains("0.05", message);
        Assert.False(note.GetProperty("blocksFiling").GetBoolean());
        Assert.True(Ready(result));

        var one = new IbStatement(Account, 2025).Interest("2025-02-05", 0.01m, "EUR").Build();
        var single = await app.ImportAsync([("one.csv", one)], taxYear: 2025);
        Assert.Equal(0.03m, single.Field("zryczaltowanyPodatek"));
        Assert.Empty(Of(single, DiagnosticCodes.InterestRoundingLevel));
    }

    // ── The dates a trade is recognised and converted on ──

    [Fact]
    public void Flex_AndGenericTradesKeepTheSettlementDate()
    {
        var flex = FlexParser().Parse(new IbFlexCsv().Buy(Account, "SYN", Syn, "20250303;100000", 10m, 100m, settleDate: "20250305").Build());
        Assert.Equal(new DateTime(2025, 3, 5), Assert.Single(flex.Trades).SettleDate);

        var generic = new GenericCsvStatementParser(NullLogger<GenericCsvStatementParser>.Instance).Parse(
            "broker,account_id,tx_id,direction,symbol,isin,country,currency,price,quantity,amount,commission,operation_datetime,settlement_date\n" +
            $"XB,A,T1,BUY,SYN,{Syn},US,USD,100,10,1000,0,2025-03-03 10:00:00,2025-03-05\n");
        Assert.Equal(new DateTime(2025, 3, 5), Assert.Single(generic.Trades).SettleDate);
    }

    [Fact]
    public async Task Trades_CarryTheSettlementDateAndTheRateDateForTheAuditTrail()
    {
        // Buy 10 at 100 USD on 2025-03-03 (settles 03-05), sell 10 at 120 USD on 2025-06-03 (settles 06-05). Both
        // are converted at the rate of the day before the trade date (art. 11a ust. 1-2); the test rates are dated
        // that way, so the rows show 2025-03-02 and 2025-06-02 as the rate dates and keep the settlement dates.
        // Nothing crosses a year end, so nothing is flagged.
        var flex = new IbFlexCsv()
            .Buy(Account, "SYN", Syn, "20250303;100000", 10m, 100m, settleDate: "20250305")
            .Sell(Account, "SYN", Syn, "20250603;100000", 10m, 120m, settleDate: "20250605")
            .Build();
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", flex)], taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.DoesNotContain(DiagnosticCodes.RecognitionDate, Codes(result));

        var id = result.Body.GetProperty("sessionId").GetString();
        var trades = (await app.GetAsync($"/api/session/{id}/trades?pageSize=10")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, trades.Count);
        Assert.Equal("2025-03-05", trades[0].GetProperty("settleDate").GetString()?[..10]);
        Assert.Equal("2025-03-02", trades[0].GetProperty("rateDate").GetString()?[..10]);
        Assert.Equal("2025-06-05", trades[1].GetProperty("settleDate").GetString()?[..10]);
        Assert.Equal("2025-06-02", trades[1].GetProperty("rateDate").GetString()?[..10]);
    }

    [Fact]
    public async Task Sale_ThatSettlesInTheNextYearIsCountedInTheTradeYearAndBlocksBothYearsUntilConfirmed()
    {
        // Buy 10 at 100 on 2025-03-03; sell 10 at 120 on 2025-12-30, settling 2026-01-02. Pitly counts the sale in
        // 2025 (proceeds 4800, costs 4000, gain 800, tax 152) and says the year is not settled: RECOGNITION_DATE
        // for 2025 and 2026, blocking in both.
        var flex = new IbFlexCsv()
            .Buy(Account, "SYN", Syn, "20250303;100000", 10m, 100m, settleDate: "20250305")
            .Sell(Account, "SYN", Syn, "20251230;100000", 10m, 120m, settleDate: "20260102")
            .Build();
        using var app = new PitlyApp();

        var y2025 = await app.ImportAsync([("flex.csv", flex)], taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, y2025.Status);
        Assert.Equal(4800m, y2025.Field("innePrzychody"));
        Assert.Equal(152m, y2025.Field("podatekNalezny"));
        var finding = Assert.Single(Of(y2025, DiagnosticCodes.RecognitionDate));
        Assert.Equal([2025, 2026], Years(finding));
        Assert.True(finding.GetProperty("blocksFiling").GetBoolean());
        Assert.False(Ready(y2025));

        var y2026 = await app.ImportAsync([("flex.csv", flex)], taxYear: 2026);
        Assert.Equal(HttpStatusCode.OK, y2026.Status);
        Assert.Equal(0m, y2026.Field("innePrzychody"));
        Assert.Single(Of(y2026, DiagnosticCodes.RecognitionDate));
        Assert.False(Ready(y2026));
    }

    [Fact]
    public async Task Sale_InTheLastDaysOfTheYearWithoutASettlementDateIsFlaggedAndOneEarlierIsNot()
    {
        // The Activity Statement gives no settlement date. A sale on 2025-12-31 settles in 2026 under T+1 or T+2,
        // so it is flagged; a sale on 2025-12-15 settles within the year and is not.
        using var app = new PitlyApp();
        var late = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Buy("SYN", "2025-03-03, 10:00:00", 10m, 100m).Sell("SYN", "2025-12-31, 10:00:00", 10m, 120m).Build();
        var flagged = await app.ImportAsync([("late.csv", late)], taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, flagged.Status);
        var finding = Assert.Single(Of(flagged, DiagnosticCodes.RecognitionDate));
        Assert.Contains("no settlement date", finding.GetProperty("message").GetString());
        Assert.False(Ready(flagged));

        var early = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Buy("SYN", "2025-03-03, 10:00:00", 10m, 100m).Sell("SYN", "2025-12-15, 10:00:00", 10m, 120m).Build();
        var clear = await app.ImportAsync([("early.csv", early)], taxYear: 2025);
        Assert.Empty(Of(clear, DiagnosticCodes.RecognitionDate));
        Assert.True(Ready(clear));
    }

    [Fact]
    public void Policies_NameTheTradeDateRuleAndItsSources()
    {
        Assert.Contains("trade", DatePolicy.Current.RecognitionRule, StringComparison.OrdinalIgnoreCase);
        Assert.True(DatePolicy.Current.RecognitionRuleIsInterpretation);
        Assert.Contains(DatePolicy.Current.Sources, s => s.Contains("art. 17 ust. 1ab"));
        Assert.Contains("per payment", RoundingPolicy.Current.InterestRule);
    }
}
