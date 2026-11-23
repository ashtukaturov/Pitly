using System.Globalization;
using Pitly.Core.Models;

namespace Pitly.Core.Parsing;

/// <summary>
/// Combines several parsed statements into one timeline. Files may overlap (a partial-year export next to the
/// full year, the same period exported twice, an Activity Statement next to a Flex Query of the same account), so
/// rows that an earlier file of the same account already contributed are skipped and reported instead of being
/// counted twice. Rows are recognised by the broker's operation id when both rows carry one, and by their content
/// (instrument, date, amount) when at least one of them does not, so an event is skipped whichever format it
/// arrives in a second time. Only duplicates across files are removed: identical rows inside one file (two fills
/// of the same order in the same second) are kept.
/// Every row leaves the merger stamped with the account it belongs to and the file it came from, so FIFO queues,
/// corporate actions and withholding matching stay inside one account and every finding can name its source.
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
        // case of yearly exports). The assumption is reported so that a second account can be marked. The file name
        // is never used as evidence of the account.
        return new AccountResolution(broker + " (no account number)", FromStatement: false, FromUser: false);
    }

    public static ParsedStatement Merge(IReadOnlyList<NamedStatement> statements, int targetYear, List<string> warnings)
        => Merge(statements, targetYear, warnings, null);

    public static ParsedStatement Merge(IReadOnlyList<NamedStatement> statements, int targetYear, List<string> warnings, List<TaxDiagnostic>? diagnostics)
    {
        diagnostics ??= [];

        // Fuller files first so that partial exports are the ones whose rows get skipped. The merged rows do not
        // depend on the order of upload (a duplicate is a duplicate whichever file names it first); only the file
        // named in the overlap message does.
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

        var seenTrades = new Seen();
        var seenDividends = new Seen();
        var seenWithholding = new Seen();
        var seenActions = new Seen();
        var seenCarryIns = new Seen();

        foreach (var file in ordered)
        {
            var name = file.Name;
            var statement = file.Statement;
            // The same trade or dividend on the same day can legitimately occur on two accounts; only rows of one
            // account are compared with each other.
            var acct = ResolveAccount(file).Account;

            var skippedTrades = TakeNew(
                statement.Trades.Select(t => t with { AccountId = acct }), trades, seenTrades,
                t => Key(acct, "row", t.Symbol.ToUpperInvariant(), t.Isin, t.Currency, Stamp(t.DateTime), Num(t.Quantity), Num(t.Price), Num(t.Proceeds), Num(t.Commission), t.Type.ToString()),
                t => IdKey(acct, t.TransactionId));
            var skippedDividends = TakeNew(
                statement.Dividends.Select(d => d with { AccountId = acct, SourceFile = d.SourceFile ?? name }), dividends, seenDividends,
                d => Key(acct, "row", d.Symbol.ToUpperInvariant(), d.Isin, d.Currency, Stamp(d.Date), Num(d.Amount), d.Kind.ToString(), Num(d.WithheldAmount ?? 0m), d.WithheldCurrency),
                d => IdKey(acct, d.TransactionId));
            var skippedWithholding = TakeNew(
                statement.WithholdingTaxes.Select(w => w with { AccountId = acct, SourceFile = w.SourceFile ?? name }), withholding, seenWithholding,
                w => Key(acct, "row", w.Symbol.ToUpperInvariant(), w.Isin, w.Currency, Stamp(w.Date), Num(w.Amount), w.Kind.ToString()),
                w => IdKey(acct, w.TransactionId));
            var skippedActions = TakeNew(
                (statement.CorporateActions ?? []).Select(a => a with { AccountId = acct }), actions, seenActions,
                a => Key(acct, "row", Stamp(a.DateTime.Date), a.Type.ToString(), a.Symbol.ToUpperInvariant(), a.Isin, a.SourceSymbol, a.SourceIsin, Num(a.Numerator), Num(a.Denominator), Num(a.Quantity), Num(a.CashProceeds)),
                a => IdKey(acct, a.ActionId),
                allowRepeatsWithinFile: false);
            // One opening position per instrument, account and year; overlapping exports of the same year repeat it.
            TakeNew(
                (statement.CarryInPositions ?? []).Select(p => p with { AccountId = acct }), carryIns, seenCarryIns,
                p => Key(acct, p.Symbol.ToUpperInvariant(), p.Isin, p.Year.ToString(CultureInfo.InvariantCulture)),
                _ => null,
                allowRepeatsWithinFile: false);

            statementWarnings.AddRange(statement.Warnings ?? []);
            diagnostics.AddRange((statement.Diagnostics ?? []).Select(d => d with { Source = d.Source ?? name, AccountId = d.AccountId ?? acct }));

            var skippedTotal = skippedTrades + skippedDividends + skippedWithholding + skippedActions;
            if (skippedTotal == 0)
                continue;

            var rowsTotal = statement.Trades.Count + statement.Dividends.Count + statement.WithholdingTaxes.Count + (statement.CorporateActions?.Count ?? 0);
            var parts = new List<string>();
            if (skippedTrades > 0) parts.Add(Plural(skippedTrades, "trade"));
            if (skippedDividends > 0) parts.Add(Plural(skippedDividends, "dividend/interest row"));
            if (skippedWithholding > 0) parts.Add(Plural(skippedWithholding, "withholding-tax row"));
            if (skippedActions > 0) parts.Add(Plural(skippedActions, "corporate action"));

            var message = skippedTotal == rowsTotal
                ? "'" + name + "' only repeats data already present in the other uploaded files (" + string.Join(", ", parts) + "); nothing new was added from it."
                : "'" + name + "' overlaps with the other uploaded files: " + string.Join(", ", parts) + " already present were skipped so nothing is counted twice.";
            warnings.Add(message);
            diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.OverlapSkipped, DiagnosticSeverity.Info, message, name, acct));
        }

        AddUnconfirmedAccountWarnings(statements, warnings, diagnostics);

        return new ParsedStatement(
            Trades: trades.OrderBy(t => t.DateTime).ToList(),
            Dividends: dividends.OrderBy(d => d.Date).ToList(),
            WithholdingTaxes: withholding.OrderBy(t => t.Date).ToList(),
            CorporateActions: actions.OrderBy(a => a.DateTime).ToList(),
            CarryInPositions: carryIns,
            StatementYear: targetYear,
            Warnings: statementWarnings.Distinct().Concat(warnings).Distinct().ToList(),
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// Several files of one broker without an account number and without an answer from the user were treated as
    /// one account. That is an assumption, not a fact, so it is reported.
    /// </summary>
    private static void AddUnconfirmedAccountWarnings(IReadOnlyList<NamedStatement> statements, List<string> warnings, List<TaxDiagnostic> diagnostics)
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

            var message =
                AccountCheckPrefix + string.Join(", ", names) + " carry no account number and were treated as statements of one account. " +
                "If they come from different accounts, mark them as separate accounts and recalculate: otherwise identical rows are removed as duplicates and FIFO lots are shared between the accounts.";
            warnings.Add(message);
            diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.AccountAssumed, DiagnosticSeverity.Warning, message, AccountId: group.Key, AffectsAmounts: true));
        }
    }

    public const string AccountCheckPrefix = "Account check: ";

    /// <summary>What earlier files contributed, per account.</summary>
    private sealed class Seen
    {
        // Operation ids already taken (account + id)
        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);
        // Per content key: the most rows one earlier file contributed without an id, and with an id
        public Dictionary<string, int> RowsWithoutId { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> RowsWithId { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Appends the rows of one file that earlier files have not contributed yet. Two rows that both carry an
    /// operation id are the same row only when the ids are equal. A row without an id is the same as an earlier row
    /// with the same content key, whether or not that earlier row had an id. A content key seen N times in earlier
    /// files and M times in this file adds max(0, M - N) rows, so genuine same-second duplicates within a file survive.
    /// Returns how many rows were skipped.
    /// </summary>
    private static int TakeNew<T>(
        IEnumerable<T> rows,
        List<T> target,
        Seen seen,
        Func<T, string> contentKeyOf,
        Func<T, string?> idKeyOf,
        bool allowRepeatsWithinFile = true)
    {
        var skipped = 0;
        foreach (var group in rows.GroupBy(contentKeyOf, StringComparer.Ordinal))
        {
            seen.RowsWithoutId.TryGetValue(group.Key, out var earlierWithoutId);
            seen.RowsWithId.TryGetValue(group.Key, out var earlierWithId);
            var withIdInFile = 0;
            var withoutIdInFile = 0;
            var acceptedInGroup = 0;

            // Earlier rows of this content that may explain rows of this file: all of them for id-less rows, only
            // the id-less ones for rows that carry an id (an id is compared with ids).
            var budgetForIdLess = earlierWithoutId + earlierWithId;
            var budgetForIdRows = earlierWithoutId;

            foreach (var item in group)
            {
                var id = idKeyOf(item);
                if (id is not null)
                    withIdInFile++;
                else
                    withoutIdInFile++;

                bool duplicate;
                if (id is not null && seen.Ids.Contains(id))
                {
                    duplicate = true;
                }
                else if (!allowRepeatsWithinFile)
                {
                    duplicate = earlierWithoutId + earlierWithId > 0 || acceptedInGroup > 0;
                }
                else if (id is not null)
                {
                    duplicate = budgetForIdRows > 0;
                    if (duplicate)
                    {
                        budgetForIdRows--;
                        budgetForIdLess--;
                    }
                }
                else
                {
                    duplicate = budgetForIdLess > 0;
                    if (duplicate)
                        budgetForIdLess--;
                }

                if (id is not null)
                    seen.Ids.Add(id);

                if (duplicate)
                {
                    skipped++;
                    continue;
                }

                target.Add(item);
                acceptedInGroup++;
            }

            var limitWithoutId = allowRepeatsWithinFile ? withoutIdInFile : Math.Min(withoutIdInFile, 1);
            var limitWithId = allowRepeatsWithinFile ? withIdInFile : Math.Min(withIdInFile, 1);
            seen.RowsWithoutId[group.Key] = Math.Max(earlierWithoutId, limitWithoutId);
            seen.RowsWithId[group.Key] = Math.Max(earlierWithId, limitWithId);
        }

        return skipped;
    }

    // Unit separator: cannot occur in a ticker, ISIN or number, so joined parts never collide.
    private const char KeySeparator = (char)31;

    private static string Key(params string?[] parts) => string.Join(KeySeparator, parts.Select(p => p ?? string.Empty));

    private static string? IdKey(string account, string? id) => string.IsNullOrWhiteSpace(id) ? null : Key(account, "id", id.Trim());

    private static string Num(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Stamp(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun)
        => count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");
}
