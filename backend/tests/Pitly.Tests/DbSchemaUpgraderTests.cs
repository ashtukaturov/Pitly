using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitly.Api.Data;
using Pitly.Api.Mapping;

namespace Pitly.Tests;

/// <summary>
/// Upgrades temporary SQLite databases that have an earlier schema and checks what happens to the sessions
/// stored in them. The databases are created with raw SQL so that they really lack the newer columns.
/// </summary>
public sealed class DbSchemaUpgraderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "pitly-upgrade-" + Guid.NewGuid().ToString("N") + ".db");

    private static readonly Guid Interest = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZeroCredit = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Insufficient = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TradesOnly = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    private AppDbContext NewContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + _path).Options);

    private void Execute(params string[] statements)
    {
        using var connection = new SqliteConnection("Data Source=" + _path);
        connection.Open();
        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    private const string TradeResultsTable =
        """
        CREATE TABLE TradeResults (
            Id INTEGER NOT NULL CONSTRAINT PK_TradeResults PRIMARY KEY AUTOINCREMENT,
            SessionId TEXT NOT NULL, Symbol TEXT NOT NULL, DateTime TEXT NOT NULL, Type TEXT NOT NULL,
            Quantity TEXT NOT NULL, PriceOriginal TEXT NOT NULL, ProceedsOriginal TEXT NOT NULL,
            CommissionOriginal TEXT NOT NULL, Currency TEXT NOT NULL, ExchangeRate TEXT NOT NULL,
            ProceedsPln TEXT NOT NULL, CostPln TEXT NOT NULL, GainLossPln TEXT NOT NULL, RateUnavailable INTEGER NOT NULL);
        """;

    private static string Session(Guid id, string dividends, string withholding, string creditable, bool withCreditColumn = true)
        => withCreditColumn
            ? $"INSERT INTO Sessions (Id, CreatedAt, Year, TotalProceedsPln, TotalCostPln, CapitalGainPln, CapitalGainTaxPln, TotalDividendsPln, TotalWithholdingPln, TotalCreditableWithholdingPln, DividendTaxOwedPln) " +
              $"VALUES ('{id.ToString().ToUpperInvariant()}', '2026-01-01 00:00:00', 2025, '0.0', '0.0', '0.0', '0.0', '{dividends}', '{withholding}', '{creditable}', '0.0');"
            : $"INSERT INTO Sessions (Id, CreatedAt, Year, TotalProceedsPln, TotalCostPln, CapitalGainPln, CapitalGainTaxPln, TotalDividendsPln, TotalWithholdingPln, DividendTaxOwedPln) " +
              $"VALUES ('{id.ToString().ToUpperInvariant()}', '2026-01-01 00:00:00', 2025, '0.0', '0.0', '0.0', '0.0', '{dividends}', '{withholding}', '0.0');";

    /// <summary>
    /// The schema of the previous release: dividend rows already carry Kind and CreditableWithholdingPln, but the
    /// sessions have no interest aggregates, no result warnings and no data version.
    /// </summary>
    private void CreatePreviousSchema()
    {
        Execute(
            """
            CREATE TABLE Sessions (
                Id TEXT NOT NULL CONSTRAINT PK_Sessions PRIMARY KEY,
                CreatedAt TEXT NOT NULL, Year INTEGER NOT NULL, TaxableFrom TEXT NULL,
                TotalProceedsPln TEXT NOT NULL, TotalCostPln TEXT NOT NULL, CapitalGainPln TEXT NOT NULL,
                CapitalGainTaxPln TEXT NOT NULL, TotalDividendsPln TEXT NOT NULL, TotalWithholdingPln TEXT NOT NULL,
                TotalCreditableWithholdingPln TEXT NOT NULL DEFAULT '0.0', DividendTaxOwedPln TEXT NOT NULL,
                PriorYearLossPln TEXT NOT NULL DEFAULT '0.0', PriorYearLossesJson TEXT NULL, WarningsJson TEXT NULL,
                CountriesJson TEXT NULL);
            """,
            TradeResultsTable,
            """
            CREATE TABLE Dividends (
                Id INTEGER NOT NULL CONSTRAINT PK_Dividends PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL, Symbol TEXT NOT NULL, Currency TEXT NOT NULL, Date TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'Dividend', AmountOriginal TEXT NOT NULL, WithholdingTaxOriginal TEXT NOT NULL,
                AmountPln TEXT NOT NULL, WithholdingTaxPln TEXT NOT NULL,
                CreditableWithholdingPln TEXT NOT NULL DEFAULT '0.0', MaxCreditableRate TEXT NULL,
                ExchangeRate TEXT NOT NULL, RateUnavailable INTEGER NOT NULL, Isin TEXT NULL);
            """);
    }

    private static string DividendRow(Guid session, string kind, string amountPln, string withheldPln, string creditablePln, string? rate)
        => "INSERT INTO Dividends (SessionId, Symbol, Currency, Date, Kind, AmountOriginal, WithholdingTaxOriginal, AmountPln, WithholdingTaxPln, CreditableWithholdingPln, MaxCreditableRate, ExchangeRate, RateUnavailable) " +
           $"VALUES ('{session.ToString().ToUpperInvariant()}', 'X', 'PLN', '2025-05-15 00:00:00', '{kind}', '{amountPln}', '{withheldPln}', '{amountPln}', '{withheldPln}', '{creditablePln}', {(rate is null ? "NULL" : "'" + rate + "'")}, '1.0', 0);";

    [Fact]
    public void Upgrade_RestoresInterestAggregatesKeepsACorrectZeroAndFlagsWhatCannotBeRebuilt()
    {
        CreatePreviousSchema();
        Execute(
            // Interest next to a dividend: the aggregates are missing from the session but the rows have everything.
            Session(Interest, dividends: "500.0", withholding: "70.0", creditable: "70.0"),
            DividendRow(Interest, "Dividend", "400.0", "60.0", "60.0", "0.15"),
            DividendRow(Interest, "Interest", "100.0", "10.0", "10.0", null),
            // Tax was withheld but none of it is creditable (treaty rate 0): zero is the correct stored value.
            Session(ZeroCredit, dividends: "200.0", withholding: "50.0", creditable: "0.0"),
            DividendRow(ZeroCredit, "Dividend", "200.0", "50.0", "0.0", "0.0"),
            // A row saved before the credit columns existed and later given their defaults: nothing to rebuild from.
            Session(Insufficient, dividends: "300.0", withholding: "45.0", creditable: "0.0"),
            DividendRow(Insufficient, "Dividend", "300.0", "45.0", "0.0", null),
            // No dividends at all.
            Session(TradesOnly, dividends: "0.0", withholding: "0.0", creditable: "0.0"));

        using (var db = NewContext())
            DbSchemaUpgrader.EnsureLatestSchema(db);

        AssertUpgraded();

        // Running the upgrade again (every application start does) must not change anything.
        using (var db = NewContext())
            DbSchemaUpgrader.EnsureLatestSchema(db);

        AssertUpgraded();
    }

    private void AssertUpgraded()
    {
        using var db = NewContext();
        var sessions = db.Sessions.Include(s => s.Dividends).ToDictionary(s => s.Id);

        var interest = sessions[Interest];
        Assert.False(interest.NeedsReimport);
        Assert.Equal(100m, interest.TotalInterestPln);
        Assert.Equal(10m, interest.TotalInterestCreditableWithholdingPln);
        Assert.Equal(70m, interest.TotalCreditableWithholdingPln);
        // With the interest known again, PIT-38 rounds the interest part to the grosz and the dividend part to PLN.
        var pit38 = EntityMapper.ToPit38(interest);
        Assert.Equal(95m, pit38.ZryczaltowanyPodatek);          // 76 + 19
        Assert.Equal(70m, pit38.PodatekZaplaconyZaGranica);
        Assert.Equal(25m, pit38.Roznica);                       // (76 - 60) + (19 - 10)
        Assert.False(pit38.NeedsReimport);

        var zero = sessions[ZeroCredit];
        Assert.False(zero.NeedsReimport);
        Assert.Equal(0m, zero.TotalCreditableWithholdingPln);   // not replaced by "19% of the dividends"
        Assert.Equal(0m, zero.TotalInterestPln);
        Assert.Equal(38m, EntityMapper.ToPit38(zero).Roznica);  // the full 19% of 200 PLN stays payable

        var insufficient = sessions[Insufficient];
        Assert.True(insufficient.NeedsReimport);
        Assert.Equal(0m, insufficient.TotalCreditableWithholdingPln); // left as stored, not guessed
        Assert.True(EntityMapper.ToPit38(insufficient).NeedsReimport);

        Assert.False(sessions[TradesOnly].NeedsReimport);
        Assert.All(sessions.Values, s => Assert.Equal(DbSchemaUpgrader.CurrentDataVersion, s.DataVersion));
    }

    [Fact]
    public void Upgrade_FromTheOldestSchemaFlagsSessionsWhoseRowsNeverCarriedKindOrCredit()
    {
        // The original schema: no creditable amounts anywhere and no kind of income on the rows.
        Execute(
            """
            CREATE TABLE Sessions (
                Id TEXT NOT NULL CONSTRAINT PK_Sessions PRIMARY KEY,
                CreatedAt TEXT NOT NULL, Year INTEGER NOT NULL,
                TotalProceedsPln TEXT NOT NULL, TotalCostPln TEXT NOT NULL, CapitalGainPln TEXT NOT NULL,
                CapitalGainTaxPln TEXT NOT NULL, TotalDividendsPln TEXT NOT NULL, TotalWithholdingPln TEXT NOT NULL,
                DividendTaxOwedPln TEXT NOT NULL);
            """,
            TradeResultsTable,
            """
            CREATE TABLE Dividends (
                Id INTEGER NOT NULL CONSTRAINT PK_Dividends PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL, Symbol TEXT NOT NULL, Currency TEXT NOT NULL, Date TEXT NOT NULL,
                AmountOriginal TEXT NOT NULL, WithholdingTaxOriginal TEXT NOT NULL,
                AmountPln TEXT NOT NULL, WithholdingTaxPln TEXT NOT NULL,
                ExchangeRate TEXT NOT NULL, RateUnavailable INTEGER NOT NULL);
            """,
            Session(Interest, dividends: "100.0", withholding: "0.0", creditable: "", withCreditColumn: false),
            $"INSERT INTO Dividends (SessionId, Symbol, Currency, Date, AmountOriginal, WithholdingTaxOriginal, AmountPln, WithholdingTaxPln, ExchangeRate, RateUnavailable) VALUES ('{Interest.ToString().ToUpperInvariant()}', 'USD interest', 'PLN', '2025-05-15 00:00:00', '100.0', '0.0', '100.0', '0.0', '1.0', 0);",
            Session(TradesOnly, dividends: "0.0", withholding: "0.0", creditable: "", withCreditColumn: false));

        for (var run = 0; run < 2; run++)
        {
            using (var db = NewContext())
                DbSchemaUpgrader.EnsureLatestSchema(db);

            using var check = NewContext();
            var sessions = check.Sessions.ToDictionary(s => s.Id);
            // Whether the 100 PLN was interest or a dividend is unknown, and the two are rounded differently.
            Assert.True(sessions[Interest].NeedsReimport);
            Assert.False(sessions[TradesOnly].NeedsReimport);
            Assert.All(sessions.Values, s => Assert.Equal(DbSchemaUpgrader.CurrentDataVersion, s.DataVersion));
        }
    }

    [Fact]
    public void Upgrade_LeavesADatabaseCreatedByTheCurrentVersionUntouched()
    {
        using (var db = NewContext())
        {
            db.Database.EnsureCreated();
            db.Sessions.Add(new SessionEntity
            {
                Id = Interest,
                CreatedAt = DateTime.UtcNow,
                Year = 2025,
                TotalDividendsPln = 100m,
                TotalInterestPln = 100m,
                DataVersion = DbSchemaUpgrader.CurrentDataVersion,
                Dividends = [new DividendEntity { Symbol = "USD interest", Currency = "PLN", Date = new DateTime(2025, 5, 15), Kind = "Interest", AmountOriginal = 100m, AmountPln = 100m, ExchangeRate = 1m }]
            });
            db.SaveChanges();
            DbSchemaUpgrader.EnsureLatestSchema(db);
        }

        using var check = NewContext();
        var session = check.Sessions.Single();
        Assert.False(session.NeedsReimport);
        Assert.Equal(100m, session.TotalInterestPln);
    }
}
