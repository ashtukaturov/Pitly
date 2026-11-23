using System.Globalization;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Core.Parsing;

/// <summary>
/// Combines several parsed statements into one timeline. Files may overlap (a partial-year export next to the
/// full year, the same period exported twice, an Activity Statement next to a Flex Query of the same account), so
/// rows that an earlier file of the same account already contributed are skipped and reported instead of being
/// counted twice. Rows are recognised by the broker's operation id when both rows carry one, and by their content
/// (instrument, date, amount) when at least one of them does not, so an event is skipped whichever format it
/// arrives in a second time. Only duplicates across files are removed: identical rows inside one file (two fills
/// of the same order in the same second) are kept.
/// Two formats of one broker describe the same operations at different levels (an Activity Statement shows an
/// order, the Flex Query its executions), so after the row-level comparison the rows of one instrument, day and
/// side are compared as a group across formats: equal quantity, amount and fees mean the same operations and the
/// group is skipped; equal quantity with different amounts is a conflict, which is reported as blocking and never
/// doubled.
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

        // Which format family (Activity Statement, Flex Query, ...) each merged row came from, for the comparison
        // of order-level and execution-level rows across formats.
        var tradeOrigins = new Dictionary<object, Origin>(ReferenceEqualityComparer.Instance);
        var dividendOrigins = new Dictionary<object, Origin>(ReferenceEqualityComparer.Instance);
        var withholdingOrigins = new Dictionary<object, Origin>(ReferenceEqualityComparer.Instance);

        foreach (var file in ordered)
        {
            var name = file.Name;
            var statement = file.Statement;
            // The same trade or dividend on the same day can legitimately occur on two accounts; only rows of one
            // account are compared with each other.
            var acct = ResolveAccount(file).Account;
            var format = FormatFamily(statement.ParserVersion);
            var origin = new Origin(format, name, acct, statement.StatementYear,
                DateRange.Of(statement.Trades.Select(t => t.DateTime.Date)),
                DateRange.Of(statement.Dividends.Select(d => d.Date.Date)),
                DateRange.Of(statement.WithholdingTaxes.Select(w => w.Date.Date)));
            var conflicts = new List<string>();

            var fileTrades = statement.Trades.Select(t => t with { AccountId = acct }).ToList();
            var newTrades = new List<Trade>();
            var skippedTrades = TakeNew(
                fileTrades, newTrades, seenTrades,
                t => Key(acct, "row", t.Symbol.ToUpperInvariant(), t.Isin, t.Currency, Stamp(t.DateTime), Num(t.Quantity), Num(t.Price), Num(t.Proceeds), Num(t.Commission), t.Type.ToString()),
                t => IdKey(acct, t.TransactionId));
            skippedTrades += ReconcileAcrossFormats(
                fileTrades, newTrades, trades, tradeOrigins, origin,
                t => Key(acct, t.Symbol.ToUpperInvariant(), t.Currency, Stamp(t.DateTime.Date), t.Type.ToString()),
                t => new Totals(t.Quantity, t.Proceeds, t.Commission),
                t => t.DateTime.Year, t => t.DateTime.Date, o => o.Trades,
                t => $"{t.Type} {t.Symbol} on {t.DateTime:yyyy-MM-dd}", "shares", conflicts);
            foreach (var t in newTrades) { trades.Add(t); tradeOrigins[t] = origin; }

            var fileDividends = statement.Dividends.Select(d => d with { AccountId = acct, SourceFile = d.SourceFile ?? name }).ToList();
            var newDividends = new List<RawDividend>();
            var skippedDividends = TakeNew(
                fileDividends, newDividends, seenDividends,
                d => Key(acct, "row", d.Symbol.ToUpperInvariant(), d.Isin, d.Currency, Stamp(d.Date), Num(d.Amount), d.Kind.ToString(), Num(d.WithheldAmount ?? 0m), d.WithheldCurrency),
                d => IdKey(acct, d.TransactionId));
            skippedDividends += ReconcileAcrossFormats(
                fileDividends, newDividends, dividends, dividendOrigins, origin,
                d => Key(acct, d.Symbol.ToUpperInvariant(), d.Currency, Stamp(d.Date), d.Kind.ToString()),
                d => new Totals(d.Amount, d.WithheldAmount ?? 0m, 0m),
                d => (d.BookingDate ?? d.Date).Year, d => d.Date.Date, o => o.Dividends,
                d => $"{WithholdingMatcher.KindLabel(d.Kind)} {d.Symbol} on {d.Date:yyyy-MM-dd}", "units", conflicts);
            foreach (var d in newDividends) { dividends.Add(d); dividendOrigins[d] = origin; }

            var fileWithholding = statement.WithholdingTaxes.Select(w => w with { AccountId = acct, SourceFile = w.SourceFile ?? name }).ToList();
            var newWithholding = new List<RawWithholdingTax>();
            var skippedWithholding = TakeNew(
                fileWithholding, newWithholding, seenWithholding,
                w => Key(acct, "row", w.Symbol.ToUpperInvariant(), w.Isin, w.Currency, Stamp(w.Date), Num(w.Amount), w.Kind.ToString()),
                w => IdKey(acct, w.TransactionId));
            skippedWithholding += ReconcileAcrossFormats(
                fileWithholding, newWithholding, withholding, withholdingOrigins, origin,
                w => Key(acct, w.Symbol.ToUpperInvariant(), w.Currency, Stamp(w.Date), w.Kind.ToString()),
                w => new Totals(w.Amount, 0m, 0m),
                w => w.BookingYear ?? w.Date.Year, w => w.Date.Date, o => o.Withholding,
                w => $"withholding on {w.Symbol} dated {w.Date:yyyy-MM-dd}", "units", conflicts);
            foreach (var w in newWithholding) { withholding.Add(w); withholdingOrigins[w] = origin; }

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

            foreach (var conflict in conflicts)
            {
                var conflictMessage = "'" + name + "' disagrees with the other uploaded files of account " + acct + ": " + conflict +
                              " The rows of '" + name + "' were not added (adding them would count the operation twice); the earlier file's figures stand until the difference is explained.";
                warnings.Add(conflictMessage);
                diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.SourceConflict, DiagnosticSeverity.Warning, conflictMessage, name, acct, AffectsAmounts: true));
            }

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
    /// one account. That is an assumption, not a fact, so it is reported whenever it can change the amounts: when
    /// two such files both hold trades (their FIFO lots are shared) or both hold income (identical rows of two
    /// accounts would be removed as duplicates). A trades file next to an income file of one broker shares nothing.
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
            var withTrades = group.Count(x => x.File.Statement.Trades.Count > 0 || (x.File.Statement.CorporateActions?.Count ?? 0) > 0);
            var withIncome = group.Count(x => x.File.Statement.Dividends.Count > 0 || x.File.Statement.WithholdingTaxes.Count > 0);
            if (withTrades < 2 && withIncome < 2)
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

    private static string FormatFamily(string? parserVersion)
    {
        if (string.IsNullOrWhiteSpace(parserVersion))
            return "unknown";
        var slash = parserVersion.IndexOf('/');
        return slash < 0 ? parserVersion : parserVersion[..slash];
    }

    private sealed record DateRange(DateTime First, DateTime Last)
    {
        public static DateRange? Of(IEnumerable<DateTime> dates)
        {
            var list = dates.ToList();
            return list.Count == 0 ? null : new DateRange(list.Min(), list.Max());
        }

        public bool Contains(DateTime date) => date >= First && date <= Last;
    }

    /// <summary>
    /// Which file, of which format family and statement year, contributed a merged row, with the span of dates
    /// its trades, income rows and withholding rows cover (kept apart: a withholding correction dated back to an
    /// earlier year says nothing about the trades of that year).
    /// </summary>
    private sealed record Origin(string Format, string File, string Account, int? Year, DateRange? Trades, DateRange? Dividends, DateRange? Withholding)
    {
        /// <summary>
        /// True when this file should have a row of the given kind booked in <paramref name="bookingYear"/> and
        /// dated <paramref name="date"/>: the file is not older than the booking and its rows of that kind span
        /// the date.
        /// </summary>
        public bool Covers(Func<Origin, DateRange?> rangeOf, int bookingYear, DateTime date)
            => (Year is null || bookingYear <= Year) && rangeOf(this) is { } range && range.Contains(date);
    }

    /// <summary>
    /// Compares the rows of one file with what files of another format already contributed, one instrument, day
    /// and side at a time. <paramref name="fileRows"/> are all rows of the file in that scope, <paramref name="accepted"/>
    /// the ones the row-level comparison did not recognise; it is pruned in place. Two formats describe the same
    /// operations at different levels (an Activity Statement shows an order and nets the withholding rows of a
    /// day, the Flex Query lists executions and single tax rows), so rows are matched as sets: a subset of this
    /// file's rows whose quantity, amount and fees add up to what one other-format file, one other-format row, or
    /// the other format's rows booked in this file's year give for the group is the same operations and is
    /// skipped. Rows left over while another format already covers them (a file of that format for the year the
    /// row was booked in, whose rows span the row's date) are a conflict: they are skipped so that nothing is
    /// doubled, and the conflict is reported as blocking. Rows left over because the other format does not cover
    /// them yet are kept. Returns how many rows were skipped.
    /// </summary>
    private static int ReconcileAcrossFormats<T>(
        List<T> fileRows,
        List<T> accepted,
        List<T> existing,
        Dictionary<object, Origin> origins,
        Origin origin,
        Func<T, string> groupKeyOf,
        Func<T, Totals> totalsOf,
        Func<T, int> yearOf,
        Func<T, DateTime> dateOf,
        Func<Origin, DateRange?> rangeOf,
        Func<T, string> describe,
        string unit,
        List<string> conflicts) where T : class
    {
        if (accepted.Count == 0 || origin.Format == "unknown")
            return 0;

        // Only files of the same account can describe the same operations; another broker's file covers nothing here.
        var otherFiles = origins.Values.Where(o => o.Account == origin.Account && o.Format != origin.Format && o.Format != "unknown").Distinct().ToList();
        if (otherFiles.Count == 0)
            return 0;
        var others = existing
            .Select(r => (Row: r, Origin: origins.TryGetValue(r, out var o) ? o : null))
            .Where(x => x.Origin is not null && x.Origin.Account == origin.Account && x.Origin.Format != origin.Format && x.Origin.Format != "unknown")
            .GroupBy(x => groupKeyOf(x.Row), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var skipped = 0;
        foreach (var group in accepted.GroupBy(groupKeyOf, StringComparer.Ordinal).ToList())
        {
            if (!others.TryGetValue(group.Key, out var theirs))
                theirs = [];

            // What the other format says about this group, in the shapes it can take.
            var targets = new List<Totals>();
            foreach (var perFile in theirs.GroupBy(x => x.Origin!.File, StringComparer.Ordinal))
                targets.Add(Sum(perFile.Select(x => x.Row), totalsOf));
            foreach (var perFormat in theirs.GroupBy(x => x.Origin!.Format, StringComparer.Ordinal))
            {
                targets.Add(Sum(perFormat.Select(x => x.Row), totalsOf));
                var sameYear = perFormat.Where(x => origin.Year is { } y && yearOf(x.Row) == y).Select(x => x.Row).ToList();
                if (sameYear.Count > 0)
                    targets.Add(Sum(sameYear, totalsOf));
            }
            targets.AddRange(theirs.Select(x => totalsOf(x.Row)));

            var remaining = group.ToList();
            while (remaining.Count > 0 && targets.Count > 0)
            {
                var match = FindSubset(remaining, totalsOf, targets);
                if (match is null)
                    break;
                foreach (var row in match)
                {
                    accepted.Remove(row);
                    remaining.Remove(row);
                    skipped++;
                }
            }

            if (remaining.Count == 0)
                continue;

            // The other format has no explanation for these rows. When it has been uploaded for the year they were
            // booked in and its rows span their date, it should have them: that is a conflict. Otherwise the other
            // format simply does not cover them yet and they are new.
            var conflicting = remaining.Where(r => otherFiles.Any(o => o.Covers(rangeOf, yearOf(r), dateOf(r)))).ToList();
            if (conflicting.Count == 0)
                continue;

            var sample = conflicting[0];
            var ours = Sum(fileRows.Where(r => groupKeyOf(r) == group.Key), totalsOf);
            var theirTotals = Sum(theirs.Select(x => x.Row), totalsOf);
            conflicts.Add(
                $"{describe(sample)}: this file gives {Num(ours.Quantity)} {unit} for {Num(ours.Amount)} {CurrencyOf(sample)} with fees {Num(ours.Fees)}, " +
                (theirs.Count == 0
                    ? "the other format, which covers that period, has no such row (it may book the same operation under another date)."
                    : $"the other format gives {Num(theirTotals.Quantity)} for {Num(theirTotals.Amount)} {CurrencyOf(sample)} with fees {Num(theirTotals.Fees)}."));
            foreach (var row in conflicting)
            {
                accepted.Remove(row);
                skipped++;
            }
        }

        return skipped;
    }

    private readonly record struct Totals(decimal Quantity, decimal Amount, decimal Fees)
    {
        public static Totals operator +(Totals x, Totals y) => new(x.Quantity + y.Quantity, x.Amount + y.Amount, x.Fees + y.Fees);
    }

    /// <summary>
    /// A subset of <paramref name="rows"/> whose totals equal one of <paramref name="targets"/>; the whole set and
    /// single rows are tried first, then every other subset while the group is small enough to enumerate.
    /// </summary>
    private static List<T>? FindSubset<T>(List<T> rows, Func<T, Totals> totalsOf, List<Totals> targets)
    {
        var totals = rows.Select(totalsOf).ToList();
        bool Hits(Totals t) => targets.Any(target => SameTotals(t, target));

        var all = totals.Aggregate(new Totals(0m, 0m, 0m), (acc, t) => acc + t);
        if (Hits(all))
            return rows.ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            if (Hits(totals[i]))
                return [rows[i]];
        }

        if (rows.Count > 12)
            return null;
        for (var mask = 1; mask < (1 << rows.Count) - 1; mask++)
        {
            var sum = new Totals(0m, 0m, 0m);
            for (var i = 0; i < rows.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                    sum += totals[i];
            }

            if (Hits(sum))
                return rows.Where((_, i) => (mask & (1 << i)) != 0).ToList();
        }

        return null;
    }

    private static Totals Sum<T>(IEnumerable<T> rows, Func<T, Totals> totalsOf)
        => rows.Select(totalsOf).Aggregate(new Totals(0m, 0m, 0m), (acc, t) => acc + t);

    // Money is compared to the grosz/cent: two formats of one broker round the same executions differently.
    private static bool SameTotals(Totals x, Totals y)
        => x.Quantity == y.Quantity && Math.Abs(x.Amount - y.Amount) <= 0.01m && Math.Abs(x.Fees - y.Fees) <= 0.01m;

    private static string CurrencyOf<T>(T row) => row switch
    {
        Trade t => t.Currency,
        RawDividend d => d.Currency,
        RawWithholdingTax w => w.Currency,
        _ => string.Empty
    };

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
