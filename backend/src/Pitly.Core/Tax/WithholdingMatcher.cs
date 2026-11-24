using System.Globalization;
using Pitly.Core.Models;

namespace Pitly.Core.Tax;

/// <summary>
/// Ties every withholding event (tax withheld, refunded or re-charged) to the payment it belongs to, over the whole
/// uploaded history and never across accounts. The order of evidence is:
///   1. the broker's own link: the same ActionID on the payment and on the tax row (IB Flex);
///   2. a complete, unambiguous match: same account, same kind of income, same instrument (ISIN against ISIN,
///      ticker against ticker, never one against the other), same reference date, same currency; for credit
///      interest the instrument is the currency and the reference is the interest period when both rows name one;
///   3. the same match in another currency, when exactly one payment fits;
///   4. the broker's text of the distribution on another day (IB prints "USD 0.31 per Share" on every row of one
///      distribution and an Activity Statement books a correction under the day it was made): the event is tied
///      to the one payment of the instrument that carries the same text. The text names a distribution, not a
///      payment, so several payments with it stay ambiguous and the years of all of them are reported.
/// A row that fits no payment, or several interest payments, is left unresolved with a reason: nothing is
/// attributed by proximity (a similar amount, the nearest date), and an excess refund never moves to another
/// payment. Several dividend rows of one instrument on one pay date are one distribution event, so one tax row of
/// that event is shared between them.
/// </summary>
public static class WithholdingMatcher
{
    /// <summary>A withholding event attributed to a payment; <see cref="Share"/> of its amount belongs to that payment.</summary>
    public sealed record Link(RawWithholdingTax Event, decimal Share);

    public sealed class PaymentLinks
    {
        public required RawDividend Payment { get; init; }
        public List<Link> Links { get; } = [];
    }

    /// <summary>An event that was not attributed, with the code and the English reason the diagnostics carry.</summary>
    public sealed record Unresolved(RawWithholdingTax Event, string Code, string Reason, IReadOnlyList<RawDividend> Candidates);

    /// <summary>One event shared between the payments of one distribution event.</summary>
    public sealed record Shared(RawWithholdingTax Event, IReadOnlyList<RawDividend> Payments);

    public sealed record Result(IReadOnlyList<PaymentLinks> Payments, IReadOnlyList<Unresolved> Unresolved, IReadOnlyList<Shared> Shared);

    public static Result Match(IReadOnlyList<RawDividend> payments, IReadOnlyList<RawWithholdingTax> events)
    {
        var links = payments.Select(p => new PaymentLinks { Payment = p }).ToList();
        var byPayment = new Dictionary<RawDividend, PaymentLinks>(ReferenceEqualityComparer.Instance);
        foreach (var l in links)
            byPayment[l.Payment] = l;

        var unresolved = new List<Unresolved>();
        var shared = new List<Shared>();

        foreach (var e in events)
        {
            if (e.Amount == 0m)
                continue;

            if (e.Kind == IncomeKind.Unknown)
            {
                unresolved.Add(new Unresolved(e, DiagnosticCodes.UnknownIncomeKind,
                    $"{Describe(e)} names no instrument and no kind of income Pitly recognises" +
                    (string.IsNullOrWhiteSpace(e.RawDescription) ? string.Empty : $" (\"{e.RawDescription}\")") +
                    ", so it was not attributed to any payment.", []));
                continue;
            }

            // 1. The broker's own link. One action id can cover a dividend and the payment in lieu of the same
            //    distribution; the tax row says which of the two it belongs to, so the candidates are narrowed by
            //    the kind of income first and the id is never used to spread tax across incompatible kinds.
            if (!string.IsNullOrWhiteSpace(e.ActionId))
            {
                var byActionAnyKind = payments.Where(p => SameAccount(p, e) && string.Equals(p.ActionId, e.ActionId, StringComparison.OrdinalIgnoreCase)).ToList();
                var byAction = byActionAnyKind.Where(p => SameKind(p, e)).ToList();
                if (byAction.Count == 0)
                    byAction = byActionAnyKind;
                if (byAction.Count == 1)
                {
                    byPayment[byAction[0]].Links.Add(new Link(e, 1m));
                    continue;
                }

                if (byAction.Count > 1)
                {
                    if (SameDistributionEvent(byAction))
                    {
                        ShareBetween(e, byAction, byPayment, shared);
                        continue;
                    }

                    unresolved.Add(new Unresolved(e, DiagnosticCodes.AmbiguousMatch,
                        $"{Describe(e)} carries action id {e.ActionId}, which {byAction.Count} different payments carry too; it was not attributed to any of them.", byAction));
                    continue;
                }
                // No payment carries this action id (a reclassification gets a new id): fall through to the full match.
            }

            // 2. The complete match in the currency of the event, 3. then in any currency. When both rows print the
            //    broker's text of the distribution, it has to agree too: a refund of last year's distribution booked
            //    on this year's pay day is not this year's tax.
            var exact = payments.Where(p => SameAccount(p, e) && SameKind(p, e) && SameInstrument(p, e) && SameReference(p, e) && SameDistribution(p, e)).ToList();
            var sameCurrency = exact.Where(p => p.Currency.Equals(e.Currency, StringComparison.OrdinalIgnoreCase)).ToList();
            var candidates = sameCurrency.Count > 0 ? sameCurrency : exact;

            if (candidates.Count == 1)
            {
                byPayment[candidates[0]].Links.Add(new Link(e, 1m));
                continue;
            }

            if (candidates.Count > 1)
            {
                if (e.Kind != IncomeKind.Interest && sameCurrency.Count == candidates.Count && SameDistributionEvent(candidates))
                {
                    ShareBetween(e, candidates, byPayment, shared);
                    continue;
                }

                var listed = string.Join(", ", candidates.Take(4).Select(p => $"{Money(p.Amount, p.Currency)} on {p.Date:yyyy-MM-dd}"));
                var why = e.Kind == IncomeKind.Interest && string.IsNullOrWhiteSpace(e.Reference)
                    ? " and names no interest period"
                    : sameCurrency.Count == 0 ? " and none of them is in the currency of the tax row" : string.Empty;
                unresolved.Add(new Unresolved(e, DiagnosticCodes.AmbiguousMatch,
                    $"{Describe(e)} fits {candidates.Count} candidate {KindLabel(e.Kind)} payments on account {Account(e)} ({listed}){why}, " +
                    "so it was not attributed to any of them.", candidates));
                continue;
            }

            // 4. The broker's text of the distribution on another day. It is the broker's own identification of the
            //    distribution, never a similar amount or the nearest date; but it identifies a payment only when one
            //    payment of the instrument carries it.
            if (!string.IsNullOrWhiteSpace(e.Distribution))
            {
                var byDistribution = payments.Where(p => SameAccount(p, e) && SameKind(p, e) && SameInstrument(p, e) &&
                                                         string.Equals(p.Distribution, e.Distribution, StringComparison.OrdinalIgnoreCase)).ToList();
                var byDistributionSameCurrency = byDistribution.Where(p => p.Currency.Equals(e.Currency, StringComparison.OrdinalIgnoreCase)).ToList();
                var byText = byDistributionSameCurrency.Count > 0 ? byDistributionSameCurrency : byDistribution;
                if (byText.Count == 1)
                {
                    byPayment[byText[0]].Links.Add(new Link(e, 1m));
                    continue;
                }

                if (byText.Count > 1)
                {
                    if (byDistributionSameCurrency.Count == byText.Count && SameDistributionEvent(byText))
                    {
                        ShareBetween(e, byText, byPayment, shared);
                        continue;
                    }

                    var listed = string.Join(", ", byText.OrderBy(p => p.Date).Take(4).Select(p => $"{Money(p.Amount, p.Currency)} on {p.Date:yyyy-MM-dd}"));
                    unresolved.Add(new Unresolved(e, DiagnosticCodes.AmbiguousMatch,
                        $"{Describe(e)} carries the broker's text \"{e.Distribution}\", which {byText.Count} {KindLabel(e.Kind)} payments of {Instrument(e)} on account {Account(e)} carry too ({listed}); " +
                        "the text names the distribution, not the payment, so the event was not attributed to any of them.", byText));
                    continue;
                }
            }

            // Nothing fits. Say whether the instrument is in the history at all, so that the user knows what to upload.
            var near = payments.Where(p => SameAccount(p, e) && SameKind(p, e) && SameInstrument(p, e)).ToList();
            unresolved.Add(new Unresolved(e, DiagnosticCodes.UnmatchedWithholding, UnmatchedReason(e, near), near));
        }

        return new Result(links, unresolved, shared);
    }

    private static void ShareBetween(RawWithholdingTax e, List<RawDividend> group, Dictionary<RawDividend, PaymentLinks> byPayment, List<Shared> shared)
    {
        // Shares follow the gross amounts; they are in one currency, so no conversion is needed here.
        var total = group.Sum(p => Math.Max(p.Amount, 0m));
        foreach (var p in group)
        {
            var share = total > 0m ? Math.Max(p.Amount, 0m) / total : 1m / group.Count;
            byPayment[p].Links.Add(new Link(e, share));
        }

        shared.Add(new Shared(e, group));
    }

    private static string UnmatchedReason(RawWithholdingTax e, List<RawDividend> near)
    {
        var kind = KindLabel(e.Kind);
        if (e.Amount > 0m)
        {
            if (near.Count == 0)
            {
                return $"{Describe(e)} has no matching {kind} row on account {Account(e)}, so it was not credited. " +
                       "Check that the statement with that payment is uploaded.";
            }

            return $"{Describe(e)} has no matching {kind} row on account {Account(e)}: the {Instrument(e)} payments in the uploaded history are dated " +
                   $"{Dates(near)}, none on {ReferenceLabel(e)}. It was not credited.";
        }

        if (near.Count == 0)
        {
            return $"A withholding-tax refund of {Money(-e.Amount, e.Currency)} for {Instrument(e)} dated {ReferenceLabel(e)} points at a {kind} payment " +
                   $"that is not in the uploaded history: the statement with that payment ({e.Date.Year}) is not uploaded. The refund was not applied anywhere.";
        }

        if (!string.IsNullOrWhiteSpace(e.Distribution))
        {
            // The text of the distribution is on the row and no uploaded payment carries it: the payment is missing.
            return $"A withholding-tax refund of {Money(-e.Amount, e.Currency)} for {Instrument(e)} dated {ReferenceLabel(e)} carries the broker's text \"{e.Distribution}\" " +
                   $"of the distribution it corrects, and none of the {Instrument(e)} payments in the uploaded history (dated {Dates(near)}) carries it: " +
                   "the statement with that payment is not uploaded. The refund was not applied anywhere.";
        }

        return $"A withholding-tax refund of {Money(-e.Amount, e.Currency)} for {Instrument(e)} dated {ReferenceLabel(e)} matches none of the {Instrument(e)} payments " +
               $"in the uploaded history by date (dated {Dates(near)}), and the row gives no text of the distribution it corrects, so it may reverse tax withheld on any of them. " +
               "The refund was not applied anywhere.";
    }

    private static bool SameAccount(RawDividend p, RawWithholdingTax e)
        => string.Equals(p.AccountId ?? string.Empty, e.AccountId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool SameKind(RawDividend p, RawWithholdingTax e) => p.Kind == e.Kind;

    private static bool SameInstrument(RawDividend p, RawWithholdingTax e)
    {
        if (e.Kind == IncomeKind.Interest)
            return p.Kind == IncomeKind.Interest && p.Currency.Equals(e.Currency, StringComparison.OrdinalIgnoreCase);

        var pIsin = string.IsNullOrWhiteSpace(p.Isin) ? null : p.Isin.Trim();
        var eIsin = string.IsNullOrWhiteSpace(e.Isin) ? null : e.Isin.Trim();
        if (pIsin is not null || eIsin is not null)
            return pIsin is not null && eIsin is not null && pIsin.Equals(eIsin, StringComparison.OrdinalIgnoreCase);

        return p.Symbol.Trim().Equals(e.Symbol.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Two rows that both print the broker's text of the distribution are one event only when the texts agree.</summary>
    private static bool SameDistribution(RawDividend p, RawWithholdingTax e)
        => string.IsNullOrWhiteSpace(p.Distribution) || string.IsNullOrWhiteSpace(e.Distribution) ||
           string.Equals(p.Distribution, e.Distribution, StringComparison.OrdinalIgnoreCase);

    private static bool SameReference(RawDividend p, RawWithholdingTax e)
    {
        if (!string.IsNullOrWhiteSpace(p.Reference) && !string.IsNullOrWhiteSpace(e.Reference))
            return NormalizeReference(p.Reference) == NormalizeReference(e.Reference);
        return p.Date.Date == e.Date.Date;
    }

    /// <summary>Several rows of one instrument, one account, one pay date and one currency are one distribution event.</summary>
    private static bool SameDistributionEvent(List<RawDividend> group)
    {
        var first = group[0];
        return group.All(p =>
            p.Kind == first.Kind &&
            p.Date.Date == first.Date.Date &&
            p.Currency.Equals(first.Currency, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Isin ?? string.Empty, first.Isin ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            (!string.IsNullOrWhiteSpace(first.Isin) || p.Symbol.Equals(first.Symbol, StringComparison.OrdinalIgnoreCase)));
    }

    public static string NormalizeReference(string reference)
        => string.Join(" ", reference.ToUpperInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string Describe(RawWithholdingTax e)
        => (e.Amount > 0m ? "Withholding tax of " + Money(e.Amount, e.Currency) : "A withholding-tax refund of " + Money(-e.Amount, e.Currency)) +
           $" for {Instrument(e)} dated {ReferenceLabel(e)}";

    private static string Instrument(RawWithholdingTax e)
        => e.Kind == IncomeKind.Interest ? e.Currency + " interest" : e.Symbol;

    private static string ReferenceLabel(RawWithholdingTax e)
        => e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
           (string.IsNullOrWhiteSpace(e.Reference) ? string.Empty : " (" + e.Reference + ")");

    private static string Account(RawWithholdingTax e) => string.IsNullOrWhiteSpace(e.AccountId) ? "(no account number)" : e.AccountId;

    private static string Dates(IEnumerable<RawDividend> payments)
        => string.Join(", ", payments.Select(p => p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Distinct().OrderBy(d => d).Take(5));

    public static string KindLabel(IncomeKind kind) => kind switch
    {
        IncomeKind.PaymentInLieu => "payment-in-lieu",
        IncomeKind.Interest => "interest",
        IncomeKind.Unknown => "unknown-income",
        _ => "dividend"
    };

    public static string Money(decimal amount, string currency)
        => amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;
}
