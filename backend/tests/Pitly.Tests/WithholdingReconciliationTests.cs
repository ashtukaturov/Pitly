using System.Net;
using System.Text.Json;

namespace Pitly.Tests;

/// <summary>
/// Acceptance cases for the attribution of foreign tax to the payment it was withheld on (or refunded for),
/// end to end through POST /api/import. Rates are fixed at 4 PLN per USD and 1 PLN per PLN (see <see cref="PitlyApp"/>).
/// Each case names the defect it was written against.
/// </summary>
public class WithholdingReconciliationTests
{
    private const string Aapl = "US0378331005";
    private const string Msft = "US5949181045";

    private static List<JsonElement> Diagnostics(PitlyApp.Response result)
        => result.Pit38.GetProperty("diagnostics").EnumerateArray().ToList();

    private static List<string> Codes(PitlyApp.Response result)
        => Diagnostics(result).Select(d => d.GetProperty("code").GetString() ?? string.Empty).ToList();

    // ── A. Tax withheld on credit interest has no ticker or ISIN ──

    [Fact]
    public async Task Import_WithholdingOnCreditInterestWithoutIsinIsCreditedAgainstThatInterest()
    {
        // PLN 100 of credit interest for January, PLN 10 withheld on it, one account, no dividends.
        var statement = new IbStatement("U1111111", 2025)
            .Interest("2025-02-05", 100m, "PLN", "CREDIT INT FOR JAN-2025")
            .InterestWithholding("2025-02-05", 10m, "PLN", "WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", statement.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(100m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(10m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());
        Assert.Equal(10m, result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal());
        Assert.Equal(19m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(10m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(9m, result.Field("roznica"));        // interest: 19 - 10 = 9.00, grosz precision kept
        Assert.Empty(result.Strings(result.Pit38.GetProperty("dataWarnings")));

        var row = Assert.Single(result.Body.GetProperty("dividends").EnumerateArray());
        Assert.Equal("Interest", row.GetProperty("kind").GetString());
        Assert.Equal(10m, row.GetProperty("withholdingTaxPln").GetDecimal());
    }

    [Fact]
    public async Task Import_InterestWithholdingForAnotherPeriodIsNotCreditedToTheWrongInterest()
    {
        // The only interest row is for January; the tax row names February. Same account, currency and day, but the
        // period says it is not the same income, so it is reported instead of being credited to a random payment.
        var statement = new IbStatement("U1111111", 2025)
            .Interest("2025-02-05", 100m, "PLN", "CREDIT INT FOR JAN-2025")
            .InterestWithholding("2025-02-05", 15m, "PLN", "WITHHOLDING @ 10% ON CREDIT INT FOR FEB-2025");

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", statement.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(100m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(0m, result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal());
        var diagnostic = Assert.Single(Diagnostics(result), d => d.GetProperty("code").GetString() == "UNMATCHED_WHT");
        Assert.Equal(15m, diagnostic.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.Contains("FEB-2025", diagnostic.GetProperty("message").GetString());
        Assert.False(result.Pit38.GetProperty("readyToFile").GetBoolean());

        // A withholding row of a kind Pitly cannot read is reported too, never guessed.
        var unknown = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 25m)
            .InterestWithholding("2025-05-15", 2m, "USD", "WITHHOLDING ON SOMETHING ELSE");
        var unknownResult = await app.ImportAsync([("2025.csv", unknown.Build())]);
        Assert.Contains("UNKNOWN_INCOME_KIND", Codes(unknownResult));
        Assert.Equal(0m, unknownResult.Summary.GetProperty("totalWithholdingPln").GetDecimal());
    }

    // ── B. A refund on account A never reduces the credit of account B ──

    [Fact]
    public async Task Import_ExcessRefundOnOneAccountDoesNotReduceTheCreditOfAnotherAccountOrInstrument()
    {
        // Account A: dividend 25 USD (100 PLN), 3.75 USD (15 PLN) withheld, then 5 USD (20 PLN) refunded.
        var accountA = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 25m)
            .Withholding("AAPL", Aapl, "2025-05-15", 3.75m)
            .Withholding("AAPL", Aapl, "2025-05-15", -5m);
        // Account B: a different instrument, dividend 25 USD, 3.75 USD withheld, nothing refunded.
        var accountB = new IbStatement("U2222222", 2025)
            .Dividend("MSFT", Msft, "2025-05-15", 25m)
            .Withholding("MSFT", Msft, "2025-05-15", 3.75m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a.csv", accountA.Build()), ("b.csv", accountB.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var rows = result.Body.GetProperty("dividends").EnumerateArray().ToList();
        var onA = rows.Single(r => r.GetProperty("symbol").GetString() == "AAPL");
        var onB = rows.Single(r => r.GetProperty("symbol").GetString() == "MSFT");
        Assert.Equal(0m, onA.GetProperty("withholdingTaxPln").GetDecimal());
        Assert.Equal(15m, onB.GetProperty("withholdingTaxPln").GetDecimal());          // untouched by A's refund
        Assert.Equal(15m, onB.GetProperty("creditableWithholdingPln").GetDecimal());
        Assert.Equal(15m, result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal());
        Assert.Equal(15m, result.Field("podatekZaplaconyZaGranica"));

        // The 5 PLN excess stays visible as an unresolved amount instead of being spread anywhere.
        var excess = Assert.Single(Diagnostics(result), d => d.GetProperty("code").GetString() == "EXCESS_REFUND");
        Assert.Equal(5m, excess.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.Equal("U1111111", excess.GetProperty("accountId").GetString());
        var message = excess.GetProperty("message").GetString()!;
        Assert.DoesNotContain("offset", message);
        Assert.DoesNotContain("probably", message);
        Assert.False(result.Pit38.GetProperty("readyToFile").GetBoolean());
    }

    // ── C. A refund booked in the next year points at the payment of the previous year ──

    [Fact]
    public async Task Import_RefundBookedNextYearStaysWithThePaymentOfTheEarlierYear()
    {
        // 2024: dividend 25 USD (100 PLN) with 3.75 USD (15 PLN) withheld.
        var year2024 = new IbStatement("U1111111", 2024)
            .Dividend("AAPL", Aapl, "2024-05-15", 25m)
            .Withholding("AAPL", Aapl, "2024-05-15", 3.75m);
        // 2025: the whole 2024 tax is refunded (dated back to the 2024 payment) and an unrelated dividend is paid.
        var year2025 = new IbStatement("U1111111", 2025)
            .Withholding("AAPL", Aapl, "2024-05-15", -3.75m)
            .Dividend("MSFT", Msft, "2025-05-15", 25m)
            .Withholding("MSFT", Msft, "2025-05-15", 3.75m);
        var files = new[] { ("2024.csv", year2024.Build()), ("2025.csv", year2025.Build()) };

        using var app = new PitlyApp();

        // 2025: the other dividend keeps its full credit and nothing is unresolved about it.
        var result2025 = await app.ImportAsync(files, taxYear: 2025);
        Assert.Equal(HttpStatusCode.OK, result2025.Status);
        var msft = Assert.Single(result2025.Body.GetProperty("dividends").EnumerateArray());
        Assert.Equal(15m, msft.GetProperty("creditableWithholdingPln").GetDecimal());
        Assert.Equal(15m, result2025.Field("podatekZaplaconyZaGranica"));
        Assert.DoesNotContain("EXCESS_REFUND", Codes(result2025));
        Assert.DoesNotContain("UNMATCHED_WHT", Codes(result2025));
        // The cross-year refund is reported for both years, because its tax year is a policy question.
        var crossYear = Assert.Single(Diagnostics(result2025), d => d.GetProperty("code").GetString() == "CROSS_YEAR_ADJUSTMENT");
        Assert.Equal([2024, 2025], crossYear.GetProperty("affectedYears").EnumerateArray().Select(y => y.GetInt32()));
        Assert.Equal(15m, crossYear.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.DoesNotContain("probably", crossYear.GetProperty("message").GetString());

        // 2024: the refund is attributed to the 2024 payment, whose credit is therefore gone.
        var result2024 = await app.ImportAsync(files, taxYear: 2024);
        Assert.Equal(HttpStatusCode.OK, result2024.Status);
        var aapl = Assert.Single(result2024.Body.GetProperty("dividends").EnumerateArray());
        Assert.Equal(0m, aapl.GetProperty("withholdingTaxPln").GetDecimal());
        Assert.Equal(0m, result2024.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(19m, result2024.Field("roznica"));
        Assert.Contains("CROSS_YEAR_ADJUSTMENT", Codes(result2024));
        Assert.DoesNotContain("EXCESS_REFUND", Codes(result2024));
    }

    [Fact]
    public async Task Import_RefundWhoseOriginalPaymentIsNotUploadedSaysSoWithoutGuessing()
    {
        // Only the 2025 statement is uploaded: the refund points at a 2024 payment that is not in the history.
        var year2025 = new IbStatement("U1111111", 2025)
            .Withholding("AAPL", Aapl, "2024-05-15", -3.75m)
            .Dividend("MSFT", Msft, "2025-05-15", 25m)
            .Withholding("MSFT", Msft, "2025-05-15", 3.75m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", year2025.Build())], taxYear: 2025);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(15m, result.Field("podatekZaplaconyZaGranica")); // MSFT is untouched
        var unmatched = Assert.Single(Diagnostics(result), d => d.GetProperty("code").GetString() == "UNMATCHED_WHT");
        Assert.Equal(15m, unmatched.GetProperty("unresolvedAmountPln").GetDecimal());
        Assert.Contains("2024", unmatched.GetProperty("message").GetString());
        Assert.Contains("not uploaded", unmatched.GetProperty("message").GetString());
    }

    // ── D. Properties that were already right ──

    [Fact]
    public async Task Import_DividendRoundingAndInterestRoundingStaySeparate()
    {
        // 103 PLN of dividends with 15.10 PLN withheld: 19.57 - 15.10 = 4.47 -> 4 PLN.
        var dividend = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 25.75m)
            .Withholding("AAPL", Aapl, "2025-05-15", 3.775m);
        // 100 PLN of bank interest with 10 PLN withheld: 19.00 - 10.00 = 9.00 PLN, kept to the grosz.
        var interest = new IbStatement("U2222222", 2025)
            .Interest("2025-02-05", 100m, "PLN", "CREDIT INT FOR JAN-2025")
            .InterestWithholding("2025-02-05", 10m, "PLN", "WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025");

        using var app = new PitlyApp();
        var onlyDividend = await app.ImportAsync([("d.csv", dividend.Build())]);
        Assert.Equal(4m, onlyDividend.Field("roznica"));

        var onlyInterest = await app.ImportAsync([("i.csv", interest.Build())]);
        Assert.Equal(9m, onlyInterest.Field("roznica"));

        // Both streams in one return: each is rounded by its own rule and the assumption about the mix is stated.
        var both = await app.ImportAsync([("d.csv", dividend.Build()), ("i.csv", interest.Build())]);
        Assert.Equal(13m, both.Field("roznica"));
        Assert.Contains("MIXED_STREAM_ROUNDING", Codes(both));
        Assert.NotEqual(JsonValueKind.Null, both.Pit38.GetProperty("roundingPolicy").ValueKind);
    }

    // ── Precision: nothing is rounded before the form field asks for it ──

    [Fact]
    public async Task Import_InterestOfOneCentIsNotRoundedBeforeTheTaxIsComputed()
    {
        // 0.01 EUR at a synthetic fixed rate of 10.51 PLN/EUR = 0.1051 PLN. The summary keeps that exact figure.
        // Recheck P1-07: the earlier expectation of 0.02 PLN (19% of the unrounded gross, rounded up) was wrong.
        // Art. 63 § 1a O.p. rounds the base up to the grosz as well as the tax: base 0.11, 19% = 0.0209 -> 0.03 PLN.
        // (10.51 is a test rate, not a quotation.)
        var statement = new IbStatement("U1111111", 2025).Interest("2025-01-31", 0.01m, "EUR", "EUR CREDIT INT FOR JAN-2025");

        using var app = new PitlyApp(new Dictionary<string, decimal> { ["EUR"] = 10.51m });
        var result = await app.ImportAsync([("2025.csv", statement.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(0.1051m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(0.03m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(0.03m, result.Field("roznica"));
        Assert.Equal(0.03m, result.Field("podatekDoZaplaty"));

        // The stored session gives the same figures after a reload.
        var sessionId = result.Body.GetProperty("sessionId").GetString();
        var reopened = await app.GetAsync($"/api/session/{sessionId}/pit38");
        Assert.Equal(0.03m, reopened.Body.GetProperty("podatekDoZaplaty").GetDecimal());
        var summary = await app.GetAsync($"/api/session/{sessionId}/summary");
        Assert.Equal(0.1051m, summary.Body.GetProperty("totalDividendsPln").GetDecimal());
    }
}
