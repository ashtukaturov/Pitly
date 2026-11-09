using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pitly.Api.Data;

public static class DbSchemaUpgrader
{
    public static void EnsureLatestSchema(AppDbContext db)
    {
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN TaxableFrom TEXT NULL;");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN TotalCreditableWithholdingPln TEXT NOT NULL DEFAULT '0.0';");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN PriorYearLossPln TEXT NOT NULL DEFAULT '0.0';");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN PriorYearLossesJson TEXT NULL;");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN WarningsJson TEXT NULL;");
        TryExecute(db, "ALTER TABLE TradeResults ADD COLUMN Note TEXT NULL;");
        TryExecute(db, "ALTER TABLE TradeResults ADD COLUMN Isin TEXT NULL;");
        TryExecute(db, "ALTER TABLE Dividends ADD COLUMN Isin TEXT NULL;");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN CountriesJson TEXT NULL;");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN TotalInterestPln TEXT NOT NULL DEFAULT '0.0';");
        TryExecute(db, "ALTER TABLE Sessions ADD COLUMN TotalInterestCreditableWithholdingPln TEXT NOT NULL DEFAULT '0.0';");
        // Sessions saved before the treaty cap existed have no creditable amount; approximate it with the statutory
        // 19% cap so that reopening them does not silently drop the foreign tax credit.
        TryExecute(db, "UPDATE Sessions SET TotalCreditableWithholdingPln = CAST(MIN(CAST(TotalWithholdingPln AS REAL), ROUND(CAST(TotalDividendsPln AS REAL) * 0.19, 2)) AS TEXT) WHERE CAST(TotalCreditableWithholdingPln AS REAL) = 0 AND CAST(TotalWithholdingPln AS REAL) > 0;");
        TryExecute(db, "ALTER TABLE Dividends ADD COLUMN Kind TEXT NOT NULL DEFAULT 'Dividend';");
        TryExecute(db, "ALTER TABLE Dividends ADD COLUMN CreditableWithholdingPln TEXT NOT NULL DEFAULT '0.0';");
        TryExecute(db, "ALTER TABLE Dividends ADD COLUMN MaxCreditableRate TEXT NULL;");
    }

    private static void TryExecute(AppDbContext db, string sql)
    {
        try
        {
            db.Database.ExecuteSqlRaw(sql);
        }
        catch (SqliteException ex) when (
            ex.SqliteErrorCode == 1 &&
            ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
    }
}
