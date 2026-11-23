namespace Pitly.Api.Data;

public class DividendEntity
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string Symbol { get; set; } = "";
    public string Currency { get; set; } = "";
    public DateTime Date { get; set; }
    public string Kind { get; set; } = "Dividend";
    public decimal AmountOriginal { get; set; }
    public decimal WithholdingTaxOriginal { get; set; }
    public decimal AmountPln { get; set; }
    public decimal WithholdingTaxPln { get; set; }
    public decimal CreditableWithholdingPln { get; set; }
    public decimal? MaxCreditableRate { get; set; }
    public decimal ExchangeRate { get; set; }
    public bool RateUnavailable { get; set; }
    public string? Isin { get; set; }
    // Source country named by the statement (generic CSV); null when the country comes from the ISIN
    public string? CountryCode { get; set; }
    // Provenance of the row: account, the broker's ids, the file it came from and how its withholding was tied to it
    public string? AccountId { get; set; }
    public string? TransactionId { get; set; }
    public string? ActionId { get; set; }
    public string? SourceFile { get; set; }
    public string? WithholdingSource { get; set; }
}
