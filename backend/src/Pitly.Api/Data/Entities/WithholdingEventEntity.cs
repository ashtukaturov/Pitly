namespace Pitly.Api.Data;

/// <summary>
/// One withholding event of the session's year as it was read and what the calculation did with it; see
/// <see cref="Pitly.Core.Models.WithholdingEvent"/>. Amounts keep their full precision (SQLite stores decimals as text).
/// </summary>
public class WithholdingEventEntity
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string? AccountId { get; set; }
    public string Symbol { get; set; } = "";
    public string? Isin { get; set; }
    public string Currency { get; set; } = "";
    public string Kind { get; set; } = "Dividend";
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public DateTime? BookingDate { get; set; }
    public int? SourceStatementYear { get; set; }
    public string? TransactionId { get; set; }
    public string? ActionId { get; set; }
    public string? Reference { get; set; }
    public string? SourceFile { get; set; }
    public string? RawType { get; set; }
    public string? RawDescription { get; set; }
    public string Status { get; set; } = "Matched";
    public string? MatchedPayment { get; set; }
    public decimal AmountPln { get; set; }
    public decimal UnresolvedAmountPln { get; set; }
    public string? DiagnosticCode { get; set; }
}
