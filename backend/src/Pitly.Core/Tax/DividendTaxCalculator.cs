using System.Globalization;
using Pitly.Core.Models;
using Pitly.Core.Services;

namespace Pitly.Core.Tax;

public interface IDividendTaxCalculator
{
    /// <summary>
    /// Converts dividends, interest and payments in lieu to PLN and attributes the foreign tax withheld on them.
    /// When <paramref name="warnings"/> is given, attributions that needed an assumption are reported in it.
    /// </summary>
    Task<List<Dividend>> CalculateAsync(
        List<RawDividend> dividends,
        List<RawWithholdingTax> withholdingTaxes,
        List<string>? warnings = null);
}

public class DividendTaxCalculator : IDividendTaxCalculator
{
    private readonly INbpExchangeRateService _rateService;

    public DividendTaxCalculator(INbpExchangeRateService rateService)
    {
        _rateService = rateService;
    }

    /// <summary>Working state of one payment while withholding is being attributed to it.</summary>
    private sealed class Payment
    {
        public required RawDividend Raw { get; init; }
        public required decimal Rate { get; init; }
        public required decimal AmountPln { get; init; }
        public decimal WithheldPln { get; set; }
        // Withholding expressed in the payment's own currency, for display next to the gross amount
        public decimal WithheldOriginal { get; set; }
    }

    public async Task<List<Dividend>> CalculateAsync(
        List<RawDividend> rawDividends,
        List<RawWithholdingTax> rawWithholdingTaxes,
        List<string>? warnings = null)
    {
        var payments = new List<Payment>();
        foreach (var div in rawDividends)
        {
            var rate = await _rateService.GetRateAsync(div.Currency, div.Date);
            var payment = new Payment { Raw = div, Rate = rate, AmountPln = div.Amount * rate };

            // Withholding reported in the same row as the payment (Trading 212) belongs to that payment only.
            if (div.WithheldAmount is { } withheld && withheld != 0m)
            {
                var currency = string.IsNullOrWhiteSpace(div.WithheldCurrency) ? div.Currency : div.WithheldCurrency;
                await AddWithholdingAsync(payment, withheld, currency, div.Date, share: 1m);
            }

            payments.Add(payment);
        }

        // Refunds that exceed what was withheld on the payment they point at (or that point at no payment).
        var refundExcess = new List<(string Scope, string Symbol, DateTime Date, decimal Pln)>();

        // Withholding booked as separate rows (Interactive Brokers) is matched within one account by instrument,
        // date and kind of payment, preferring payments in the currency of the withholding row.
        foreach (var tax in rawWithholdingTaxes)
        {
            if (tax.Amount == 0m)
                continue;

            var candidates = payments.Where(p => MatchKey(p.Raw) == MatchKey(tax)).ToList();
            var sameCurrency = candidates
                .Where(p => p.Raw.Currency.Equals(tax.Currency, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var targets = sameCurrency.Count > 0 ? sameCurrency : candidates;

            if (targets.Count == 0)
            {
                var taxRate = await _rateService.GetRateAsync(tax.Currency, tax.Date);
                if (tax.Amount > 0m)
                {
                    warnings?.Add(
                        $"Withholding tax of {Money(tax.Amount, tax.Currency)} for {tax.Symbol} on {tax.Date:yyyy-MM-dd} has no matching " +
                        $"{KindLabel(tax.Kind)} row on the same account, so it was not credited. Check that the statement with that payment is uploaded.");
                }
                else
                {
                    refundExcess.Add((ScopeKey(tax.AccountId, tax.Isin, tax.Symbol, tax.Kind), tax.Symbol, tax.Date, -tax.Amount * taxRate));
                }

                continue;
            }

            if (sameCurrency.Count == 0 && targets.Count > 1)
            {
                warnings?.Add(
                    $"Withholding tax of {Money(tax.Amount, tax.Currency)} for {tax.Symbol} on {tax.Date:yyyy-MM-dd} could not be tied to one payment: " +
                    $"the {targets.Count} payments of that day are in other currencies ({string.Join(", ", targets.Select(t => t.Raw.Currency).Distinct(StringComparer.OrdinalIgnoreCase))}). " +
                    "It was split between them in proportion to their PLN value; verify against the broker statement.");
            }

            // Proportional split uses the PLN value of the payments, i.e. a common currency.
            var totalPln = targets.Sum(t => Math.Max(t.AmountPln, 0m));
            foreach (var target in targets)
            {
                var share = targets.Count == 1 ? 1m
                    : totalPln > 0m ? Math.Max(target.AmountPln, 0m) / totalPln
                    : 1m / targets.Count;
                await AddWithholdingAsync(target, tax.Amount, tax.Currency, tax.Date, share);
            }
        }

        // Corrections carry a negative amount and net against the original. A net refund on one payment leaves
        // nothing to credit there and the surplus reduces the credit elsewhere, so that the total credit can never
        // exceed the tax actually withheld.
        foreach (var payment in payments.Where(p => p.WithheldPln < 0m))
        {
            refundExcess.Add((ScopeKey(payment.Raw.AccountId, payment.Raw.Isin, payment.Raw.Symbol, payment.Raw.Kind),
                payment.Raw.Symbol, payment.Raw.Date, -payment.WithheldPln));
            payment.WithheldPln = 0m;
            payment.WithheldOriginal = 0m;
        }

        foreach (var (scope, symbol, date, pln) in refundExcess)
        {
            var remaining = pln;
            var sameInstrument = payments
                .Where(p => p.WithheldPln > 0m && ScopeKey(p.Raw.AccountId, p.Raw.Isin, p.Raw.Symbol, p.Raw.Kind) == scope)
                .OrderByDescending(p => p.Raw.Date);
            remaining = Reduce(sameInstrument, remaining);
            var offsetWithinInstrument = pln - remaining;

            var offsetElsewhere = 0m;
            if (remaining > 0m)
            {
                var before = remaining;
                remaining = Reduce(payments.Where(p => p.WithheldPln > 0m).OrderByDescending(p => p.WithheldPln), remaining);
                offsetElsewhere = before - remaining;
            }

            warnings?.Add(
                $"A withholding-tax refund for {symbol} dated {date:yyyy-MM-dd} exceeds the tax withheld on that payment by {Pln(pln)}. " +
                (offsetWithinInstrument > 0m ? $"{Pln(offsetWithinInstrument)} was offset against other {symbol} payments. " : string.Empty) +
                (offsetElsewhere > 0m ? $"{Pln(offsetElsewhere)} was offset against withholding on other instruments so that the credit does not exceed the tax actually withheld. " : string.Empty) +
                (remaining > 0m ? $"{Pln(remaining)} could not be offset in the selected period. " : string.Empty) +
                "The original withholding is probably in a statement that is not uploaded; verify the foreign tax credit.");
        }

        var results = new List<Dividend>();
        foreach (var payment in payments)
        {
            var div = payment.Raw;

            // Art. 30a ust. 9: the foreign tax credit cannot exceed 19% of the dividend. On top of that,
            // only tax withheld in line with the applicable treaty is creditable: the excess (e.g. 30% US
            // withholding without a W-8BEN) must be reclaimed abroad, not offset against Polish tax.
            var treatyRate = TreatyWithholdingRates.GetMaxCreditableRate(div.Isin);
            var maxCreditableRate = treatyRate ?? TaxConstants.TaxRate;
            var creditableWithholdingPln = Math.Max(Math.Min(payment.WithheldPln, payment.AmountPln * maxCreditableRate), 0m);

            results.Add(new Dividend(
                Symbol: div.Symbol,
                Currency: div.Currency,
                Date: div.Date,
                Kind: div.Kind,
                AmountOriginal: div.Amount,
                WithholdingTaxOriginal: payment.WithheldOriginal,
                AmountPln: payment.AmountPln,
                WithholdingTaxPln: payment.WithheldPln,
                CreditableWithholdingPln: creditableWithholdingPln,
                MaxCreditableRate: treatyRate,
                ExchangeRate: payment.Rate,
                Isin: div.Isin));
        }

        return results;
    }

    private async Task AddWithholdingAsync(Payment payment, decimal amount, string currency, DateTime date, decimal share)
    {
        var sameCurrency = currency.Equals(payment.Raw.Currency, StringComparison.OrdinalIgnoreCase);
        var rate = sameCurrency ? payment.Rate : await _rateService.GetRateAsync(currency, date);
        var pln = amount * rate * share;

        payment.WithheldPln += pln;
        payment.WithheldOriginal += sameCurrency || payment.Rate == 0m ? amount * share : pln / payment.Rate;
    }

    /// <summary>Takes <paramref name="amount"/> PLN off the withholding of the given payments; returns what is left.</summary>
    private static decimal Reduce(IEnumerable<Payment> payments, decimal amount)
    {
        foreach (var payment in payments.ToList())
        {
            if (amount <= 0m)
                break;

            var take = Math.Min(payment.WithheldPln, amount);
            var keep = payment.WithheldPln - take;
            payment.WithheldOriginal = payment.WithheldPln > 0m ? payment.WithheldOriginal * keep / payment.WithheldPln : 0m;
            payment.WithheldPln = keep;
            amount -= take;
        }

        return amount;
    }

    // A dividend and a payment in lieu for the same instrument can land on the same day (partially lent position);
    // their withholding rows must not be attributed to both. Rows of different accounts never match.
    private static string MatchKey(RawDividend d) => MatchKey(d.AccountId, d.Isin, d.Symbol, d.Date, d.Kind);

    private static string MatchKey(RawWithholdingTax t) => MatchKey(t.AccountId, t.Isin, t.Symbol, t.Date, t.Kind);

    private static string MatchKey(string? account, string? isin, string symbol, DateTime date, IncomeKind kind)
        => ScopeKey(account, isin, symbol, kind) + "|" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The ISIN identifies the instrument when the statement has one; a row with an ISIN never matches a row
    // without it, because the ticker alone is not unique across exchanges.
    private static string ScopeKey(string? account, string? isin, string symbol, IncomeKind kind)
        => (account ?? string.Empty).ToUpperInvariant() + "|" +
           (string.IsNullOrWhiteSpace(isin) ? "sym:" + symbol.ToUpperInvariant() : "isin:" + isin.ToUpperInvariant()) + "|" + kind;

    private static string KindLabel(IncomeKind kind) => kind switch
    {
        IncomeKind.PaymentInLieu => "payment-in-lieu",
        IncomeKind.Interest => "interest",
        _ => "dividend"
    };

    private static string Money(decimal amount, string currency)
        => amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;

    private static string Pln(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture) + " PLN";
}
