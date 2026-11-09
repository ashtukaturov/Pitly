namespace Pitly.Core.Models;

public static class TaxConstants
{
    /// <summary>
    /// Polish flat tax rate for capital gains and dividends (art. 30b ust. 1 ustawy o PIT).
    /// </summary>
    public const decimal TaxRate = 0.19m;

    /// <summary>
    /// Solidarity levy (danina solidarnosciowa, art. 30h ustawy o PIT): 4% of the sum of qualifying incomes
    /// (including art. 30b capital gains, after prior-year losses) above 1 000 000 PLN. Declared on DSF-1.
    /// </summary>
    public const decimal SolidarityLevyRate = 0.04m;
    public const decimal SolidarityLevyThresholdPln = 1_000_000m;
}
