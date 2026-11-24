using System.Text.Json;
using System.Text.Json.Serialization;
using Pitly.Api.Data;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Api.Mapping;

public static class EntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public static SessionEntity ToSessionEntity(TaxSummary summary)
    {
        var pit38 = Pit38Fields.FromSummary(summary);
        return new SessionEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Year = summary.Year,
            TaxableFrom = summary.TaxableFrom,
            TotalProceedsPln = summary.TotalProceedsPln,
            TotalCostPln = summary.TotalCostPln,
            CapitalGainPln = summary.CapitalGainPln,
            CapitalGainTaxPln = summary.CapitalGainTaxPln,
            TotalDividendsPln = summary.TotalDividendsPln,
            TotalWithholdingPln = summary.TotalWithholdingPln,
            TotalCreditableWithholdingPln = summary.TotalCreditableWithholdingPln,
            DividendTaxOwedPln = summary.DividendTaxOwedPln,
            PriorYearLossPln = pit38.StratyZLatUbieglych,
            PriorYearLossesJson = PriorYearLossJson.Serialize(summary.PriorYearLosses),
            WarningsJson = summary.Warnings is { Count: > 0 } ? JsonSerializer.Serialize(summary.Warnings) : null,
            ResultWarningsJson = JsonSerializer.Serialize(summary.ResultWarnings ?? []),
            CountriesJson = summary.Countries is { Count: > 0 } ? JsonSerializer.Serialize(summary.Countries) : null,
            TotalInterestPln = summary.TotalInterestPln,
            TotalInterestCreditableWithholdingPln = summary.TotalInterestCreditableWithholdingPln,
            DataVersion = DbSchemaUpgrader.CurrentDataVersion,
            NeedsReimport = false,
            DiagnosticsJson = JsonSerializer.Serialize(summary.Diagnostics ?? [], JsonOptions),
            SourceFilesJson = JsonSerializer.Serialize(summary.SourceFiles ?? [], JsonOptions),
            RulesVersion = RuleVersions.Rules + "/rounding-" + RoundingPolicy.Current.Version,
            FormVersion = pit38.Form?.Version,
            CreditMethod = summary.CreditMethod.ToString(),
            TradeResults = summary.TradeResults.Select(t => new TradeResultEntity
            {
                Symbol = t.Symbol,
                DateTime = t.DateTime,
                Type = t.Type.ToString(),
                Quantity = t.Quantity,
                PriceOriginal = t.PriceOriginal,
                ProceedsOriginal = t.ProceedsOriginal,
                CommissionOriginal = t.CommissionOriginal,
                Currency = t.Currency,
                ExchangeRate = t.ExchangeRate,
                ProceedsPln = t.ProceedsPln,
                CostPln = t.CostPln,
                GainLossPln = t.GainLossPln,
                RateUnavailable = t.RateUnavailable,
                Note = t.Note,
                Isin = t.Isin,
                CountryCode = t.CountryCode,
                SettleDate = t.SettleDate,
                RateDate = t.RateDate
            }).ToList(),
            Dividends = summary.Dividends.Select(d => new DividendEntity
            {
                Symbol = d.Symbol,
                Currency = d.Currency,
                Date = d.Date,
                Kind = d.Kind.ToString(),
                AmountOriginal = d.AmountOriginal,
                WithholdingTaxOriginal = d.WithholdingTaxOriginal,
                AmountPln = d.AmountPln,
                WithholdingTaxPln = d.WithholdingTaxPln,
                CreditableWithholdingPln = d.CreditableWithholdingPln,
                MaxCreditableRate = d.MaxCreditableRate,
                ExchangeRate = d.ExchangeRate,
                RateUnavailable = d.RateUnavailable,
                Isin = d.Isin,
                CountryCode = d.CountryCode,
                AccountId = d.AccountId,
                TransactionId = d.TransactionId,
                ActionId = d.ActionId,
                SourceFile = d.SourceFile,
                WithholdingSource = d.WithholdingSource
            }).ToList(),
            WithholdingEvents = (summary.WithholdingEvents ?? []).Select(e => new WithholdingEventEntity
            {
                AccountId = e.AccountId,
                Symbol = e.Symbol,
                Isin = e.Isin,
                Currency = e.Currency,
                Kind = e.Kind.ToString(),
                Amount = e.Amount,
                Date = e.Date,
                BookingDate = e.BookingDate,
                SourceStatementYear = e.SourceStatementYear,
                TransactionId = e.TransactionId,
                ActionId = e.ActionId,
                Reference = e.Reference,
                SourceFile = e.SourceFile,
                RawType = e.RawType,
                RawDescription = e.RawDescription,
                Status = e.Status.ToString(),
                MatchedPayment = e.MatchedPayment,
                AmountPln = e.AmountPln,
                UnresolvedAmountPln = e.UnresolvedAmountPln,
                DiagnosticCode = e.DiagnosticCode,
                ReportDate = e.ReportDate,
                SettleDate = e.SettleDate
            }).ToList()
        };
    }

    public static TaxPeriod ToTaxPeriod(SessionEntity session)
    {
        var taxableFrom = session.TaxableFrom?.Date ?? new DateTime(session.Year, 1, 1);
        return new TaxPeriod(session.Year, taxableFrom, new DateTime(session.Year, 12, 31));
    }

    /// <summary>The stored session as a summary without its rows: everything PIT-38 is computed from.</summary>
    public static TaxSummary ToSummary(SessionEntity session)
    {
        var taxPeriod = ToTaxPeriod(session);
        var warnings = DeserializeWarnings(session.WarningsJson);
        var diagnostics = Deserialize<List<TaxDiagnostic>>(session.DiagnosticsJson) ?? [];
        return new TaxSummary(
            session.TotalProceedsPln, session.TotalCostPln,
            session.CapitalGainPln, session.CapitalGainTaxPln,
            session.TotalDividendsPln, session.TotalWithholdingPln,
            session.TotalCreditableWithholdingPln,
            session.DividendTaxOwedPln, session.Year,
            taxPeriod.TaxableFrom, taxPeriod.TaxableTo,
            // The stored income rows, when the caller loaded them: section G rounds interest per payment, so the
            // rows are needed to give back exactly the fields of the import (an empty list falls back to the totals).
            [], session.Dividends.Select(ToDividend).ToList(),
            PriorYearLossJson.Deserialize(session.PriorYearLossesJson),
            Warnings: warnings,
            Countries: DeserializeCountries(session.CountriesJson),
            TotalInterestPln: session.TotalInterestPln,
            TotalInterestCreditableWithholdingPln: session.TotalInterestCreditableWithholdingPln,
            // Sessions saved before result warnings were stored separately: the opening-position check is
            // recognised by its fixed prefix.
            ResultWarnings: session.ResultWarningsJson is null
                ? warnings.Where(w => w.StartsWith(CapitalGainsTaxCalculator.PositionCheckPrefix, StringComparison.Ordinal)).ToList()
                : DeserializeWarnings(session.ResultWarningsJson),
            Diagnostics: diagnostics,
            CreditMethod: Enum.TryParse<CreditMethod>(session.CreditMethod, out var method) ? method : CreditMethod.TreatyCap,
            SourceFiles: Deserialize<List<SourceFile>>(session.SourceFilesJson));
    }

    public static Dividend ToDividend(DividendEntity d) => new(
        d.Symbol, d.Currency, d.Date,
        Enum.TryParse<IncomeKind>(d.Kind, out var kind) ? kind : IncomeKind.Dividend,
        d.AmountOriginal, d.WithholdingTaxOriginal, d.AmountPln, d.WithholdingTaxPln, d.CreditableWithholdingPln,
        d.MaxCreditableRate, d.ExchangeRate, d.RateUnavailable, d.Isin, d.CountryCode,
        d.AccountId, d.TransactionId, d.ActionId, d.SourceFile, d.WithholdingSource);

    /// <summary>
    /// PIT-38 of a stored session: the same calculation as right after the import. The session must be loaded with
    /// its dividend rows (see <see cref="ToDividend"/>), or interest with several payments rounds differently.
    /// </summary>
    public static Pit38Fields ToPit38(SessionEntity session)
    {
        var pit38 = Pit38Fields.FromSummary(ToSummary(session));
        if (!session.NeedsReimport)
            return pit38;

        var reimport = new TaxDiagnostic(DiagnosticCodes.ReimportRequired, DiagnosticSeverity.Error,
            "This session was saved by an older version of Pitly (data version " + session.DataVersion + ", rules " + (session.RulesVersion ?? "unversioned") + "). " +
            "The stored data is not enough to rebuild the amounts with the current rules; import the statements again.",
            AffectedYears: [session.Year], AffectsAmounts: true);
        return pit38 with
        {
            NeedsReimport = true,
            ReadyToFile = false,
            Diagnostics = (pit38.Diagnostics ?? []).Prepend(reimport).ToList()
        };
    }

    public static WithholdingEvent ToWithholdingEvent(WithholdingEventEntity e) => new(
        e.AccountId, e.Symbol, e.Isin, e.Currency,
        Enum.TryParse<IncomeKind>(e.Kind, out var kind) ? kind : IncomeKind.Unknown,
        e.Amount, e.Date, e.BookingDate, e.SourceStatementYear, e.TransactionId, e.ActionId, e.Reference,
        e.SourceFile, e.RawType, e.RawDescription,
        Enum.TryParse<WithholdingEventStatus>(e.Status, out var status) ? status : WithholdingEventStatus.Unresolved,
        e.MatchedPayment, e.AmountPln, e.UnresolvedAmountPln, e.DiagnosticCode, e.ReportDate, e.SettleDate);

    public static object ToSummaryResponse(SessionEntity session) => ToSummaryResponse(ToSummary(session), ToPit38(session));

    /// <summary>
    /// Summary as sent to the client. The tax amounts are taken from PIT-38 so that no screen can show a figure
    /// computed before the loss deduction or with different rounding.
    /// </summary>
    public static object ToSummaryResponse(TaxSummary summary, Pit38Fields pit38)
    {
        return new
        {
            summary.TotalProceedsPln,
            summary.TotalCostPln,
            summary.CapitalGainPln,
            CapitalGainTaxPln = pit38.PodatekNalezny,
            summary.TotalDividendsPln,
            summary.TotalWithholdingPln,
            summary.TotalCreditableWithholdingPln,
            DividendTaxOwedPln = pit38.Roznica,
            summary.Year,
            summary.TaxableFrom,
            summary.TaxableTo,
            PriorYearLossPln = pit38.StratyZLatUbieglych,
            Warnings = summary.Warnings ?? [],
            pit38.NeedsReimport,
            pit38.ReadyToFile,
            pit38.UnresolvedAmountPln,
            CreditMethod = summary.CreditMethod.ToString(),
            SourceFiles = summary.SourceFiles ?? []
        };
    }

    public static List<CountryIncome> DeserializeCountries(string? json) => Deserialize<List<CountryIncome>>(json) ?? [];

    public static List<string> DeserializeWarnings(string? json) => Deserialize<List<string>>(json) ?? [];

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
