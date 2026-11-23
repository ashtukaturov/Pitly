using System.Text.Json;
using Pitly.Api.Data;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Api.Mapping;

public static class EntityMapper
{
    public static SessionEntity ToSessionEntity(TaxSummary summary)
    {
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
            PriorYearLossPln = Pit38Fields.FromSummary(summary).StratyZLatUbieglych,
            PriorYearLossesJson = PriorYearLossJson.Serialize(summary.PriorYearLosses),
            WarningsJson = summary.Warnings is { Count: > 0 } ? JsonSerializer.Serialize(summary.Warnings) : null,
            ResultWarningsJson = JsonSerializer.Serialize(summary.ResultWarnings ?? []),
            CountriesJson = summary.Countries is { Count: > 0 } ? JsonSerializer.Serialize(summary.Countries) : null,
            TotalInterestPln = summary.TotalInterestPln,
            TotalInterestCreditableWithholdingPln = summary.TotalInterestCreditableWithholdingPln,
            DataVersion = DbSchemaUpgrader.CurrentDataVersion,
            NeedsReimport = false,
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
                CountryCode = t.CountryCode
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
                CountryCode = d.CountryCode
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
        return new TaxSummary(
            session.TotalProceedsPln, session.TotalCostPln,
            session.CapitalGainPln, session.CapitalGainTaxPln,
            session.TotalDividendsPln, session.TotalWithholdingPln,
            session.TotalCreditableWithholdingPln,
            session.DividendTaxOwedPln, session.Year,
            taxPeriod.TaxableFrom, taxPeriod.TaxableTo,
            [], [],
            PriorYearLossJson.Deserialize(session.PriorYearLossesJson),
            Warnings: warnings,
            Countries: DeserializeCountries(session.CountriesJson),
            TotalInterestPln: session.TotalInterestPln,
            TotalInterestCreditableWithholdingPln: session.TotalInterestCreditableWithholdingPln,
            // Sessions saved before result warnings were stored separately: the opening-position check is
            // recognised by its fixed prefix.
            ResultWarnings: session.ResultWarningsJson is null
                ? warnings.Where(w => w.StartsWith(CapitalGainsTaxCalculator.PositionCheckPrefix, StringComparison.Ordinal)).ToList()
                : DeserializeWarnings(session.ResultWarningsJson));
    }

    /// <summary>PIT-38 of a stored session: the same calculation as right after the import.</summary>
    public static Pit38Fields ToPit38(SessionEntity session)
        => Pit38Fields.FromSummary(ToSummary(session)) with { NeedsReimport = session.NeedsReimport };

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
            pit38.NeedsReimport
        };
    }

    public static List<CountryIncome> DeserializeCountries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<CountryIncome>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static List<string> DeserializeWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
