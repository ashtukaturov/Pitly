using Pitly.Core.Services;

namespace Pitly.Tests;

internal sealed class TestRateService : INbpExchangeRateService
{
    private readonly IReadOnlyDictionary<string, decimal> _rates;

    public TestRateService(IReadOnlyDictionary<string, decimal> rates)
    {
        _rates = rates;
    }

    /// <summary>The fixed rate, dated as the NBP table of the day before the operation would be.</summary>
    public async Task<ExchangeRateQuote> GetQuoteAsync(string currency, DateTime transactionDate)
        => new(await GetRateAsync(currency, transactionDate), transactionDate.Date.AddDays(-1));

    public Task<decimal> GetRateAsync(string currency, DateTime transactionDate)
    {
        var datedKey = $"{currency}@{transactionDate:yyyy-MM-dd}";
        if (_rates.TryGetValue(datedKey, out var datedRate))
            return Task.FromResult(datedRate);

        if (_rates.TryGetValue(currency, out var rate))
            return Task.FromResult(rate);

        throw new InvalidOperationException(
            $"Missing test exchange rate for {currency} on {transactionDate:yyyy-MM-dd}.");
    }
}
