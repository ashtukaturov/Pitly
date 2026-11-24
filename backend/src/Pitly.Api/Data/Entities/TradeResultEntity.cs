namespace Pitly.Api.Data;

public class TradeResultEntity
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string Symbol { get; set; } = "";
    public DateTime DateTime { get; set; }
    public string Type { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal PriceOriginal { get; set; }
    public decimal ProceedsOriginal { get; set; }
    public decimal CommissionOriginal { get; set; }
    public string Currency { get; set; } = "";
    public decimal ExchangeRate { get; set; }
    public decimal ProceedsPln { get; set; }
    public decimal CostPln { get; set; }
    public decimal GainLossPln { get; set; }
    public bool RateUnavailable { get; set; }
    public string? Note { get; set; }
    public string? Isin { get; set; }
    // Source country named by the statement (generic CSV); null when the country comes from the ISIN
    public string? CountryCode { get; set; }
    // Audit trail of the dates: the settlement day the statement gave and the day of the NBP table used
    public DateTime? SettleDate { get; set; }
    public DateTime? RateDate { get; set; }
}
