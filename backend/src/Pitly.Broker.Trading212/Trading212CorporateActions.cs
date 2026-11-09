using Pitly.Core.Models;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Broker.Trading212;

/// <summary>
/// Trading 212 exports a stock split (or share consolidation) as two rows for the same instrument and moment:
/// "Stock split close" with the complete position before the split and "Stock split open" with the complete
/// position after it. The pair is turned into one <see cref="CorporateActionType.StockSplit"/> whose ratio is
/// open shares / close shares.
/// </summary>
public partial class Trading212StatementParser
{
    private sealed record SplitRow(string Action, string Ticker, string? Isin, DateTime DateTime, decimal Shares, int LineNumber);

    private static bool IsStockSplitAction(string normalizedAction)
        => normalizedAction.Contains("split", StringComparison.OrdinalIgnoreCase) ||
           normalizedAction.Contains("consolidation", StringComparison.OrdinalIgnoreCase);

    private SplitRow ParseSplitRow(
        List<string> fields,
        Dictionary<string, HeaderColumn> columnMap,
        string normalizedAction,
        int lineNumber)
    {
        var ticker = RequireField(fields, columnMap, "ticker", normalizedAction, lineNumber);
        var isin = GetField(fields, columnMap, "isin");
        var timeStr = RequireField(fields, columnMap, "time", normalizedAction, lineNumber);
        var sharesStr = RequireField(fields, columnMap, "no. of shares", normalizedAction, lineNumber);

        if (!TryParseDateTime(timeStr, out var dateTime))
            throw new FormatException($"Could not parse date '{timeStr}' for {normalizedAction} row on line {lineNumber}.");

        if (!TryParseDecimal(sharesStr, out var shares))
            throw new FormatException($"Could not parse share quantity '{sharesStr}' for {normalizedAction} row on line {lineNumber}.");

        return new SplitRow(normalizedAction, ticker, string.IsNullOrWhiteSpace(isin) ? null : isin, dateTime, shares, lineNumber);
    }

    private static List<CorporateAction> BuildStockSplits(List<SplitRow> rows, List<string> warnings)
    {
        var actions = new List<CorporateAction>();
        if (rows.Count == 0)
            return actions;

        foreach (var group in rows
                     .GroupBy(r => (Key: (r.Isin ?? r.Ticker).ToUpperInvariant(), r.DateTime.Date))
                     .OrderBy(g => g.Key.Date))
        {
            var closes = group.Where(r => r.Action.Contains("close", StringComparison.OrdinalIgnoreCase)).ToList();
            var opens = group.Where(r => r.Action.Contains("open", StringComparison.OrdinalIgnoreCase)).ToList();
            var first = group.First();

            if (closes.Count == 0 || opens.Count == 0)
            {
                throw new FormatException(
                    $"Trading 212 stock split for {first.Ticker} on {first.DateTime:yyyy-MM-dd} (line {first.LineNumber}) is incomplete: " +
                    "Pitly needs both the 'Stock split close' row (position before) and the 'Stock split open' row (position after) " +
                    "to work out the ratio. Re-export the full period that contains both rows, or upload the file that has the other half.");
            }

            var before = closes.Sum(r => r.Shares);
            var after = opens.Sum(r => r.Shares);
            if (before <= 0 || after <= 0)
            {
                throw new FormatException(
                    $"Trading 212 stock split for {first.Ticker} on {first.DateTime:yyyy-MM-dd} has non-positive share counts ({before} before, {after} after).");
            }

            var open = opens.OrderBy(r => r.DateTime).First();
            actions.Add(new CorporateAction(
                Symbol: open.Ticker,
                DateTime: open.DateTime,
                Type: CorporateActionType.StockSplit,
                Numerator: after,
                Denominator: before,
                Isin: open.Isin,
                Description: $"Trading 212 stock split {before} -> {after} shares"));

            var ratio = after / before;
            if (ratio != Math.Round(ratio, 4))
            {
                warnings.Add(
                    $"Stock split for {open.Ticker} on {open.DateTime:yyyy-MM-dd}: the ratio {after}/{before} derived from the position sizes is not a round number " +
                    "(fractional shares or a cash-in-lieu payout may be involved). Check the resulting lots.");
            }
        }

        return actions;
    }
}
