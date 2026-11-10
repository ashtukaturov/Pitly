using System.Globalization;
using Pitly.Core.Models;

namespace Pitly.Core.Parsing;

/// <summary>
/// Combines several parsed statements into one timeline. Files may overlap (a partial-year export next to the
/// full year, the same period exported twice), so rows that an earlier file of the same account already
/// contributed are skipped and reported instead of being counted twice. Only duplicates across files are removed:
/// identical rows inside one file (two fills of the same order in the same second) are kept.
/// Every row leaves the merger stamped with the account it belongs to, so FIFO queues, corporate actions and
/// withholding matching stay inside one account.
/// </summary>
public static class StatementMerger
{
    /// <param name="AccountLabel">
    /// The user's answer for a file that carries no account number: files with the same label are one account,
    /// files with different labels are different accounts. Ignored when the statement names its account.
    /// </param>
    public sealed record NamedStatement(string Name, ParsedStatement Statement, string? AccountLabel = null);

    /// <summary>How the account of one uploaded file was established.</summary>
    /// <param name="Account">Key used to keep accounts apart (never empty).</param>
    /// <param name="FromStatement">True when the statement itself names the account.</param>
    /// <param name="FromUser">True when the user labelled a file that has no account number.</param>
    public sealed record AccountResolution(string Account, bool FromStatement, bool FromUser);

    public static AccountResolution ResolveAccount(NamedStatement file)
    {
        var statementAccount = file.Statement.AccountId?.Trim();
        if (!string.IsNullOrEmpty(statementAccount))
            return new AccountResolution(statementAccount.ToUpperInvariant(), FromStatement: true, FromUser: false);

        var broker = string.IsNullOrWhiteSpace(file.Statement.Broker) ? "Unknown broker" : file.Statement.Broker.Trim();
        var label = file.AccountLabel?.Trim();
        if (!string.IsNullOrEmpty(label))
            return new AccountResolution(broker + " / " + label, FromStatement: false, FromUser: true);

        // No number and no answer from the user: statements of one broker are assumed to be one account (the usual
        // case of yearly exports). The assumption is reported so that a second account can be marked.
        return new AccountResolution(broker + " (no account number)", FromStatement: false, FromUser: false);
    }

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

        var seenTrades = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenDividends = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenWithholding = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenActions = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenCarryIns = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in ordered)
        {
            var name = file.Name;
            var statement = file.Statement;
            // The same trade or dividend on the same day can legitimately occur on two accounts; only rows of one
            // account are compared with each other.
            var acct = ResolveAccount(file).Account;

            var skippedTrades = TakeNew(
                statement.Trades.Select(t => t with { AccountId = acct }), trades, seenTrades,
                t => string.IsNullOrWhiteSpace(t.TransactionId)
                    ? Key(acct, "row", t.Symbol.ToUpperInvariant(), t.Isin, t.Currency, Stamp(t.DateTime), Num(t.Quantity), Num(t.Price), Num(t.Proceeds), Num(t.Commission), t.Type.ToString())
                    : Key(acct, "id", t.TransactionId));
            var skippedDividends = TakeNew(
                statement.Dividends.Select(d => d with { AccountId = acct }), dividends, seenDividends,
                d => string.IsNullOrWhiteSpace(d.TransactionId)
                    ? Key(acct, "row", d.Symbol.ToUpperInvariant(), d.Isin, d.Currency, Stamp(d.Date), Num(d.Amount), d.Kind.ToString(), Num(d.WithheldAmount ?? 0m), d.WithheldCurrency)
                    : Key(acct, "id", d.TransactionId));
            var skippedWithholding = TakeNew(
                statement.WithholdingTaxes.Select(w => w with { AccountId = acct }), withholding, seenWithholding,
                w => Key(acct, w.Symbol.ToUpperInvariant(), w.Isin, w.Currency, Stamp(w.Date), Num(w.Amount), w.Kind.ToString()));
            var skippedActions = TakeNew(
                (statement.CorporateActions ?? []).Select(a => a with { AccountId = acct }), actions, seenActions,
                a => Key(acct, Stamp(a.DateTime), a.Type.ToString(), a.Symbol.ToUpperInvariant(), a.Isin, a.SourceSymbol, a.SourceIsin, Num(a.Numerator), Num(a.Denominator), Num(a.Quantity), Num(a.CashProceeds)),
                allowRepeatsWithinFile: false);
            // One opening position per instrument, account and year; overlapping exports of the same year repeat it.
            TakeNew(
                (statement.CarryInPositions ?? []).Select(p => p with { AccountId = acct }), carryIns, seenCarryIns,
                p => Key(acct, p.Symbol.ToUpperInvariant(), p.Isin, p.Year.ToString(CultureInfo.InvariantCulture)),
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
        }

        AddUnconfirmedAccountWarnings(statements, warnings);

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
    /// Several files of one broker without an account number and without an answer from the user were treated as
    /// one account. That is an assumption, not a fact, so it is reported.
    /// </summary>
    private static void AddUnconfirmedAccountWarnings(IReadOnlyList<NamedStatement> statements, List<string> warnings)
    {
        var assumed = statements
            .Select(s => (File: s, Resolution: ResolveAccount(s)))
            .Where(x => !x.Resolution.FromStatement && !x.Resolution.FromUser)
            .GroupBy(x => x.Resolution.Account, StringComparer.OrdinalIgnoreCase);

        foreach (var group in assumed)
        {
            var names = group.Select(x => "'" + x.File.Name + "'").ToList();
            if (names.Count < 2)
                continue;

            warnings.Add(
                AccountCheckPrefix + string.Join(", ", names) + " carry no account number and were treated as statements of one account. " +
                "If they come from different accounts, mark them as separate accounts and recalculate: otherwise identical rows are removed as duplicates and FIFO lots are shared between the accounts.");
        }
    }

    public const string AccountCheckPrefix = "Account check: ";

    /// <summary>
    /// Appends the rows of one file that earlier files have not contributed yet. A key seen N times in earlier files
    /// and M times in this file adds max(0, M - N) rows, so genuine same-second duplicates within a file survive.
    /// Returns how many rows were skipped.
    /// </summary>
    private static int TakeNew<T>(
        IEnumerable<T> rows,
        List<T> target,
        Dictionary<string, int> seen,
        Func<T, string> keyOf,
        bool allowRepeatsWithinFile = true)
    {
        var skipped = 0;
        foreach (var group in rows.GroupBy(keyOf, StringComparer.Ordinal))
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

    // Unit separator: cannot occur in a ticker, ISIN or number, so joined parts never collide.
    private const char KeySeparator = (char)31;

    private static string Key(params string?[] parts) => string.Join(KeySeparator, parts.Select(p => p ?? string.Empty));

    private static string Num(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Stamp(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun)
        => count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");
}
