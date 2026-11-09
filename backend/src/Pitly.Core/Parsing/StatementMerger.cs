using System.Globalization;
using Pitly.Core.Models;

namespace Pitly.Core.Parsing;

/// <summary>
/// Combines several parsed statements into one timeline. Files may overlap (a partial-year export next to the
/// full year, the same period exported twice), so rows that an earlier file already contributed are skipped and
/// reported instead of being counted twice. Only duplicates across files are removed: identical rows inside one
/// file (two fills of the same order in the same second) are kept.
/// </summary>
public static class StatementMerger
{
    public sealed record NamedStatement(string Name, ParsedStatement Statement);

    public static ParsedStatement Merge(IReadOnlyList<NamedStatement> statements, int targetYear, List<string> warnings)
    {
        // Fuller files first so that partial exports are the ones whose rows get skipped.
        var ordered = statements
            .Select((s, index) => (Statement: s, Index: index))
            .OrderBy(x => x.Statement.Statement.StatementYear ?? int.MaxValue)
            .ThenByDescending(x => x.Statement.Statement.Trades.Count + x.Statement.Statement.Dividends.Count + x.Statement.Statement.WithholdingTaxes.Count)
            .ThenBy(x => x.Index)
            .Select(x => x.Statement)
            .ToList();

        var trades = new List<Trade>();
        var dividends = new List<RawDividend>();
        var withholding = new List<RawWithholdingTax>();
        var actions = new List<CorporateAction>();
        var carryIns = new List<CarryInPosition>();
        var statementWarnings = new List<string>();

        var seenTrades = new Dictionary<(string, string, string, string, DateTime, decimal, decimal, decimal, decimal, TradeType), int>();
        var seenDividends = new Dictionary<(string, string, string, string, DateTime, decimal, IncomeKind), int>();
        var seenWithholding = new Dictionary<(string, string, string, string, DateTime, decimal, IncomeKind), int>();
        var seenActions = new Dictionary<(string, DateTime, CorporateActionType, string, string, string, string, decimal, decimal, decimal, decimal), int>();
        var seenCarryIns = new Dictionary<(string, string, string, int), int>();

        foreach (var (name, statement) in ordered)
        {
            // The same dividend on the same day can legitimately occur on two accounts; only rows of one account dedupe.
            var acct = (statement.AccountId ?? string.Empty).ToUpperInvariant();
            var skippedTrades = TakeNew(statement.Trades, trades, seenTrades,
                t => (acct, t.Symbol.ToUpperInvariant(), t.Isin ?? string.Empty, t.Currency, t.DateTime, t.Quantity, t.Price, t.Proceeds, t.Commission, t.Type));
            var skippedDividends = TakeNew(statement.Dividends, dividends, seenDividends,
                d => (acct, d.Symbol.ToUpperInvariant(), d.Isin ?? string.Empty, d.Currency, d.Date, d.Amount, d.Kind));
            var skippedWithholding = TakeNew(statement.WithholdingTaxes, withholding, seenWithholding,
                w => (acct, w.Symbol.ToUpperInvariant(), w.Isin ?? string.Empty, w.Currency, w.Date, w.Amount, w.Kind));
            var skippedActions = TakeNew(statement.CorporateActions ?? [], actions, seenActions,
                a => (acct, a.DateTime, a.Type, a.Symbol.ToUpperInvariant(), a.Isin ?? string.Empty, a.SourceSymbol ?? string.Empty, a.SourceIsin ?? string.Empty, a.Numerator, a.Denominator, a.Quantity, a.CashProceeds),
                allowRepeatsWithinFile: false);
            // One opening position per instrument and year; overlapping exports of the same year repeat it.
            var skippedCarryIns = TakeNew(statement.CarryInPositions ?? [], carryIns, seenCarryIns,
                p => (acct, p.Symbol.ToUpperInvariant(), p.Isin ?? string.Empty, p.Year),
                allowRepeatsWithinFile: false);

            statementWarnings.AddRange(statement.Warnings ?? []);

            var skippedTotal = skippedTrades + skippedDividends + skippedWithholding + skippedActions;
            if (skippedTotal == 0)
                continue;

            var rowsTotal = statement.Trades.Count + statement.Dividends.Count + statement.WithholdingTaxes.Count + (statement.CorporateActions?.Count ?? 0);
            var parts = new List<string>();
            if (skippedTrades > 0) parts.Add(Plural(skippedTrades, "trade"));
            if (skippedDividends > 0) parts.Add(Plural(skippedDividends, "dividend/interest row"));
            if (skippedWithholding > 0) parts.Add(Plural(skippedWithholding, "withholding-tax row"));
            if (skippedActions > 0) parts.Add(Plural(skippedActions, "corporate action"));

            warnings.Add(skippedTotal == rowsTotal
                ? "'" + name + "' only repeats data already present in the other uploaded files (" + string.Join(", ", parts) + "); nothing new was added from it."
                : "'" + name + "' overlaps with the other uploaded files: " + string.Join(", ", parts) + " already present were skipped so nothing is counted twice.");
            _ = skippedCarryIns;
        }

        return new ParsedStatement(
            Trades: trades.OrderBy(t => t.DateTime).ToList(),
            Dividends: dividends.OrderBy(d => d.Date).ToList(),
            WithholdingTaxes: withholding.OrderBy(t => t.Date).ToList(),
            CorporateActions: actions.OrderBy(a => a.DateTime).ToList(),
            CarryInPositions: carryIns,
            StatementYear: targetYear,
            Warnings: statementWarnings.Distinct().Concat(warnings).Distinct().ToList());
    }

    /// <summary>
    /// Appends the rows of one file that earlier files have not contributed yet. A key seen N times in earlier files
    /// and M times in this file adds max(0, M - N) rows, so genuine same-second duplicates within a file survive.
    /// Returns how many rows were skipped.
    /// </summary>
    private static int TakeNew<T, TKey>(
        IEnumerable<T> rows,
        List<T> target,
        Dictionary<TKey, int> seen,
        Func<T, TKey> keyOf,
        bool allowRepeatsWithinFile = true)
        where TKey : notnull
    {
        var skipped = 0;
        foreach (var group in rows.GroupBy(keyOf))
        {
            var inThisFile = group.Count();
            seen.TryGetValue(group.Key, out var already);
            var limit = allowRepeatsWithinFile ? inThisFile : Math.Min(inThisFile, 1);
            var accept = Math.Max(0, limit - already);

            target.AddRange(group.Take(accept));
            skipped += inThisFile - accept;
            seen[group.Key] = Math.Max(already, limit);
        }

        return skipped;
    }

    private static string Plural(int count, string noun)
        => count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");
}
