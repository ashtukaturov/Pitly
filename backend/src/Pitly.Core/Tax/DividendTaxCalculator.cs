using System.Globalization;
using Pitly.Core.Models;
using Pitly.Core.Services;

namespace Pitly.Core.Tax;

public interface IDividendTaxCalculator
{
    /// <summary>
    /// Converts dividends, interest and payments in lieu to PLN and attributes the foreign tax withheld on them.
    /// All rows are treated as the selected period. When <paramref name="warnings"/> is given, attributions that
    /// needed an assumption are reported in it.
    /// </summary>
    Task<List<Dividend>> CalculateAsync(
        List<RawDividend> dividends,
        List<RawWithholdingTax> withholdingTaxes,
        List<string>? warnings = null);

    /// <summary>
    /// The full calculation: withholding is matched over the whole <paramref name="history"/>, then the payments
    /// of <paramref name="period"/> (all of them when null) are converted and their credit is capped by
    /// <paramref name="method"/>. Every finding goes to <paramref name="diagnostics"/>.
    /// </summary>
    Task<DividendCalculation> CalculateAsync(
        IReadOnlyList<RawDividend> history,
        IReadOnlyList<RawWithholdingTax> events,
        TaxPeriod? period,
        CreditMethod method,
        List<TaxDiagnostic> diagnostics);
}

public sealed record DividendCalculation(List<Dividend> Dividends, List<WithholdingEvent> Events);

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
        public string Source { get; set; } = "none";
    }

    public async Task<List<Dividend>> CalculateAsync(
        List<RawDividend> rawDividends,
        List<RawWithholdingTax> rawWithholdingTaxes,
        List<string>? warnings = null)
    {
        var diagnostics = new List<TaxDiagnostic>();
        var result = await CalculateAsync(rawDividends, rawWithholdingTaxes, null, CreditMethod.TreatyCap, diagnostics);
        warnings?.AddRange(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.Message));
        return result.Dividends;
    }

    public async Task<DividendCalculation> CalculateAsync(
        IReadOnlyList<RawDividend> history,
        IReadOnlyList<RawWithholdingTax> events,
        TaxPeriod? period,
        CreditMethod method,
        List<TaxDiagnostic> diagnostics)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var payments = new List<Payment>();
        foreach (var div in history)
        {
            var rate = await RateAsync(rates, div.Currency, div.Date);
            var payment = new Payment { Raw = div, Rate = rate, AmountPln = div.Amount * rate };

            // Withholding reported in the same row as the payment (Trading 212, generic CSV) belongs to that payment only.
            if (div.WithheldAmount is { } withheld && withheld != 0m)
            {
                var currency = string.IsNullOrWhiteSpace(div.WithheldCurrency) ? div.Currency : div.WithheldCurrency;
                var withheldRate = currency.Equals(div.Currency, StringComparison.OrdinalIgnoreCase) ? rate : await RateAsync(rates, currency, div.Date);
                payment.WithheldPln += withheld * withheldRate;
                payment.WithheldOriginal += currency.Equals(div.Currency, StringComparison.OrdinalIgnoreCase) || rate == 0m ? withheld : withheld * withheldRate / rate;
                payment.Source = "same row";
            }

            payments.Add(payment);
        }

        var match = WithholdingMatcher.Match(history, events);
        var eventRecords = new List<WithholdingEvent>();
        var seenMessages = new HashSet<string>(StringComparer.Ordinal);

        bool InPeriod(DateTime date) => period is null || period.IncludesDate(date);
        bool YearShown(int year) => period is null || period.Year == year;
        bool EventShown(RawWithholdingTax e) => YearShown(e.Date.Year) || (e.BookingYear is { } y && YearShown(y));

        void Add(TaxDiagnostic diagnostic)
        {
            if (seenMessages.Add(diagnostic.Code + "|" + diagnostic.Message))
                diagnostics.Add(diagnostic);
        }

        var byRaw = payments.ToDictionary(p => p.Raw, p => p, ReferenceEqualityComparer.Instance as IEqualityComparer<RawDividend>);

        foreach (var linked in match.Payments)
        {
            if (linked.Links.Count == 0)
                continue;

            var payment = byRaw[linked.Payment];
            var paymentYear = payment.Raw.Date.Year;
            if (payment.Source == "none")
                payment.Source = linked.Links.Any(l => l.Share < 1m) ? "shared" : "matched";

            // A refund is netted against the tax it refunds in the currency of the tax; what remains is tax paid and
            // is valued at the rate of the day it was paid (art. 11a ust. 3). Netting after conversion would turn a
            // full refund at another rate into a spurious excess or a spurious credit.
            foreach (var currencyGroup in linked.Links.GroupBy(l => l.Event.Currency.ToUpperInvariant()))
            {
                var currency = currencyGroup.Key;
                var net = currencyGroup.Sum(l => l.Event.Amount * l.Share);
                var sameCurrency = currency.Equals(payment.Raw.Currency, StringComparison.OrdinalIgnoreCase);
                var positives = currencyGroup.Where(l => l.Event.Amount > 0m).ToList();
                var valuationDate = positives.Count > 0 ? positives[0].Event.EffectiveDate : currencyGroup.First().Event.EffectiveDate;
                var rate = sameCurrency && valuationDate.Date == payment.Raw.Date.Date ? payment.Rate : await RateAsync(rates, currency, valuationDate);

                foreach (var positive in positives.Where(l => l.Event.EffectiveDate.Date != payment.Raw.Date.Date))
                {
                    if (InPeriod(payment.Raw.Date))
                    {
                        Add(new TaxDiagnostic(DiagnosticCodes.RateDate, DiagnosticSeverity.Info,
                            $"Tax of {Money(positive.Event.Amount * positive.Share, currency)} on {payment.Raw.Symbol} ({payment.Raw.Date:yyyy-MM-dd}) was paid on " +
                            $"{positive.Event.EffectiveDate:yyyy-MM-dd} and is converted at the NBP rate of the day before that date, not the income's rate (art. 11a ust. 3).",
                            positive.Event.SourceFile, payment.Raw.AccountId, [paymentYear]));
                    }
                }

                if (net >= 0m)
                {
                    payment.WithheldPln += net * rate;
                    payment.WithheldOriginal += sameCurrency || payment.Rate == 0m ? net : net * rate / payment.Rate;
                }
                else
                {
                    var excess = -net;
                    var refund = currencyGroup.Where(l => l.Event.Amount < 0m).OrderByDescending(l => l.Event.EffectiveDate).First().Event;
                    var excessRate = await RateAsync(rates, currency, refund.EffectiveDate);
                    var excessPln = excess * excessRate;
                    var years = new[] { paymentYear }.Concat(currencyGroup.Select(l => l.Event.BookingYear).OfType<int>()).Distinct().OrderBy(y => y).ToList();
                    if (years.Any(YearShown))
                    {
                        Add(new TaxDiagnostic(DiagnosticCodes.ExcessRefund, DiagnosticSeverity.Warning,
                            $"The withholding-tax refund for {payment.Raw.Symbol} dated {refund.Date:yyyy-MM-dd} exceeds the tax withheld on that payment by " +
                            $"{Money(excess, currency)} ({Pln(excessPln)}). The credit on that payment is 0 and the excess is not applied to any other payment, " +
                            "account or instrument: it stays unresolved until the statement that explains it is uploaded or the refund is verified by hand.",
                            refund.SourceFile, payment.Raw.AccountId, years, excessPln, AffectsAmounts: true));
                    }
                }

                // Money that moved in another year than the income is a policy question (correction of the income's
                // year or an event of the booking year); it is flagged for both years and never decided here.
                foreach (var link in currencyGroup.Where(l => l.Event.BookingYear is { } y && y != paymentYear))
                {
                    var bookingYear = link.Event.BookingYear!.Value;
                    if (!YearShown(paymentYear) && !YearShown(bookingYear))
                        continue;
                    var amount = link.Event.Amount * link.Share;
                    var amountPln = Math.Abs(amount) * await RateAsync(rates, currency, link.Event.EffectiveDate);
                    var what = amount < 0m ? "refund of tax withheld" : "additional tax";
                    Add(new TaxDiagnostic(DiagnosticCodes.CrossYearAdjustment, DiagnosticSeverity.Warning,
                        $"A {what} on {payment.Raw.Symbol} paid on {payment.Raw.Date:yyyy-MM-dd} ({paymentYear}) was booked in {bookingYear}: {Money(Math.Abs(amount), currency)} ({Pln(amountPln)}). " +
                        $"It is attributed to the {paymentYear} payment, so the {paymentYear} credit already reflects it. Whether its tax effect belongs to {paymentYear} (a corrected return) " +
                        $"or to {bookingYear} is not decided by Pitly; confirm the treatment before filing either year.",
                        link.Event.SourceFile, payment.Raw.AccountId, new[] { paymentYear, bookingYear }.OrderBy(y => y).ToList(), amountPln, AffectsAmounts: true));
                }
            }

            if (payment.WithheldPln < 0m)
            {
                // Links in several currencies can still net below zero; the credit is never negative.
                payment.WithheldPln = 0m;
                payment.WithheldOriginal = 0m;
            }
        }

        foreach (var shared in match.Shared)
        {
            if (!shared.Payments.Any(p => InPeriod(p.Date)))
                continue;
            var first = shared.Payments[0];
            Add(new TaxDiagnostic(DiagnosticCodes.SharedWithholding, DiagnosticSeverity.Info,
                $"Withholding tax of {Money(shared.Event.Amount, shared.Event.Currency)} on {first.Symbol} ({first.Date:yyyy-MM-dd}) was shared between the " +
                $"{shared.Payments.Count} rows of that distribution in proportion to their gross amounts; the total credit does not depend on the split.",
                shared.Event.SourceFile, first.AccountId, [first.Date.Year]));
        }

        // Unresolved rows that cancel each other out (a booking and its reversal of a payment that was itself reversed)
        // change nothing and are only noted.
        var unresolvedGroups = match.Unresolved
            .GroupBy(u => ScopeKey(u.Event))
            .ToList();
        foreach (var group in unresolvedGroups)
        {
            var items = group.ToList();
            if (items.Count > 1 && items.Sum(u => u.Event.Amount) == 0m && items.All(u => u.Code == DiagnosticCodes.UnmatchedWithholding))
            {
                var e = items[0].Event;
                if (EventShown(e))
                {
                    Add(new TaxDiagnostic(DiagnosticCodes.UnmatchedWithholding, DiagnosticSeverity.Info,
                        $"{items.Count} withholding rows for {e.Symbol} dated {e.Date:yyyy-MM-dd} match no payment but cancel each other out (net 0.00 {e.Currency}); nothing was changed.",
                        e.SourceFile, e.AccountId, [e.Date.Year]));
                }

                foreach (var u in items)
                    eventRecords.Add(await ToEventAsync(rates, u.Event, WithholdingEventStatus.Unresolved, null, 0m, null));
                continue;
            }

            foreach (var u in items)
            {
                var e = u.Event;
                var pln = Math.Abs(e.Amount) * await RateAsync(rates, e.Currency, e.EffectiveDate);
                var years = new[] { e.Date.Year }.Concat(e.BookingYear is { } y ? [y] : Array.Empty<int>()).Distinct().OrderBy(x => x).ToList();
                if (EventShown(e))
                {
                    Add(new TaxDiagnostic(u.Code, DiagnosticSeverity.Warning,
                        u.Reason + $" {Pln(pln)} of foreign tax is not in the declared amounts.",
                        e.SourceFile, e.AccountId, years, pln, AffectsAmounts: true));
                }

                if (EventShown(e))
                    eventRecords.Add(await ToEventAsync(rates, e, WithholdingEventStatus.Unresolved, null, pln, u.Code));
            }
        }

        foreach (var linked in match.Payments)
        {
            foreach (var link in linked.Links)
            {
                if (!EventShown(link.Event) && !InPeriod(linked.Payment.Date))
                    continue;
                var status = link.Share < 1m ? WithholdingEventStatus.Shared : WithholdingEventStatus.Matched;
                var matched = $"{linked.Payment.Symbol} {linked.Payment.Date:yyyy-MM-dd}";
                eventRecords.Add(await ToEventAsync(rates, link.Event with { Amount = link.Event.Amount * link.Share }, status, matched, 0m, null));
            }
        }

        if (method == CreditMethod.StatutoryCap && payments.Any(p => InPeriod(p.Raw.Date)))
        {
            Add(new TaxDiagnostic(DiagnosticCodes.CreditMethod, DiagnosticSeverity.Info,
                "The foreign tax credit was limited only by 19% of each payment (the literal reading of art. 30a ust. 9 followed by several NSA rulings), " +
                "not by the treaty rate of the issuer's country, which is the position of the Ministry of Finance and KIS. This method is for comparison; the default is the treaty cap.",
                null, null, period is null ? null : [period.Year]));
        }

        var results = new List<Dividend>();
        foreach (var payment in payments.Where(p => InPeriod(p.Raw.Date)))
        {
            var div = payment.Raw;

            // Art. 30a ust. 9: the foreign tax credit cannot exceed 19% of the income. On top of that, for dividends
            // only tax withheld in line with the applicable treaty is creditable (TreatyCap): the excess (e.g. 30% US
            // withholding without a W-8BEN) must be reclaimed abroad, not offset against Polish tax. The treaty
            // dividend rates do not apply to interest: interest has its own treaty articles, which Pitly does not
            // model, so interest keeps the statutory cap whatever ISIN the row carries.
            decimal? treatyRate = div.Kind == IncomeKind.Interest || method == CreditMethod.StatutoryCap
                ? null
                : TreatyWithholdingRates.GetMaxCreditableRate(div.Isin);
            var maxCreditableRate = treatyRate ?? TaxConstants.TaxRate;
            var creditableWithholdingPln = Math.Max(Math.Min(payment.WithheldPln, payment.AmountPln * maxCreditableRate), 0m);

            if (div.Kind == IncomeKind.Interest && !string.IsNullOrWhiteSpace(div.Isin) && payment.WithheldPln > 0m)
            {
                Add(new TaxDiagnostic(DiagnosticCodes.ClassificationUnverified, DiagnosticSeverity.Info,
                    $"Interest on {div.Symbol} ({div.Isin}) carries foreign tax; the credit was capped at 19% (art. 30a ust. 9), not at a treaty rate, " +
                    "because the treaty rates on file are dividend rates.",
                    div.SourceFile, div.AccountId, [div.Date.Year]));
            }

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
                Isin: div.Isin,
                CountryCode: div.CountryCode,
                AccountId: div.AccountId,
                TransactionId: div.TransactionId,
                ActionId: div.ActionId,
                SourceFile: div.SourceFile,
                WithholdingSource: payment.Source));
        }

        return new DividendCalculation(results, eventRecords.OrderBy(e => e.Date).ThenBy(e => e.Symbol).ToList());
    }

    private async Task<WithholdingEvent> ToEventAsync(
        Dictionary<string, decimal> rates, RawWithholdingTax e, WithholdingEventStatus status, string? matched, decimal unresolvedPln, string? code)
    {
        var rate = await RateAsync(rates, e.Currency, e.EffectiveDate);
        return new WithholdingEvent(
            e.AccountId, e.Symbol, e.Isin, e.Currency, e.Kind, e.Amount, e.Date, e.BookingDate, e.SourceStatementYear,
            e.TransactionId, e.ActionId, e.Reference, e.SourceFile, e.RawType, e.RawDescription,
            status, matched, e.Amount * rate, unresolvedPln, code);
    }

    private async Task<decimal> RateAsync(Dictionary<string, decimal> cache, string currency, DateTime date)
    {
        var key = currency.ToUpperInvariant() + "@" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (cache.TryGetValue(key, out var rate))
            return rate;
        rate = await _rateService.GetRateAsync(currency, date);
        cache[key] = rate;
        return rate;
    }

    private static string ScopeKey(RawWithholdingTax e)
        => (e.AccountId ?? string.Empty).ToUpperInvariant() + "|" +
           (string.IsNullOrWhiteSpace(e.Isin) ? "sym:" + e.Symbol.ToUpperInvariant() : "isin:" + e.Isin.ToUpperInvariant()) + "|" +
           e.Kind + "|" + e.Currency.ToUpperInvariant() + "|" + e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + (e.ActionId ?? string.Empty);

    private static string Money(decimal amount, string currency) => WithholdingMatcher.Money(amount, currency);

    private static string Pln(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture) + " PLN";
}
