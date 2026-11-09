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

            // Several dividend rows can share one withholding row (same instrument, day and kind: two Trading 212
            // rows, dividends on several lots). The withholding is split between them by amount, never counted twice.
            var group = rawDividends.Where(d => SameGroup(d, div)).ToList();
            var groupAmount = group.Sum(d => d.Amount);
            var share = group.Count <= 1 ? 1m : groupAmount > 0 ? div.Amount / groupAmount : 1m / group.Count;

            // Corrections carry a negative amount and net against the original; a net refund leaves nothing to credit.
            var withholdingAmount = Math.Max(matchingTaxes.Sum(t => t.Amount) * share, 0m);
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

                withholdingPln += tax.Amount * withholdingRate * share;
            }
            withholdingPln = Math.Max(withholdingPln, 0m);

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
                ExchangeRate: rate,
                Isin: div.Isin));
        }

        return results;
    }

    private static bool SameGroup(RawDividend a, RawDividend b)
    {
        if (a.Kind != b.Kind || a.Date != b.Date)
            return false;
        if (!string.IsNullOrWhiteSpace(a.Isin) || !string.IsNullOrWhiteSpace(b.Isin))
            return string.Equals(a.Isin, b.Isin, StringComparison.OrdinalIgnoreCase);
        return string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase);
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
