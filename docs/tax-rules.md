# Tax rules implemented in Pitly

What the code calculates, where the rule comes from, and a control example for each rule. Every example here is
also a test, so a change that breaks one fails `dotnet test`.

This document describes the software. It is not tax advice, and several points below are interpretations where
the law or the Ministry of Finance gives no explicit rule. Those are marked.

Abbreviations: "ustawa o PIT" is the Polish Personal Income Tax Act; "O.p." is Ordynacja podatkowa (the Tax
Ordinance).

Article numbers repeat the comments in the code, with two exceptions: the exchange-rate rule (art. 11a) and the
FIFO rule (art. 24 ust. 10) are not cited in the code and were added here without checking the statute text.
Verify those two before quoting them.

## Conversion to PLN

Each amount is converted at the NBP mid rate of the last business day before the transaction date (art. 11a
ust. 1 and 2 ustawy o PIT). The trade date is used, not the settlement date; the README lists this as a
limitation.

Tax paid abroad is converted at the rate of the day before it was paid (art. 11a ust. 3: "zapłaty podatku").
Tax withheld on the day of the payment therefore shares the payment's rate. Tax paid or re-charged on another day
(IB Flex `Date/Time`, or an explicit booking date) is converted at the rate of its own day and the result notes
it (`RATE_DATE`). A refund is netted against the tax it refunds in the currency of the tax before conversion, so
a full refund leaves exactly zero whatever the rates; the remaining tax is valued at the original rate.
**Interpretation:** the law names the day of payment; valuing a refund at the original rate is Pitly's choice
to keep "refunded in full" and "zero credit" the same thing.

## Precision

Every PLN amount keeps its full precision through the calculation and in the stored session. Rounding happens
once, in `Pit38Fields.FromSummary`, by the rule of each field. Control example: 0.01 EUR of interest at a fixed
test rate of 10.51 PLN/EUR is 0.1051 PLN; 19% of it is 0.019969 PLN, entered as 0.02 PLN (grosz upward). Rounding
the income to 0.11 PLN first would give 0.0209 PLN, entered as 0.03 PLN, which is wrong. (10.51 is a test rate,
not a quotation.)

## Capital gains (art. 30b)

- Income is proceeds minus costs. Proceeds are entered gross; buy and sell commissions are costs.
- Lots are consumed first in, first out (art. 24 ust. 10), separately for each broker account. Within an account lots are keyed by ISIN, or by ticker when the statement has no ISIN.
- Splits, ISIN changes, mergers and spin-offs change the lots of the account whose statement reports them.
- Only sales inside the taxable window are taxed: the whole year, or from the residency start date when that
  date falls inside the year. Earlier events still build the lots. Nothing dated after 31 December of the
  chosen year takes part.
- When a sale has no purchase history, the import of that year fails with a message; an earlier year with
  complete history still calculates.

Control examples:

| Input | Expected |
|---|---|
| Account A buys 1 share for 100 PLN in 2024. Account B buys 1 for 300 PLN in 2025 and sells it for 350 PLN. | Proceeds 350, costs 300, income 50, tax due 10 PLN. A's share keeps its cost of 100. |
| Both accounts report the same 2:1 split. | Each account's position doubles once. |
| Full 2024 history, plus a 2025 sale of an instrument with no purchase on record. | 2024 calculates. 2025 fails and the error offers 2024. |

## Prior-year losses (art. 9 ust. 3)

- A loss can be deducted in the five tax years after it arose.
- A loss from 2019 or later can be deducted in one go up to 5 000 000 PLN, once per loss (pkt 2); otherwise
  at most 50% of the loss per year. An earlier loss: at most 50% of the loss per year (pkt 1).
- Oldest losses are used first and the total never exceeds this year's income.
- Losses that will expire unused produce a warning.

Control example: income 10 000 PLN and an available loss of 10 000 PLN give a tax base of 0 and 0 PLN to pay,
on the dashboard, on the PIT-38 page and after reopening the session.

## Capital gains: rounding (art. 63 § 1 O.p.)

The tax base ("Podstawa obliczenia podatku") is rounded to full PLN: below 50 groszy down, 50 groszy and more
up. The tax is 19% of the base, and the tax due ("Podatek należny") is rounded to full PLN the same way.

## Dividends, interest and payments in lieu (art. 30a)

- Foreign dividends are taxed at a flat 19% (art. 30a ust. 1 pkt 4). Credit interest on cash, bank interest
  (Revolut savings) and bond interest are interest under art. 30a ust. 1 pkt 2-3; IB payments in lieu of dividends
  are treated as flat-tax income of the same field. Debit (margin) interest is ignored.
- **Interpretation:** how payments in lieu should be classified is not settled in Polish practice. The UI flags
  them (`CLASSIFICATION_UNVERIFIED`). The same flag marks bond interest and interest that carries an ISIN: a
  broker's label "interest" is not a legal classification, and securities-lending income is not classified by
  Pitly either.
- IB lists some payments in lieu inside the `Dividends` section, with the tax row labelled "Payment in Lieu of
  Dividend - US Tax". The parser classifies by the description so that both sides get the same kind; otherwise
  that withholding would match no payment and its credit would be lost.

### Foreign tax credit (art. 30a ust. 9)

- Tax withheld abroad on dividends is credited only up to the rate of the double-tax treaty with the issuer's
  country, taken from the ISIN prefix (`TreatyWithholdingRates`: 15% for US, 10% for GB, and so on). Tax withheld
  above that rate is shown but not credited, because it has to be reclaimed abroad. This is the position of the
  Ministry of Finance (Broszura PIT-38, part G) and of KIS rulings on 30% US withholding without a W-8BEN.
- Without an ISIN or a treaty rate on file, the statutory cap of 19% applies.
- The treaty rates on file are dividend rates. Interest keeps the statutory 19% cap whatever ISIN the row
  carries; the treaty articles on interest are not modelled.
- The credit never exceeds the Polish flat tax on the same stream.
- **Comparison method.** `CreditMethod.StatutoryCap` credits the tax actually paid up to 19% of each payment,
  the literal reading of art. 30a ust. 9 followed by several NSA rulings. It is never the default, has to be
  chosen on the import page, is recorded with the session and shown next to the result (`CREDIT_METHOD`).
  An older session is never switched to it.
- **Limitation:** the ISIN prefix is a proxy. A depositary receipt carries the ISIN of the depositary's country
  while the dividend is taxed in the company's country.

### Attribution of withholding events

Every withholding row of every uploaded statement is one event: tax withheld (positive), refunded or reversed
(negative), with its account, the date the broker refers it to, its booking date or statement year, the broker's
ids and text. Events are tied to payments over the whole uploaded history first; the tax year is selected
afterwards. Inside one account, in this order:

1. the broker's own link: the same `ActionID` on payment and tax row (IB Flex);
2. a complete match: same kind of income, same instrument (ISIN against ISIN, ticker against ticker, never one
   against the other), same reference date, same currency. For credit interest the instrument is the currency and
   the reference is the interest period when both rows name one (`CREDIT INT FOR JAN-2025`); a tax row for another
   period does not match;
3. the same match in another currency, when exactly one payment fits.

Several dividend rows of one instrument, account, pay date and currency are one distribution event; one tax row
of that event is shared between them in proportion to their gross amounts and the sharing is noted
(`SHARED_WITHHOLDING`; the total credit does not depend on the split). Interest rows are never shared: a tax row
that fits two interest rows is unresolved.

Anything else is unresolved and reported with the amount that is therefore not in the declared figures:
`UNMATCHED_WHT` (no payment fits; the message says whether the instrument is in the history at all, so "statement
not uploaded" is only said when the payment really is absent), `AMBIGUOUS_MATCH` (several fit),
`UNKNOWN_INCOME_KIND` (the row names neither an instrument nor interest). Nothing is attributed by proximity and
nothing is split by assumption. Unmatched rows that cancel each other out (a booking and its reversal of a
payment that was itself reversed) are only noted.

Control examples (USD at 4 PLN, PLN at 1):

| Input | Expected |
|---|---|
| IB: PLN 100 `CREDIT INT FOR JAN-2025` and PLN -10 `WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025`, no ISIN on either. | Interest 100, withheld and credited 10, flat tax 19, difference 9.00 PLN, nothing unresolved. |
| The same, but the tax row names `FEB-2025`. | Credit 0; `UNMATCHED_WHT` with 15 PLN unresolved; not ready to file. |
| Two interest rows of one day and currency without a period, one tax row without a period (possible in a Flex Query or a generic file). | Credit 0; `AMBIGUOUS_MATCH` naming the 2 candidates. |
| A withholding row `WITHHOLDING ON SOMETHING ELSE` without ISIN. | `UNKNOWN_INCOME_KIND`; the amount is not credited. |

### Refunds and corrections

A refund is netted against the tax it refunds on the payment it points at. A refund larger than that tax leaves
a credit of 0 on the payment and an `EXCESS_REFUND` finding with the excess; the excess is never taken off
another payment, account or instrument, because nothing in the statement proves which other payment it belongs
to. (Earlier versions did offset it, first within the instrument, then anywhere, to keep the total credit at or
below the net tax withheld; that moved money between payments without evidence of a link and is gone.)

An event booked in another year than its payment (an Activity Statement of year N+1 that dates the refund back to
the dividend of year N; a Flex row of year N+1 with the `ActionID` of a year-N dividend) stays with the payment,
so the credit of year N already reflects it, and is reported as `CROSS_YEAR_ADJUSTMENT` for both years with the
amount. **Open question:** whether the tax effect of such a refund belongs to year N (a corrected return) or to
year N+1 is not decided by Pitly. Art. 45 ust. 3a ustawy o PIT (adding back a deduction that was later refunded
in the year of the refund) is the candidate rule for year N+1, and a correction of year N is the alternative; the
IBKR reclassification notice (https://www.interactivebrokers.com/support/faq/content/dividend-income-reclassification-1055108870)
describes a correction of the original document, which is not the same thing as a refund of tax already
credited. Until this is confirmed, the row is attributed, flagged and counted as unresolved; the user confirms
the treatment before filing either year.

Control examples (USD at 4 PLN):

| Input | Expected |
|---|---|
| A: dividend 100 PLN, 15 withheld, 20 refunded. B (another account, another instrument): dividend 100 PLN, 15 withheld. | B keeps 15 credited; A's credit is 0; `EXCESS_REFUND` 5 PLN on account A; not ready to file. |
| 2024: dividend 100 PLN with 15 withheld. 2025 statement: refund of 15 dated back to the 2024 payment, plus another dividend 100 PLN with 15 withheld. | 2025: the other dividend keeps 15 credited, `CROSS_YEAR_ADJUSTMENT` for 2024 and 2025 with 15 PLN. 2024: credit 0, difference 19 PLN. |
| Only the 2025 statement is uploaded. | `UNMATCHED_WHT`: the refund points at a 2024 payment that is not in the history; the other dividend is untouched. |
| Two dividends of 100 USD with 15 USD withheld each; the next year's statement refunds 20 USD dated back to the first. | First credit 0, second 60 PLN, `EXCESS_REFUND` 20 PLN, `CROSS_YEAR_ADJUSTMENT` 80 PLN; not ready to file. |

### Readiness

A finding with `AffectsAmounts` that is at least a warning blocks filing: `Pit38Fields.ReadyToFile` is false and
`UnresolvedAmountPln` sums the money that is not in the declared figures. The dashboard, the PIT-38 page and the
printout show the same banner and the same list; informational notes (`SHARED_WITHHOLDING`,
`MIXED_STREAM_ROUNDING`, `RATE_DATE`, `OVERLAP_SKIPPED`, `CREDIT_METHOD`, `CLASSIFICATION_UNVERIFIED`) do not
block. A session flagged `NeedsReimport` is never ready (`REIMPORT_REQUIRED`).

Control examples (USD at 4 PLN):

| Input | Expected |
|---|---|
| Dividend 100 USD with 15 USD withheld, and a payment in lieu of 10 USD with 3 USD withheld, same instrument and day. | Credit 60 PLN on the dividend. On the payment in lieu 12 PLN withheld, 6 PLN creditable (15% of 40 PLN). |
| The same dividend on two accounts; one statement uploaded twice. | Both accounts count; the repeated statement adds nothing. |
| Two dividends of 100 USD with 15 USD withheld on each; the next year's statement refunds 20 USD dated back to the first one. | Actually withheld 10 USD = 40 PLN, so the credit is 40 PLN, with a warning about the refund. |

### Rounding of section G

Source: the form itself and the Ministry's brochure "Broszura informacyjna do zeznania PIT-38", part G
(https://www.podatki.gov.pl/media/g5ebnm2e/broszura-do-pit-38-za-2025-r.pdf).

1. The flat tax ("Zryczałtowany podatek") is 19% of the income, entered in zł, gr with no rounding rule.
2. The tax paid abroad is entered in zł, gr and may not exceed the flat tax.
3. Only the difference is rounded: to full PLN (art. 63 § 1 O.p.), except the flat tax on interest under
   art. 30a ust. 1 pkt 1-3, which is rounded up to the grosz (art. 63 § 1a O.p.).

So the dividend tax keeps its precision until the difference is taken. Rounding it first would overstate the
difference.

Control example, tax year 2025: dividends 103 PLN, creditable tax paid abroad 15.10 PLN. Flat tax 19.57
(poz. 47), paid abroad 15.10 (poz. 48), difference 4.47, entered as 4 (poz. 49).

**Interpretation:** the form has one field for dividends and interest together and the Ministry gives no rule
for a mix. Pitly rounds each stream by its own rule and adds the results. This is stated as an assumption in
`RoundingPolicy.Current` (version `2025.1`, with the sources above), returned with every result, printed under
section G and noted as `MIXED_STREAM_ROUNDING` whenever a year has both streams.

Control examples: 103 PLN of dividends with 15.10 PLN withheld give a difference of 4 PLN; 100 PLN of bank
interest with 10 PLN withheld give 9.00 PLN; both together give 13.00 PLN.

## Form versions and field numbers

Field numbers are shown only for tax years whose form was checked field by field against the Ministry's PDF.
Links to the PDFs are in the comment at the top of `Pit38FormLayout.cs`.

| Tax year | Form | Prior-year losses | "Razem" przychód / koszty | Flat tax / paid abroad / difference | To pay |
|---|---|---|---|---|---|
| 2021 | PIT-38(15) | poz. 28 | 24 / 25 | 45 / 46 / 47 | 49 |
| 2022, 2023 | PIT-38(16) | poz. 28 | 24 / 25 | 45 / 46 / 47 | 49 |
| 2024 | PIT-38(17) | poz. 28 | 24 / 25 | 45 / 46 / 47 | 49 |
| 2025 | PIT-38(18) | poz. 30 | 26 / 27 | 47 / 48 / 49 | 51 |

Version 18 added a row to section C (exempt income, poz. 24-25), which moved "Razem" and every later field by
two. For any other year the amounts are calculated and shown by field name with a note that the numbering is
not verified. No form for 2026 had been published when this was written.

## PIT/ZG

Art. 30b income is listed per source country: przychód, koszty, dochód and tax paid abroad for section C.3 of
the annex. Dividends and their withholding are shown per country for reference only; they are not entered in
PIT/ZG.

The country of a row is the one the statement names when it has a country column (the generic CSV templates),
otherwise the issuer's country from the ISIN prefix, otherwise "unknown". The named country only changes where
the income is listed in PIT/ZG; the treaty cap on the foreign tax credit keeps following the ISIN, and a named
country that disagrees with the ISIN prefix is reported as a warning (it is correct only for a depositary receipt).

Control example: 120 EUR of Swiss bank interest (540 PLN at 4.50) entered with country `CH` and no ISIN is
listed under Szwajcaria; without the country column it would fall under "Nieznany kraj (brak ISIN)".

## Danina solidarnościowa (art. 30h)

When the capital-gains tax base exceeds 1 000 000 PLN, 4% of the excess is shown as the solidarity levy,
declared on DSF-1. Prior-year losses reduce the base. Art. 30a income is not included. Only income known to
Pitly counts, while the real threshold also covers employment and business income.

## Revolut savings statements

Only the `Interest earned` rows of the consolidated statement are income (art. 30a ust. 1 pkt 3); deposits,
withdrawals, transfers and balances are the user's own money. The summary's "Interest earned" is compared with
the rows and a difference is reported (`SUMMARY_MISMATCH`). The statement names no source country, and the
currency is not one: the interest falls under the unknown country in PIT/ZG unless the user enters it through the
generic income template with a `country`. Revolut pays this interest gross; a tax row, if a statement ever has
one, is kept as an interest withholding event of the same day and currency.

Control example: deposit 100 EUR, interest 0.01 EUR, withdrawal 100.01 EUR give an income of 0.01 EUR, not 100.01
and not 200.02; the same rows entered in the generic income template give the same payments and the same fields.

## What the code does not do

- Instruments other than stocks and ETFs (options, futures, bonds as trades, forex, crypto).
- Decide the tax year of a cross-year refund or re-charge (see "Refunds and corrections").
- Classify securities-lending income and payments in lieu with legal certainty.
- Settlement-date conversion.
- Domestic income reported in PIT-8C (those fields are left for the user).
- Electronic submission.
