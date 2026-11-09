using Pitly.Core.Models;
using Pitly.Core.Services;

namespace Pitly.Core.Tax;

public interface IDividendTaxCalculator
{
    Task<List<Dividend>> CalculateAsync(List<RawDividend> dividends, List<RawWithholdingTax> withholdingTaxes);
}

public class DividendTaxCalculator : IDividendTaxCalculator
{
    private readonly INbpExchangeRateService _rateService;

    public DividendTaxCalculator(INbpExchangeRateService rateService)
    {
        _rateService = rateService;
    }

    public async Task<List<Dividend>> CalculateAsync(List<RawDividend> rawDividends, List<RawWithholdingTax> rawWithholdingTaxes)
    {
        var results = new List<Dividend>();

        foreach (var div in rawDividends)
        {
            var rate = await _rateService.GetRateAsync(div.Currency, div.Date);
            var amountPln = div.Amount * rate;
            var matchingTaxes = rawWithholdingTaxes
                .Where(t => IsMatch(div, t))
                .ToList();

            var withholdingAmount = matchingTaxes.Sum(t => t.Amount);
            var withholdingCurrencies = matchingTaxes
                .Select(t => t.Currency)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (withholdingCurrencies.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Dividend withholding tax for {div.Symbol} on {div.Date:yyyy-MM-dd} uses multiple currencies. " +
                    "This Trading 212 export is not supported safely.");
            }

            decimal withholdingPln = 0;
            foreach (var tax in matchingTaxes)
            {
                if (tax.Amount == 0)
                    continue;

                var withholdingRate = tax.Currency.Equals(div.Currency, StringComparison.OrdinalIgnoreCase)
                    ? rate
                    : await _rateService.GetRateAsync(tax.Currency, div.Date);

                withholdingPln += tax.Amount * withholdingRate;
            }

            // Art. 30a ust. 9: the foreign tax credit cannot exceed 19% of the dividend. On top of that,
            // only tax withheld in line with the applicable treaty is creditable: the excess (e.g. 30% US
            // withholding without a W-8BEN) must be reclaimed abroad, not offset against Polish tax.
            var treatyRate = TreatyWithholdingRates.GetMaxCreditableRate(div.Isin);
            var maxCreditableRate = treatyRate ?? TaxConstants.TaxRate;
            var creditableWithholdingPln = Math.Min(withholdingPln, amountPln * maxCreditableRate);

            results.Add(new Dividend(
                Symbol: div.Symbol,
                Currency: div.Currency,
                Date: div.Date,
                Kind: div.Kind,
                AmountOriginal: div.Amount,
                WithholdingTaxOriginal: withholdingAmount,
                AmountPln: amountPln,
                WithholdingTaxPln: withholdingPln,
                CreditableWithholdingPln: creditableWithholdingPln,
                MaxCreditableRate: treatyRate,
                ExchangeRate: rate));
        }

        return results;
    }

    private static bool IsMatch(RawDividend dividend, RawWithholdingTax tax)
    {
        // A dividend and a payment in lieu for the same instrument can land on the same day
        // (partially lent position); their withholding rows must not be attributed to both.
        if (dividend.Kind != tax.Kind)
            return false;

        if (dividend.Date != tax.Date)
            return false;

        if (!string.IsNullOrWhiteSpace(dividend.Isin) || !string.IsNullOrWhiteSpace(tax.Isin))
            return string.Equals(dividend.Isin, tax.Isin, StringComparison.OrdinalIgnoreCase);

        return string.Equals(dividend.Symbol, tax.Symbol, StringComparison.OrdinalIgnoreCase);
    }
}
