using System.Net;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Tests;

/// <summary>
/// The rules that turn attributed withholding into a credit: the rate of the day the tax was paid, the treaty cap
/// against the statutory cap, and interest never borrowing a dividend treaty rate.
/// </summary>
public class CreditRulesTests
{
    private const string Aapl = "US0378331005";
    private static readonly DateTime May = new(2025, 5, 15);
    private static readonly DateTime October = new(2025, 10, 1);

    [Fact]
    public async Task TaxPaidOnAnotherDayIsConvertedAtTheRateOfThatDayAndSaidSo()
    {
        // Income on 15 May at 4.00; the tax is re-charged on 1 October, when the rate is 4.20 (art. 11a ust. 3: the
        // rate of the day before the tax was paid). A refund of the same amount in the same currency cancels the
        // original tax at the original rate, without a spurious excess from the rate difference.
        var rates = new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m, ["USD@2025-10-01"] = 4.2m });
        var calculator = new DividendTaxCalculator(rates);
        var dividends = new List<RawDividend> { new("AAPL", "USD", May, 100m, Aapl) };

        var recharged = new List<RawWithholdingTax> { new("AAPL", "USD", May, 15m, Aapl, BookingDate: October) };
        var diagnostics = new List<TaxDiagnostic>();
        var result = await calculator.CalculateAsync(dividends, recharged, TaxPeriod.FullYear(2025), CreditMethod.TreatyCap, diagnostics);
        Assert.Equal(63m, Assert.Single(result.Dividends).WithholdingTaxPln);   // 15 USD x 4.20
        Assert.Equal(60m, Assert.Single(result.Dividends).CreditableWithholdingPln); // capped at 15% of 400 PLN
        var note = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.RateDate);
        Assert.Equal(DiagnosticSeverity.Info, note.Severity);
        Assert.False(note.BlocksFiling);
        Assert.Contains("2025-10-01", note.Message);

        var refunded = new List<RawWithholdingTax>
        {
            new("AAPL", "USD", May, 15m, Aapl),
            new("AAPL", "USD", May, -15m, Aapl, BookingDate: October)
        };
        diagnostics.Clear();
        var netted = await calculator.CalculateAsync(dividends, refunded, TaxPeriod.FullYear(2025), CreditMethod.TreatyCap, diagnostics);
        Assert.Equal(0m, Assert.Single(netted.Dividends).WithholdingTaxPln);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.ExcessRefund);
    }

    [Fact]
    public async Task InterestWithAnIsinKeepsTheStatutoryCapInsteadOfADividendTreatyRate()
    {
        // Bond interest with a US ISIN and 30% withheld: the dividend treaty rate of 15% does not apply to interest,
        // so the cap is 19% of the income (art. 30a ust. 9), and the row says that the classification is unverified.
        var calculator = new DividendTaxCalculator(new TestRateService(new Dictionary<string, decimal> { ["USD"] = 4m }));
        var dividends = new List<RawDividend> { new("BND", "USD", May, 100m, "US9219378356", IncomeKind.Interest) };
        var taxes = new List<RawWithholdingTax> { new("BND", "USD", May, 30m, "US9219378356", IncomeKind.Interest) };
        var diagnostics = new List<TaxDiagnostic>();

        var result = await calculator.CalculateAsync(dividends, taxes, null, CreditMethod.TreatyCap, diagnostics);

        var row = Assert.Single(result.Dividends);
        Assert.Equal(120m, row.WithholdingTaxPln);
        Assert.Equal(76m, row.CreditableWithholdingPln);   // 19% of 400 PLN, not 15%
        Assert.Null(row.MaxCreditableRate);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.ClassificationUnverified);
    }

    [Fact]
    public async Task Import_StatutoryCapIsOnlyUsedWhenAskedAndIsRecordedWithTheSession()
    {
        // 30% withheld on a US dividend of 25 USD (100 PLN): 30 PLN withheld, 15 PLN creditable under the treaty cap,
        // 19 PLN under the literal reading of art. 30a ust. 9.
        var statement = new IbStatement("U1111111", 2025)
            .Dividend("AAPL", Aapl, "2025-05-15", 25m)
            .Withholding("AAPL", Aapl, "2025-05-15", 7.5m);

        using var app = new PitlyApp();
        var treaty = await app.ImportAsync([("2025.csv", statement.Build())]);
        Assert.Equal(HttpStatusCode.OK, treaty.Status);
        Assert.Equal(15m, treaty.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(4m, treaty.Field("roznica"));
        Assert.Equal("TreatyCap", treaty.Pit38.GetProperty("creditMethod").GetString());
        Assert.DoesNotContain("CREDIT_METHOD", treaty.Pit38.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()));

        var statutory = await app.ImportAsync([("2025.csv", statement.Build())], creditMethod: "StatutoryCap");
        Assert.Equal(HttpStatusCode.OK, statutory.Status);
        Assert.Equal(19m, statutory.Field("podatekZaplaconyZaGranica"));
        Assert.Equal(0m, statutory.Field("roznica"));
        Assert.Equal("StatutoryCap", statutory.Pit38.GetProperty("creditMethod").GetString());
        Assert.Contains("CREDIT_METHOD", statutory.Pit38.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()));
        Assert.True(statutory.Pit38.GetProperty("readyToFile").GetBoolean()); // a choice, not an unresolved amount

        // The stored session remembers the method, so reopening it does not silently switch back.
        var sessionId = statutory.Body.GetProperty("sessionId").GetString();
        var reopened = await app.GetAsync($"/api/session/{sessionId}/pit38");
        Assert.Equal("StatutoryCap", reopened.Body.GetProperty("creditMethod").GetString());
        Assert.Equal(19m, reopened.Body.GetProperty("podatekZaplaconyZaGranica").GetDecimal());

        var rejected = await app.ImportAsync([("2025.csv", statement.Build())], creditMethod: "Whatever");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
    }

    [Fact]
    public async Task Import_ReturnsTheRoundingPolicyAndItsSourcesWithTheFields()
    {
        var statement = new IbStatement("U1111111", 2025).Dividend("AAPL", Aapl, "2025-05-15", 25m);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("2025.csv", statement.Build())]);

        var policy = result.Pit38.GetProperty("roundingPolicy");
        Assert.Equal(RoundingPolicy.Current.Version, policy.GetProperty("version").GetString());
        Assert.True(policy.GetProperty("mixedRuleIsAssumption").GetBoolean());
        Assert.Contains(policy.GetProperty("sources").EnumerateArray(), s => s.GetString()!.Contains("broszura-do-pit-38-za-2025-r.pdf"));
        // Provenance of the upload travels with the session.
        var summary = result.Summary;
        var file = Assert.Single(summary.GetProperty("sourceFiles").EnumerateArray());
        Assert.Equal(("2025.csv", "ib-activity/2", "U1111111"), (file.GetProperty("name").GetString(), file.GetProperty("parserVersion").GetString(), file.GetProperty("account").GetString()));
        Assert.Equal(64, file.GetProperty("sha256").GetString()!.Length);
    }
}
