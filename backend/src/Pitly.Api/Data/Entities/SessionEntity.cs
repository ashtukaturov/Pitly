namespace Pitly.Api.Data;

public class SessionEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Year { get; set; }
    public DateTime? TaxableFrom { get; set; }
    public decimal TotalProceedsPln { get; set; }
    public decimal TotalCostPln { get; set; }
    public decimal CapitalGainPln { get; set; }
    public decimal CapitalGainTaxPln { get; set; }
    public decimal TotalDividendsPln { get; set; }
    public decimal TotalWithholdingPln { get; set; }
    public decimal TotalCreditableWithholdingPln { get; set; }
    public decimal DividendTaxOwedPln { get; set; }
    // Amount actually deducted in poz. 30
    public decimal PriorYearLossPln { get; set; }
    // The user's per-year loss entries (JSON array of PriorYearLoss), so PIT-38 can be recomputed with warnings
    public string? PriorYearLossesJson { get; set; }
    // Parser warnings shown on the dashboard (JSON array of strings)
    public string? WarningsJson { get; set; }
    // Income by country for PIT/ZG (JSON array of CountryIncome)
    public string? CountriesJson { get; set; }

    public List<TradeResultEntity> TradeResults { get; set; } = [];
    public List<DividendEntity> Dividends { get; set; } = [];
}
