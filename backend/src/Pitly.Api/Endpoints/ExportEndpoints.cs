using System.Globalization;
using System.Text;
using Pitly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Pitly.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/session/{sessionId:guid}/export/csv", async (Guid sessionId, AppDbContext db) =>
        {
            var session = await db.Sessions
                .Include(s => s.TradeResults)
                .Include(s => s.Dividends)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session is null)
                return Results.NotFound(new { error = "Session not found" });

            var sb = new StringBuilder();
            sb.AppendLine("Section,Date,Symbol,Type,Quantity,Price,Proceeds (Original),Commission,Currency,Exchange Rate,Proceeds (PLN),Cost (PLN),Gain/Loss (PLN),Note,Settlement Date,NBP Rate Date");

            foreach (var t in session.TradeResults.OrderBy(t => t.DateTime))
            {
                sb.AppendLine(string.Join(",",
                    "Trade",
                    t.DateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CsvQuote(t.Symbol), CsvQuote(t.Type),
                    t.Quantity.ToString(CultureInfo.InvariantCulture),
                    t.PriceOriginal.ToString(CultureInfo.InvariantCulture),
                    t.ProceedsOriginal.ToString(CultureInfo.InvariantCulture),
                    t.CommissionOriginal.ToString(CultureInfo.InvariantCulture),
                    CsvQuote(t.Currency),
                    t.ExchangeRate.ToString("F4", CultureInfo.InvariantCulture),
                    t.ProceedsPln.ToString("F2", CultureInfo.InvariantCulture),
                    t.CostPln.ToString("F2", CultureInfo.InvariantCulture),
                    t.GainLossPln.ToString("F2", CultureInfo.InvariantCulture),
                    CsvQuote(t.Note),
                    t.SettleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    t.RateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty));
            }

            sb.AppendLine();
            sb.AppendLine("Section,Date,Symbol,Type,Amount (Original),Withholding (Original),Currency,Exchange Rate,Amount (PLN),Withholding (PLN),Creditable Withholding (PLN)");

            foreach (var d in session.Dividends.OrderBy(d => d.Date))
            {
                sb.AppendLine(string.Join(",",
                    "Dividend",
                    d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CsvQuote(d.Symbol),
                    CsvQuote(d.Kind),
                    d.AmountOriginal.ToString(CultureInfo.InvariantCulture),
                    d.WithholdingTaxOriginal.ToString(CultureInfo.InvariantCulture),
                    CsvQuote(d.Currency),
                    d.ExchangeRate.ToString("F4", CultureInfo.InvariantCulture),
                    d.AmountPln.ToString("F2", CultureInfo.InvariantCulture),
                    d.WithholdingTaxPln.ToString("F2", CultureInfo.InvariantCulture),
                    d.CreditableWithholdingPln.ToString("F2", CultureInfo.InvariantCulture)));
            }

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"pitly-tax-{session.Year}.csv");
        });
    }

    /// <summary>Quotes a text field and neutralises spreadsheet formula injection (=, +, -, @, tab).</summary>
    private static string CsvQuote(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var safe = "=+-@\t\r".Contains(value[0]) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
