using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Pitly.Api.Data;

/// <summary>
/// Brings a database created by an earlier version up to the current schema and repairs the aggregates of the
/// sessions stored in it. Columns are only ever added. What can be restored for an old session depends on what its
/// rows carried when they were saved, so that is established before any column is added.
/// </summary>
public static class DbSchemaUpgrader
{
    /// <summary>
    /// Version of the data stored with a session. 0: saved before versions were tracked (aggregates may be missing).
    /// 2: interest aggregates, result warnings and row-level credit are all stored by the code that saved it.
    /// </summary>
    public const int CurrentDataVersion = 2;

    private static readonly (string Table, string Column, string Definition)[] Columns =
    [
        ("Sessions", "TaxableFrom", "TEXT NULL"),
        ("Sessions", "TotalCreditableWithholdingPln", "TEXT NOT NULL DEFAULT '0.0'"),
        ("Sessions", "PriorYearLossPln", "TEXT NOT NULL DEFAULT '0.0'"),
        ("Sessions", "PriorYearLossesJson", "TEXT NULL"),
        ("Sessions", "WarningsJson", "TEXT NULL"),
        ("Sessions", "ResultWarningsJson", "TEXT NULL"),
        ("Sessions", "CountriesJson", "TEXT NULL"),
        ("Sessions", "TotalInterestPln", "TEXT NOT NULL DEFAULT '0.0'"),
        ("Sessions", "TotalInterestCreditableWithholdingPln", "TEXT NOT NULL DEFAULT '0.0'"),
        ("Sessions", "DataVersion", "INTEGER NOT NULL DEFAULT 0"),
        ("Sessions", "NeedsReimport", "INTEGER NOT NULL DEFAULT 0"),
        ("TradeResults", "Note", "TEXT NULL"),
        ("TradeResults", "Isin", "TEXT NULL"),
        ("TradeResults", "CountryCode", "TEXT NULL"),
        ("Dividends", "Isin", "TEXT NULL"),
        ("Dividends", "Kind", "TEXT NOT NULL DEFAULT 'Dividend'"),
        ("Dividends", "CreditableWithholdingPln", "TEXT NOT NULL DEFAULT '0.0'"),
        ("Dividends", "MaxCreditableRate", "TEXT NULL"),
        ("Dividends", "CountryCode", "TEXT NULL")
    ];

    public static void EnsureLatestSchema(AppDbContext db)
    {
        var existing = Columns
            .Select(c => c.Table)
            .Distinct()
            .ToDictionary(table => table, table => GetColumns(db, table));

        // Did the dividend rows carry their kind and creditable amount before this run? If the columns are only
        // being added now, every existing row gets the defaults ('Dividend', 0) and says nothing about the truth.
        var rowsCarryKindAndCredit =
            existing["Dividends"].Contains("Kind") && existing["Dividends"].Contains("CreditableWithholdingPln");

        foreach (var (table, column, definition) in Columns)
        {
            // Table, column and definition are the constants listed above, never user input.
            if (!existing[table].Contains(column))
                Execute(db, "ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition + ";");
        }

        RepairStoredSessions(db, rowsCarryKindAndCredit);
    }

    /// <summary>
    /// Restores the aggregates of sessions saved by an earlier version. Runs once per session: afterwards the
    /// session carries <see cref="CurrentDataVersion"/> and is left alone, so repeating the upgrade changes nothing.
    /// </summary>
    private static void RepairStoredSessions(AppDbContext db, bool rowsCarryKindAndCredit)
    {
        var sessions = db.Sessions
            .Include(s => s.Dividends)
            .Where(s => s.DataVersion < CurrentDataVersion)
            .ToList();

        foreach (var session in sessions)
        {
            if (session.Dividends.Count == 0 && session.TotalDividendsPln == 0m)
            {
                // No art. 30a income at all: zero interest and zero credit are the correct aggregates.
            }
            else if (CanRestoreFromRows(session, rowsCarryKindAndCredit))
            {
                // The rows are the truth; the session totals are their sums. A total of zero is a valid result
                // (no interest, or foreign tax that is not creditable) and is stored as such, not "repaired".
                var interest = session.Dividends.Where(d => d.Kind == "Interest").ToList();
                session.TotalInterestPln = Math.Round(interest.Sum(d => d.AmountPln), 2);
                session.TotalInterestCreditableWithholdingPln = Math.Round(interest.Sum(d => d.CreditableWithholdingPln), 2);
                session.TotalCreditableWithholdingPln = Math.Round(session.Dividends.Sum(d => d.CreditableWithholdingPln), 2);
            }
            else
            {
                // Kind of income or creditable tax is unknown for these rows. Guessing (for example "19% of the
                // dividends") would silently change the tax, so the session is flagged instead.
                session.NeedsReimport = true;
            }

            session.DataVersion = CurrentDataVersion;
        }

        if (sessions.Count > 0)
            db.SaveChanges();
    }

    private static bool CanRestoreFromRows(SessionEntity session, bool rowsCarryKindAndCredit)
    {
        if (!rowsCarryKindAndCredit || session.Dividends.Count == 0)
            return false;

        // The rows must be the ones the stored total was computed from.
        if (Math.Abs(Math.Round(session.Dividends.Sum(d => d.AmountPln), 2) - session.TotalDividendsPln) > 0.01m)
            return false;

        // A row saved before the credit columns existed looks like this after they were added with defaults: tax was
        // withheld on a positive payment, yet nothing is creditable and no treaty rate is recorded. Code that knows
        // the columns always stores a positive credit here, or a treaty rate of 0 that explains the zero.
        return !session.Dividends.Any(d =>
            d.WithholdingTaxPln > 0m && d.AmountPln > 0m && d.CreditableWithholdingPln == 0m && d.MaxCreditableRate is null);
    }

    private static HashSet<string> GetColumns(AppDbContext db, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WithConnection(db, command =>
        {
            command.CommandText = "PRAGMA table_info(" + table + ");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(reader.GetOrdinal("name")));
        });
        return columns;
    }

    private static void Execute(AppDbContext db, string sql)
        => WithConnection(db, command =>
        {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        });

    private static void WithConnection(AppDbContext db, Action<System.Data.Common.DbCommand> action)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
            connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            action(command);
        }
        finally
        {
            if (wasClosed)
                connection.Close();
        }
    }
}
