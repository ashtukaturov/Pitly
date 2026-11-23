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
    public decimal TotalInterestPln { get; set; }
    public decimal TotalInterestCreditableWithholdingPln { get; set; }
    // Warnings that put the result itself in doubt (JSON array of strings); null for sessions saved before it existed
    public string? ResultWarningsJson { get; set; }
    // Version of the stored data, see DbSchemaUpgrader.CurrentDataVersion
    public int DataVersion { get; set; }
    // Saved by an older version without the data needed to rebuild the aggregates exactly
    public bool NeedsReimport { get; set; }
    // Structured findings (JSON array of TaxDiagnostic); null for sessions saved before they existed
    public string? DiagnosticsJson { get; set; }
    // The uploaded files: name, SHA-256, broker, account, statement year, parser (JSON array of SourceFile)
    public string? SourceFilesJson { get; set; }
    // Versions of the rules and of the form layout the amounts were computed with
    public string? RulesVersion { get; set; }
    public int? FormVersion { get; set; }
    // How the foreign tax credit was limited (CreditMethod name)
    public string CreditMethod { get; set; } = "TreatyCap";

    public List<TradeResultEntity> TradeResults { get; set; } = [];
    public List<DividendEntity> Dividends { get; set; } = [];
    public List<WithholdingEventEntity> WithholdingEvents { get; set; } = [];
}
