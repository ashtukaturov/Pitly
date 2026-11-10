using System.Net;
using System.Text.Json;

namespace Pitly.Tests;

/// <summary>
/// End-to-end: synthetic CSV statements go through POST /api/import of the real application and the final PIT-38
/// fields are checked. Exchange rates are fixed at 4 PLN per USD (see <see cref="PitlyApp"/>), so 25 USD = 100 PLN.
/// </summary>
public class EndToEndImportTests
{
    private const string Aapl = "US0378331005";
    private const string Msft = "US5949181045";

    // ── 1. FIFO and corporate actions stay inside one broker account ──

    [Fact]
    public async Task Import_SaleOnOneAccountDoesNotConsumeTheOtherAccountsLot()
    {
        // Account A bought one share for 100 PLN earlier; account B bought one for 300 PLN later and sold it for 350 PLN.
        var accountA = new IbStatement("U1111111", 2024).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2024-03-01, 10:00:00", 1m, 25m);
        var accountB = new IbStatement("U2222222", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-02-03, 10:00:00", 1m, 75m)
            .Sell("AAPL", "2025-03-03, 10:00:00", 1m, 87.5m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a-2024.csv", accountA.Build()), ("b-2025.csv", accountB.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(350m, result.Field("innePrzychody"));
        Assert.Equal(300m, result.Field("inneKoszty")); // a shared FIFO queue would have used A's 100 PLN lot
        Assert.Equal(50m, result.Field("dochod"));
        Assert.Equal(10m, result.Field("podatekDoZaplaty")); // 19% of 50 PLN = 9.50 -> 10 PLN
        Assert.True(File.Exists(app.DatabasePath), "the test must run on its own temporary database");
    }

    [Fact]
    public async Task Import_ShareLeftOnTheFirstAccountKeepsItsOwnCost()
    {
        var accountA2024 = new IbStatement("U1111111", 2024).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2024-03-01, 10:00:00", 1m, 25m);
        var accountB = new IbStatement("U2222222", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-02-03, 10:00:00", 1m, 75m)
            .Sell("AAPL", "2025-03-03, 10:00:00", 1m, 87.5m);
        // Later in the year account A sells its share for 120 PLN: the cost must still be A's own 100 PLN.
        var accountA2025 = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .OpeningPosition("AAPL", 1m)
            .Sell("AAPL", "2025-06-02, 10:00:00", 1m, 30m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync(
            [("a-2024.csv", accountA2024.Build()), ("b-2025.csv", accountB.Build()), ("a-2025.csv", accountA2025.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var sells = result.Body.GetProperty("trades").EnumerateArray().Where(t => t.GetProperty("type").GetString() == "Sell").ToList();
        var saleOnA = sells.Single(t => t.GetProperty("dateTime").GetString()!.StartsWith("2025-06-02"));
        var saleOnB = sells.Single(t => t.GetProperty("dateTime").GetString()!.StartsWith("2025-03-03"));
        Assert.Equal(100m, saleOnA.GetProperty("costPln").GetDecimal());
        Assert.Equal(20m, saleOnA.GetProperty("gainLossPln").GetDecimal());
        Assert.Equal(300m, saleOnB.GetProperty("costPln").GetDecimal());
        Assert.Equal(50m, saleOnB.GetProperty("gainLossPln").GetDecimal());
        Assert.Equal(70m, result.Field("dochod"));
        // The reconstructed positions equal the opening position A's 2025 statement reports: nothing to warn about.
        Assert.Empty(result.Strings(result.Pit38.GetProperty("dataWarnings")));
    }

    [Fact]
    public async Task Import_SplitReportedByBothStatementsDoublesEachAccountExactlyOnce()
    {
        // Both statements report the same 2-for-1 split with identical rows.
        var accountA = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-01-10, 10:00:00", 1m, 25m)
            .Split("AAPL", Aapl, "2025-06-01, 20:25:00", 2, 1, creditedShares: 1m)
            .Sell("AAPL", "2025-07-01, 10:00:00", 2m, 15m);
        var accountB = new IbStatement("U2222222", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-01-11, 10:00:00", 1m, 75m)
            .Split("AAPL", Aapl, "2025-06-01, 20:25:00", 2, 1, creditedShares: 1m)
            .Sell("AAPL", "2025-07-02, 10:00:00", 2m, 40m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a.csv", accountA.Build()), ("b.csv", accountB.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var sells = result.Body.GetProperty("trades").EnumerateArray().Where(t => t.GetProperty("type").GetString() == "Sell").ToList();
        // Each account holds exactly two shares after the split, carrying the full cost of its one original share.
        Assert.Equal(100m, sells.Single(t => t.GetProperty("dateTime").GetString()!.StartsWith("2025-07-01")).GetProperty("costPln").GetDecimal());
        Assert.Equal(300m, sells.Single(t => t.GetProperty("dateTime").GetString()!.StartsWith("2025-07-02")).GetProperty("costPln").GetDecimal());
        Assert.Equal(440m, result.Field("innePrzychody")); // 2 x 15 USD + 2 x 40 USD = 110 USD
        Assert.Equal(400m, result.Field("inneKoszty"));

        // A third share was never there: selling three from one account must fail instead of borrowing from the other.
        var oversold = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-01-10, 10:00:00", 1m, 25m)
            .Split("AAPL", Aapl, "2025-06-01, 20:25:00", 2, 1, creditedShares: 1m)
            .Sell("AAPL", "2025-07-01, 10:00:00", 3m, 15m);
        var failed = await app.ImportAsync([("a.csv", oversold.Build()), ("b.csv", accountB.Build())]);
        Assert.Equal(HttpStatusCode.BadRequest, failed.Status);
        Assert.Contains("only 2 shares available", failed.Error);
        Assert.Contains("U1111111", failed.Error);
    }

    // ── 2. Merging statements and attributing withholding tax ──

    [Fact]
    public async Task Import_SameDividendOnTwoAccountsCountsTwiceButARepeatedStatementDoesNot()
    {
        IbStatement Statement(string account, string period) => new IbStatement(account, period)
            .Dividend("AAPL", Aapl, "2025-05-15", 25.75m)      // 103 PLN
            .Withholding("AAPL", Aapl, "2025-05-15", 3.775m);  // 15.10 PLN

        var accountA = Statement("U1111111", "January 1, 2025 - December 31, 2025").Build();
        var accountB = Statement("U2222222", "January 1, 2025 - December 31, 2025").Build();
        // The same account exported again for a shorter period: same rows, different file.
        var accountBAgain = Statement("U2222222", "January 1, 2025 - June 30, 2025").Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("a.csv", accountA), ("b.csv", accountB), ("b-jan-jun.csv", accountBAgain)]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(2, result.Body.GetProperty("dividends").GetArrayLength());
        Assert.Equal(206m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(30.20m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());
        Assert.Equal(30.20m, result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal());
        Assert.Contains(result.Strings(result.Summary.GetProperty("warnings")), w => w.Contains("b-jan-jun.csv") && w.Contains("only repeats data"));

        Assert.Equal(39.14m, result.Field("zryczaltowanyPodatek"));       // 206 x 19%
        Assert.Equal(30.20m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(9m, result.Field("roznica"));                        // 8.94 -> 9 PLN
    }

    [Fact]
    public async Task Import_PaymentInLieuListedAmongDividendsKeepsItsOwnWithholding()
    {
        // As in real IB statements: a cash dividend and a payment in lieu for the same instrument on the same day,
        // both in the Dividends section, each with its own withholding row.
        var csv = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 100m).Withholding("AAPL", Aapl, "2025-05-15", 15m)
            .Build()
            .Replace("Withholding Tax,Header", "Dividends,Data,USD,2025-05-15,AAPL(US0378331005) Payment in Lieu of Dividend (Ordinary Dividend),10\nWithholding Tax,Header")
            + "Withholding Tax,Data,USD,2025-05-15,AAPL(US0378331005) Payment in Lieu of Dividend - US Tax,-3,\n";

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", csv)]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var rows = result.Body.GetProperty("dividends").EnumerateArray().ToList();
        var dividend = rows.Single(r => r.GetProperty("kind").GetString() == "Dividend");
        var inLieu = rows.Single(r => r.GetProperty("kind").GetString() == "PaymentInLieu");
        Assert.Equal(60m, dividend.GetProperty("creditableWithholdingPln").GetDecimal());   // 15 USD on 100 USD
        Assert.Equal(12m, inLieu.GetProperty("withholdingTaxPln").GetDecimal());            // 3 USD on 10 USD
        Assert.Equal(6m, inLieu.GetProperty("creditableWithholdingPln").GetDecimal());      // capped at 15% of 40 PLN
        Assert.Empty(result.Strings(result.Pit38.GetProperty("dataWarnings")));
    }

    [Fact]
    public async Task Import_CreditNeverExceedsTheTaxActuallyWithheldAfterCorrections()
    {
        var year2025 = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 100m).Withholding("AAPL", Aapl, "2025-05-15", 15m)
            .Dividend("AAPL", Aapl, "2025-08-15", 100m).Withholding("AAPL", Aapl, "2025-08-15", 15m);
        // Booked in the next year's statement and dated back to the May dividend: a refund of 20 USD, 5 USD more than
        // was withheld on that payment.
        var year2026 = new IbStatement("U1111111", 2026)
            .Withholding("AAPL", Aapl, "2025-05-15", -20m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", year2025.Build()), ("2026.csv", year2026.Build())], taxYear: 2025);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        // Actually withheld: (15 - 20 + 15) USD = 10 USD = 40 PLN. Without the netting the August 60 PLN would be credited.
        var creditable = result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal();
        Assert.Equal(40m, creditable);
        Assert.Equal(40m, result.Summary.GetProperty("totalWithholdingPln").GetDecimal());
        Assert.Equal(40m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Contains(result.Strings(result.Pit38.GetProperty("dataWarnings")), w => w.Contains("refund for AAPL"));
    }

    [Fact]
    public async Task Import_StatementsWithoutAccountNumberCanBeMarkedAsSeparateAccounts()
    {
        var first = new Trading212Export()
            .Buy("A1", "2025-01-10 10:00:00", "AAPL", Aapl, 1m, 25m);
        var second = new Trading212Export()
            .Buy("B1", "2025-02-03 10:00:00", "AAPL", Aapl, 1m, 75m)
            .Sell("B2", "2025-03-03 10:00:00", "AAPL", Aapl, 1m, 87.5m);
        var files = new[] { ("first.csv", first.Build()), ("second.csv", second.Build()) };

        using var app = new PitlyApp();

        // No answer from the user: the files are assumed to be one account, and that assumption is reported.
        var assumed = await app.ImportAsync(files);
        Assert.Equal(HttpStatusCode.OK, assumed.Status);
        Assert.Equal(250m, assumed.Field("dochod")); // FIFO took the 100 PLN lot of the first file
        Assert.Contains(assumed.Strings(assumed.Pit38.GetProperty("dataWarnings")), w => w.StartsWith("Account check:"));
        Assert.All(assumed.Body.GetProperty("files").EnumerateArray(), f =>
        {
            Assert.False(f.GetProperty("accountKnown").GetBoolean());
            Assert.False(f.GetProperty("accountConfirmed").GetBoolean());
        });

        // Marked as two accounts: each keeps its own lots.
        var separate = await app.ImportAsync(files, accountLabelsJson: """{"first.csv":"1","second.csv":"2"}""");
        Assert.Equal(HttpStatusCode.OK, separate.Status);
        Assert.Equal(50m, separate.Field("dochod"));
        Assert.DoesNotContain(separate.Strings(separate.Summary.GetProperty("warnings")), w => w.StartsWith("Account check:"));
        Assert.All(separate.Body.GetProperty("files").EnumerateArray(), f => Assert.True(f.GetProperty("accountConfirmed").GetBoolean()));

        // Marked as one account: same result as the assumption, but confirmed, so no warning.
        var same = await app.ImportAsync(files, accountLabelsJson: """{"first.csv":"1","second.csv":"1"}""");
        Assert.Equal(250m, same.Field("dochod"));
        Assert.DoesNotContain(same.Strings(same.Summary.GetProperty("warnings")), w => w.StartsWith("Account check:"));
    }

    [Fact]
    public async Task Import_OverlappingExportsAreMatchedByTheBrokersOperationId()
    {
        var shortExport = new Trading212Export()
            .Buy("B1", "2025-02-03 10:00:00", "AAPL", Aapl, 1m, 75m)
            .Dividend("D1", "2025-02-20 10:00:00", "AAPL", Aapl, 1m, 25.75m, withholding: 3.775m);
        // A longer export of the same account: the same operations plus a later sale.
        var longExport = new Trading212Export()
            .Buy("B1", "2025-02-03 10:00:00", "AAPL", Aapl, 1m, 75m)
            .Dividend("D1", "2025-02-20 10:00:00", "AAPL", Aapl, 1m, 25.75m, withholding: 3.775m)
            .Sell("B2", "2025-03-03 10:00:00", "AAPL", Aapl, 1m, 87.5m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync(
            [("jan-feb.csv", shortExport.Build()), ("jan-mar.csv", longExport.Build())],
            accountLabelsJson: """{"jan-feb.csv":"main","jan-mar.csv":"main"}""");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(300m, result.Field("inneKoszty"));  // one purchase, not two
        Assert.Equal(350m, result.Field("innePrzychody"));
        // The dividend and the tax withheld in the same row are counted once and stay together.
        Assert.Equal(103m, result.Summary.GetProperty("totalDividendsPln").GetDecimal());
        Assert.Equal(15.10m, result.Summary.GetProperty("totalCreditableWithholdingPln").GetDecimal());
        Assert.Equal(19.57m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(15.10m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(4m, result.Field("roznica"));
    }

    // ── 3. PIT-38 is the single source of the amount to pay ──

    [Fact]
    public async Task Import_LossDeductionEqualToTheGainLeavesNothingToPayAnywhere()
    {
        var statement = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-01-10, 10:00:00", 100m, 25m)    // cost 10 000 PLN
            .Sell("AAPL", "2025-06-10, 10:00:00", 100m, 50m);  // proceeds 20 000 PLN

        using var app = new PitlyApp();
        var result = await app.ImportAsync(
            [("2025.csv", statement.Build())],
            priorYearLossesJson: """[{"year":2023,"lossPln":10000,"deductedPln":0}]""");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(10_000m, result.Field("dochod"));
        Assert.Equal(10_000m, result.Field("stratyZLatUbieglych"));
        Assert.Equal(0m, result.Field("podstawaObliczenia"));
        Assert.Equal(0m, result.Field("podatekNalezny"));
        Assert.Equal(0m, result.Field("podatekDoZaplaty"));
        // The summary the dashboard reads carries the same figures, not a tax computed before the deduction.
        Assert.Equal(0m, result.Summary.GetProperty("capitalGainTaxPln").GetDecimal());
        Assert.Equal(0m, result.Summary.GetProperty("dividendTaxOwedPln").GetDecimal());
        Assert.Equal(10_000m, result.Summary.GetProperty("priorYearLossPln").GetDecimal());

        // Reopening the session gives the same answer.
        var sessionId = result.Body.GetProperty("sessionId").GetString();
        var summary = await app.GetAsync($"/api/session/{sessionId}/summary");
        var pit38 = await app.GetAsync($"/api/session/{sessionId}/pit38");
        Assert.Equal(0m, summary.Body.GetProperty("capitalGainTaxPln").GetDecimal());
        Assert.Equal(0m, pit38.Body.GetProperty("podatekDoZaplaty").GetDecimal());
        Assert.Equal(10_000m, pit38.Body.GetProperty("stratyZLatUbieglych").GetDecimal());
    }

    // ── 4. Rounding order of the flat tax on dividends ──

    [Fact]
    public async Task Import_DividendTaxIsRoundedOnlyInTheDifference()
    {
        var statement = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 25.75m)      // 103 PLN
            .Withholding("AAPL", Aapl, "2025-05-15", 3.775m);  // 15.10 PLN, below the 15% treaty cap of 15.45 PLN

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", statement.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(19.57m, result.Field("zryczaltowanyPodatek"));
        Assert.Equal(15.10m, result.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(4m, result.Field("roznica"));
        Assert.Equal(4m, result.Field("podatekDoZaplaty"));
        var form = result.Pit38.GetProperty("form");
        Assert.Equal((47, 48, 49), (form.GetProperty("zryczaltowany").GetInt32(), form.GetProperty("zaplacony").GetInt32(), form.GetProperty("roznica").GetInt32()));
    }

    // ── 5. Nothing after the last day of the selected year takes part ──

    [Fact]
    public async Task Import_LaterSaleWithoutHistoryDoesNotBlockTheEarlierYear()
    {
        // One export covering two years: 2024 is complete, the 2025 sale has no purchase anywhere in the upload.
        var statement = new IbStatement("U1111111", "January 1, 2024 - December 31, 2025")
            .Instrument("MSFT", Msft).Instrument("AAPL", Aapl)
            .Buy("MSFT", "2024-02-01, 10:00:00", 1m, 25m)
            .Sell("MSFT", "2024-06-03, 10:00:00", 1m, 30m)
            .Sell("AAPL", "2025-03-03, 10:00:00", 1m, 50m);
        var files = new[] { ("2024-2025.csv", statement.Build()) };

        using var app = new PitlyApp();

        var year2024 = await app.ImportAsync(files, taxYear: 2024);
        Assert.Equal(HttpStatusCode.OK, year2024.Status);
        Assert.Equal(2024, year2024.Pit38.GetProperty("year").GetInt32());
        Assert.Equal(120m, year2024.Field("innePrzychody"));
        Assert.Equal(100m, year2024.Field("inneKoszty"));
        Assert.Equal(20m, year2024.Field("dochod"));
        Assert.Equal([2024, 2025], year2024.Body.GetProperty("availableYears").EnumerateArray().Select(y => y.GetInt32()));

        // The latest year is the default; it cannot be calculated and says why, and still lists the years to pick from.
        var year2025 = await app.ImportAsync(files);
        Assert.Equal(HttpStatusCode.BadRequest, year2025.Status);
        Assert.Contains("Cannot sell 1 shares of AAPL on 2025-03-03", year2025.Error);
        Assert.Contains("Upload earlier statements", year2025.Error);
        Assert.Equal(2025, year2025.Body.GetProperty("taxYear").GetInt32());
        Assert.Equal([2024, 2025], year2025.Body.GetProperty("availableYears").EnumerateArray().Select(y => y.GetInt32()));
    }

    // ── 7. Field numbers follow the verified form version of the tax year ──

    [Theory]
    [InlineData(2024, 2022, 17, 28, 26)]
    [InlineData(2025, 2023, 18, 30, 28)]
    public async Task Import_PriorYearLossesAreNumberedByTheFormOfTheTaxYear(int year, int lossYear, int version, int stratyField, int dochodField)
    {
        var statement = new IbStatement("U1111111", year).Instrument("AAPL", Aapl)
            .Buy("AAPL", $"{year}-01-10, 10:00:00", 10m, 25m)
            .Sell("AAPL", $"{year}-06-10, 10:00:00", 10m, 50m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync(
            [("statement.csv", statement.Build())],
            priorYearLossesJson: $$"""[{"year":{{lossYear}},"lossPln":400,"deductedPln":0}]""");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(400m, result.Field("stratyZLatUbieglych"));
        var form = result.Pit38.GetProperty("form");
        Assert.Equal(version, form.GetProperty("version").GetInt32());
        Assert.Equal(stratyField, form.GetProperty("straty").GetInt32());
        Assert.Equal(dochodField, form.GetProperty("dochod").GetInt32());
    }

    [Fact]
    public async Task Import_YearWithoutAVerifiedFormStillGetsItsAmounts()
    {
        var statement = new IbStatement("U1111111", 2026).Dividend("AAPL", Aapl, "2026-05-15", 25m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2026.csv", statement.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(JsonValueKind.Null, result.Pit38.GetProperty("form").ValueKind);
        Assert.Equal(19m, result.Field("podatekDoZaplaty"));
    }

    // ── 8. Warnings that put the result in doubt travel with PIT-38 ──

    [Fact]
    public async Task Import_OpeningPositionMismatchIsShownWithPit38AlsoAfterReopeningTheSession()
    {
        // The 2025 statement opens with 5 AAPL, but no earlier statement that bought them is uploaded.
        var statement = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl).Instrument("MSFT", Msft)
            .OpeningPosition("AAPL", 5m)
            .Buy("MSFT", "2025-02-01, 10:00:00", 1m, 25m)
            .Sell("MSFT", "2025-06-03, 10:00:00", 1m, 30m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", statement.Build())]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var warning = Assert.Single(result.Strings(result.Pit38.GetProperty("dataWarnings")));
        Assert.StartsWith("Position check for 2025", warning);
        Assert.Contains("AAPL", warning);

        var sessionId = result.Body.GetProperty("sessionId").GetString();
        var reopened = await app.GetAsync($"/api/session/{sessionId}/pit38");
        Assert.Equal(HttpStatusCode.OK, reopened.Status);
        Assert.Equal(warning, Assert.Single(reopened.Strings(reopened.Body.GetProperty("dataWarnings"))));
        Assert.False(reopened.Body.GetProperty("needsReimport").GetBoolean());
    }
}
