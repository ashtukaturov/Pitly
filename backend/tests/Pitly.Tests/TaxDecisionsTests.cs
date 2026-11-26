using System.Net;
using System.Text.Json;
using Pitly.Api.Data;
using Pitly.Api.Mapping;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>
/// The user's answers to the questions no source settles (recheck R3, 26 November 2026): which date of a trade
/// sets its year and rate, how securities-lending income and payments in lieu are classified, and where a
/// withholding refund booked in a later year belongs. Every case is synthetic and every expected value was
/// computed by hand; the comment of each test is the computation. Sources: art. 11a ust. 1-2 and art. 17 ust. 1ab
/// pkt 1 ustawy o PIT with art. 7 ust. 2 ustawy o obrocie instrumentami finansowymi (dates); art. 30a ust. 1 pkt 3-4
/// and art. 20 ust. 1 ustawy o PIT (classification); art. 30a ust. 9 ustawy o PIT and art. 81 O.p. (correction of
/// the year of the payment).
/// </summary>
public class TaxDecisionsTests
{
    private const string Syn = "US0000000001";
    private const string Account = "U1111111";

    private static List<JsonElement> Diagnostics(PitlyApp.Response result)
        => result.Pit38.GetProperty("diagnostics").EnumerateArray().ToList();

    private static List<JsonElement> Of(PitlyApp.Response result, string code)
        => Diagnostics(result).Where(d => d.GetProperty("code").GetString() == code).ToList();

    private static bool Ready(PitlyApp.Response result) => result.Pit38.GetProperty("readyToFile").GetBoolean();

    private static string Decisions(PitlyApp.Response result, string name)
        => result.Pit38.GetProperty("decisions").GetProperty(name).GetString() ?? string.Empty;

    // ── The date of a trade ──

    // Buy 10 at 100 USD traded 2025-03-03, settled 03-05; sell 10 at 120 USD traded 2025-06-03, settled 06-05.
    // USD is 4.00 on every day except the two settlement days: 4.10 on 2025-03-05 and 3.90 on 2025-06-05 (the test
    // rate service returns the rate keyed by the day it is asked for and dates it the day before).
    private static readonly Dictionary<string, decimal> SettlementDayRates = new()
    {
        ["USD@2025-03-05"] = 4.10m,
        ["USD@2025-06-05"] = 3.90m
    };

    private static string FlexBuyAndSell() => new IbFlexCsv()
        .Buy(Account, "SYN", Syn, "20250303;100000", 10m, 100m, settleDate: "20250305")
        .Sell(Account, "SYN", Syn, "20250603;100000", 10m, 120m, settleDate: "20250605")
        .Build();

    [Fact]
    public async Task SettlementDate_IsTheDefaultAndSetsTheRateOfBothTheCostAndTheProceeds()
    {
        // Costs 10 x 100 x 4.10 = 4100.00; proceeds 10 x 120 x 3.90 = 4680.00; income 580.00; tax 580 x 19% = 110.20
        // -> 110 PLN.
        using var app = new PitlyApp(SettlementDayRates);
        var result = await app.ImportAsync([("flex.csv", FlexBuyAndSell())], taxYear: 2025);

        result.AssertOk();
        Assert.Equal(4680m, result.Field("innePrzychody"));
        Assert.Equal(4100m, result.Field("inneKoszty"));
        Assert.Equal(580m, result.Field("dochod"));
        Assert.Equal(110m, result.Field("podatekNalezny"));
        Assert.Equal("SettlementDate", Decisions(result, "dateBasis"));
        Assert.Equal("SettlementDate", result.Pit38.GetProperty("datePolicy").GetProperty("basis").GetString());
        Assert.Empty(Of(result, DiagnosticCodes.SettlementDateMissing));
        Assert.True(Ready(result));
    }

    [Fact]
    public async Task TradeDate_DecisionConvertsAtTheRateOfTheDayBeforeTheTrade()
    {
        // Costs 10 x 100 x 4.00 = 4000.00; proceeds 10 x 120 x 4.00 = 4800.00; income 800.00; tax 152 PLN.
        using var app = new PitlyApp(SettlementDayRates);
        var result = await app.ImportAsync([("flex.csv", FlexBuyAndSell())], taxYear: 2025, decisionsJson: """{"dateBasis":"TradeDate"}""");

        result.AssertOk();
        Assert.Equal(4800m, result.Field("innePrzychody"));
        Assert.Equal(4000m, result.Field("inneKoszty"));
        Assert.Equal(152m, result.Field("podatekNalezny"));
        Assert.Equal("TradeDate", Decisions(result, "dateBasis"));
        Assert.Equal("TradeDate", result.Pit38.GetProperty("datePolicy").GetProperty("basis").GetString());
    }

    [Fact]
    public async Task ActivityStatement_WithoutSettlementDatesIsDatedByTheTradeDateAndSaysSo()
    {
        // The Activity Statement gives no settlement dates: buy and sell of 2025 are converted by their trade dates
        // (USD 4: proceeds 4800, costs 4000) and one note counts the two trades. The note does not block.
        var activity = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Buy("SYN", "2025-03-03, 10:00:00", 10m, 100m).Sell("SYN", "2025-06-03, 10:00:00", 10m, 120m).Build();
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", activity)], taxYear: 2025);

        result.AssertOk();
        Assert.Equal(4800m, result.Field("innePrzychody"));
        var note = Assert.Single(Of(result, DiagnosticCodes.SettlementDateMissing));
        Assert.StartsWith("2 trades of 2025", note.GetProperty("message").GetString());
        Assert.False(note.GetProperty("blocksFiling").GetBoolean());
        Assert.True(Ready(result));
    }

    [Fact]
    public async Task DecemberSaleWithoutSettlementDate_BlocksBySettlementDateAndIsResolvedByTheTradeDate()
    {
        // A sale on 2025-12-31 without a settlement date settles in 2026 under T+1 or T+2: by the settlement date
        // its year is not known (blocking); by the trade date it belongs to 2025 (a note, ready to file).
        var late = new IbStatement(Account, 2025).Instrument("SYN", Syn)
            .Buy("SYN", "2025-03-03, 10:00:00", 10m, 100m).Sell("SYN", "2025-12-31, 10:00:00", 10m, 120m).Build();
        using var app = new PitlyApp();

        var bySettlement = await app.ImportAsync([("late.csv", late)], taxYear: 2025);
        bySettlement.AssertOk();
        Assert.True(Assert.Single(Of(bySettlement, DiagnosticCodes.RecognitionDate)).GetProperty("blocksFiling").GetBoolean());
        Assert.False(Ready(bySettlement));

        var byTrade = await app.ImportAsync([("late.csv", late)], taxYear: 2025, decisionsJson: """{"dateBasis":"TradeDate"}""");
        byTrade.AssertOk();
        Assert.Equal(4800m, byTrade.Field("innePrzychody"));
        Assert.False(Assert.Single(Of(byTrade, DiagnosticCodes.RecognitionDate)).GetProperty("blocksFiling").GetBoolean());
        Assert.True(Ready(byTrade));
    }

    // ── Securities-lending income ──

    // 100 USD of credit interest and 10 USD of stock yield enhancement income (USD 4: 400 and 40 PLN).
    private static string InterestAndLending() => new IbStatement(Account, 2025)
        .Interest("2025-02-05", 100m, "USD", "USD Credit Interest for Jan-2025")
        .Interest("2025-02-05", 10m, "USD", "USD SYEP Interest for Jan-2025")
        .Build();

    [Fact]
    public async Task Lending_ConfirmedAsInterestStaysInSectionGAndIsReady()
    {
        // Interest is rounded per payment (art. 63 par. 1a O.p.): 400.00 x 19% = 76.00 and 40.00 x 19% = 7.60,
        // flat tax 83.60, nothing paid abroad, difference 83.60.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", InterestAndLending())], decisionsJson: """{"securitiesLending":"Interest"}""");

        result.AssertOk();
        Assert.Equal(440m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(83.60m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(83.60m, result.Field("roznica"));
        Assert.Empty(Of(result, DiagnosticCodes.ClassificationUnverified));
        var note = Assert.Single(Of(result, DiagnosticCodes.ClassificationDecided));
        Assert.Equal("securitiesLending", note.GetProperty("decision").GetString());
        Assert.False(note.GetProperty("blocksFiling").GetBoolean());
        Assert.Equal(0m, result.Field("otherSourcesIncomePln"));
        Assert.True(Ready(result));
    }

    [Fact]
    public async Task Lending_AsOtherSourcesLeavesPit38AndIsListedForPit36()
    {
        // Section G keeps the credit interest only: 400.00 x 19% = 76.00. The 40.00 PLN of lending income is listed
        // as income from other sources (art. 20 ust. 1) for PIT-36.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", InterestAndLending())], decisionsJson: """{"securitiesLending":"OtherSources"}""");

        result.AssertOk();
        Assert.Equal(400m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(76.00m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(40m, result.Field("otherSourcesIncomePln"));
        Assert.Equal(0m, result.Field("otherSourcesWithholdingPln"));
        var listed = Assert.Single(result.Pit38.GetProperty("otherSourcesIncome").EnumerateArray());
        Assert.Equal(10m, listed.GetProperty("amountOriginal").GetDecimal());
        Assert.Equal(40m, listed.GetProperty("amountPln").GetDecimal());
        Assert.Empty(Of(result, DiagnosticCodes.ClassificationUnverified));
        Assert.Contains("PIT-36", Assert.Single(Of(result, DiagnosticCodes.ClassificationDecided)).GetProperty("message").GetString());
        Assert.True(Ready(result));
    }

    // ── Payments in lieu ──

    // One Flex action in PLN: a dividend of 80 with 12 withheld and a payment in lieu of 20 with 3 withheld. The
    // issuer is in the US (treaty rate 15%).
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
    public async Task PaymentInLieu_ConfirmedAsDividendIsReady()
    {
        // 100 x 19% = 19.00; credit 12 + 3 = 15.00 (each within 15% of its payment); difference 4.00 -> 4 PLN.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", FlexDividendAndPaymentInLieu())], decisionsJson: """{"paymentsInLieu":"Dividend"}""");

        result.AssertOk();
        Assert.Equal(19.00m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(15.00m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(4m, result.Field("roznica"));
        Assert.True(Ready(result));
    }

    [Fact]
    public async Task PaymentInLieu_AsOtherSourcesLeavesPit38WithItsWithholding()
    {
        // Section G keeps the dividend: 80 x 19% = 15.20, credit 12.00, difference 3.20 -> 3 PLN. The payment in lieu
        // (20.00 PLN, 3.00 PLN withheld) is listed for PIT-36.
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("flex.csv", FlexDividendAndPaymentInLieu())], decisionsJson: """{"paymentsInLieu":"OtherSources"}""");

        result.AssertOk();
        Assert.Equal(15.20m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(12.00m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(3m, result.Field("roznica"));
        Assert.Equal(20m, result.Field("otherSourcesIncomePln"));
        Assert.Equal(3m, result.Field("otherSourcesWithholdingPln"));
        Assert.True(Ready(result));
    }

    // ── A refund booked in the next year ──

    // 2024: a dividend of 6.20 USD on 2024-02-07 with 0.93 USD withheld (USD 4: 24.80 PLN, 3.72 PLN). 2025: the
    // refund of the whole 0.93 USD on 2025-02-13 under the text of the 2024 distribution.
    private static string Activity2024() => new IbStatement(Account, 2024).Instrument("SYN", Syn)
        .Dividend("SYN", Syn, "2024-02-07", 6.20m, perShare: "0.31")
        .Withholding("SYN", Syn, "2024-02-07", 0.93m, perShare: "0.31")
        .Build();

    private static string Activity2025Refund() => new IbStatement(Account, 2025).Instrument("SYN", Syn)
        .Withholding("SYN", Syn, "2025-02-13", -0.93m, perShare: "0.31")
        .Build();

    [Fact]
    public async Task CrossYearRefund_AsACorrectionOfThePaymentYearIsReadyInBothYears()
    {
        // 2024: flat tax 24.80 x 19% = 4.712 -> 4.71; paid abroad 0 (refunded); difference 4.712 -> 5 PLN. The finding
        // becomes a note for 2024 and 2025 that tells the user to correct 2024 if it was filed with the credit.
        using var app = new PitlyApp();
        var files = new[] { ("2024.csv", Activity2024()), ("2025.csv", Activity2025Refund()) };
        const string decided = """{"crossYearRefunds":"CorrectOriginalYear"}""";

        var y2024 = await app.ImportAsync(files, taxYear: 2024, decisionsJson: decided);
        y2024.AssertOk();
        Assert.Equal(4.71m, y2024.Field("zryczaltowanyPodatek"));
        Assert.Equal(0m, y2024.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(5m, y2024.Field("roznica"));
        var note = Assert.Single(Of(y2024, DiagnosticCodes.CrossYearAdjustment));
        Assert.False(note.GetProperty("blocksFiling").GetBoolean());
        Assert.Equal("crossYearRefunds", note.GetProperty("decision").GetString());
        Assert.Contains("corrected return for 2024", note.GetProperty("message").GetString());
        Assert.True(Ready(y2024));

        var y2025 = await app.ImportAsync(files, taxYear: 2025, decisionsJson: decided);
        y2025.AssertOk();
        Assert.Single(Of(y2025, DiagnosticCodes.CrossYearAdjustment));
        Assert.True(Ready(y2025));

        var undecided = await app.ImportAsync(files, taxYear: 2024);
        Assert.True(Assert.Single(Of(undecided, DiagnosticCodes.CrossYearAdjustment)).GetProperty("blocksFiling").GetBoolean());
        Assert.False(Ready(undecided));
    }

    // ── The API and the stored session ──

    [Theory]
    [InlineData("""{"dateBasis":"Tomorrow"}""")]
    [InlineData("""{"dateBasis":1}""")]
    [InlineData("""{"lending":"Interest"}""")]
    [InlineData("not json")]
    public async Task Import_RejectsDecisionsItCannotRead(string json)
    {
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", InterestAndLending())], decisionsJson: json);

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Contains("decisions must be", result.Error);
    }

    [Fact]
    public async Task StoredSession_GivesBackTheDecisionsAndTheIncomeLeftOut()
    {
        using var app = new PitlyApp();
        var result = await app.ImportAsync([("activity.csv", InterestAndLending())],
            decisionsJson: """{"dateBasis":"TradeDate","securitiesLending":"OtherSources","crossYearRefunds":"CorrectOriginalYear"}""");
        result.AssertOk();

        var id = result.Body.GetProperty("sessionId").GetString();
        var stored = await app.GetAsync($"/api/session/{id}/pit38");
        stored.AssertOk();
        Assert.Equal("TradeDate", stored.Body.GetProperty("decisions").GetProperty("dateBasis").GetString());
        Assert.Equal("OtherSources", stored.Body.GetProperty("decisions").GetProperty("securitiesLending").GetString());
        Assert.Equal("Unconfirmed", stored.Body.GetProperty("decisions").GetProperty("paymentsInLieu").GetString());
        Assert.Equal("CorrectOriginalYear", stored.Body.GetProperty("decisions").GetProperty("crossYearRefunds").GetString());
        Assert.Equal(40m, stored.Body.GetProperty("otherSourcesIncomePln").GetDecimal());
        Assert.Equal(result.Field("zryczaltowanyPodatek"), stored.Body.GetProperty("zryczaltowanyPodatek").GetDecimal());
    }

    [Fact]
    public void OtherSourcesTotals_AddUpTheListedFiguresInZlotyAndGrosz()
    {
        // Two payments of 40.865 and 74.425 PLN are listed in zl, gr as 40.87 and 74.43 (half away from zero). The total
        // adds up the listed figures: 115.30. Rounding the exact sum (115.29) would not match the rows. The foreign
        // tax of 11.165 PLN is listed and totalled as 11.17.
        var summary = new TaxSummary(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 2025, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), [], [],
            OtherSourcesIncome:
            [
                new OtherSourceIncome("A", "USD", new DateTime(2025, 2, 5), IncomeKind.Interest, 10m, 40.865m, 0m, 4.0865m),
                new OtherSourceIncome("B", "USD", new DateTime(2025, 6, 24), IncomeKind.PaymentInLieu, 20m, 74.425m, 11.165m, 3.72125m)
            ]);

        var pit38 = Pit38Fields.FromSummary(summary);

        Assert.Equal(115.30m, pit38.OtherSourcesIncomePln);
        Assert.Equal(11.17m, pit38.OtherSourcesWithholdingPln);
    }

    [Fact]
    public void SessionStoredBeforeDecisions_WasComputedByTheTradeDateWithNothingConfirmed()
    {
        var session = new SessionEntity { Year = 2025, DecisionsJson = null };
        var summary = EntityMapper.ToSummary(session);

        Assert.Equal(TaxDecisions.Legacy, summary.Decisions);
        Assert.Equal(TradeDateBasis.TradeDate, Pit38Fields.FromSummary(summary).DatePolicy!.Basis);
    }
}
