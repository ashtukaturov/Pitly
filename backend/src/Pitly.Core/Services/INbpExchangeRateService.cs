namespace Pitly.Core.Services;

public interface INbpExchangeRateService
{
    /// <summary>The NBP mid rate of the last business day before <paramref name="transactionDate"/> (art. 11a ustawy o PIT).</summary>
    Task<decimal> GetRateAsync(string currency, DateTime transactionDate);

    /// <summary>
    /// The same rate with the day of the NBP table it comes from, for the audit trail of a row. A source that does
    /// not know the table day (a fixed table in a test or an offline cache) reports the rate alone.
    /// </summary>
    async Task<ExchangeRateQuote> GetQuoteAsync(string currency, DateTime transactionDate)
        => new(await GetRateAsync(currency, transactionDate), null);
}

/// <summary>A rate and, when known, the day of the NBP table it was published for.</summary>
public sealed record ExchangeRateQuote(decimal Rate, DateTime? EffectiveDate);
