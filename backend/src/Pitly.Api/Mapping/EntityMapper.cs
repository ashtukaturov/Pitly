using Pitly.Api.Data;
using Pitly.Core.Models;

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
            PriorYearLossPln = Pit38Fields.FromSummary(summary).Poz30StratyZLatUbieglych,
            PriorYearLossesJson = PriorYearLossJson.Serialize(summary.PriorYearLosses),
            WarningsJson = summary.Warnings is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(summary.Warnings) : null,
            CountriesJson = summary.Countries is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(summary.Countries) : null,
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
                Isin = t.Isin
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
                Isin = d.Isin
            }).ToList()
        };
    }

    public static TaxPeriod ToTaxPeriod(SessionEntity session)
    {
        var taxableFrom = session.TaxableFrom?.Date ?? new DateTime(session.Year, 1, 1);
        return new TaxPeriod(session.Year, taxableFrom, new DateTime(session.Year, 12, 31));
    }

    public static object ToSummaryResponse(SessionEntity session)
    {
        var taxPeriod = ToTaxPeriod(session);
        return new
        {
            session.TotalProceedsPln,
            session.TotalCostPln,
            session.CapitalGainPln,
            session.CapitalGainTaxPln,
            session.TotalDividendsPln,
            session.TotalWithholdingPln,
            session.TotalCreditableWithholdingPln,
            session.DividendTaxOwedPln,
            session.Year,
            taxPeriod.TaxableFrom,
            taxPeriod.TaxableTo,
            session.PriorYearLossPln,
            Warnings = DeserializeWarnings(session.WarningsJson)
        };
    }

    public static List<CountryIncome> DeserializeCountries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<CountryIncome>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
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
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
