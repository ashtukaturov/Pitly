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

- Foreign dividends are taxed at a flat 19% (art. 30a ust. 1 pkt 4). Credit interest on cash and IB payments in
  lieu of dividends are treated as flat-tax income of the same field. Debit (margin) interest is ignored.
- **Interpretation:** how payments in lieu should be classified is not settled in Polish practice. The UI flags
  them.
- IB lists some payments in lieu inside the `Dividends` section, with the tax row labelled "Payment in Lieu of
  Dividend - US Tax". The parser classifies by the description so that both sides get the same kind; otherwise
  that withholding would match no payment and its credit would be lost.

### Foreign tax credit (art. 30a ust. 9)

- Tax withheld abroad is credited only up to the rate of the double-tax treaty with the issuer's country, taken
  from the ISIN prefix (`TreatyWithholdingRates`: 15% for US, 10% for GB, and so on). Tax withheld above that
  rate is shown but not credited, because it has to be reclaimed abroad.
- Without an ISIN or a treaty rate on file, the statutory cap of 19% applies.
- The credit never exceeds the Polish flat tax on the same stream, and the total credit never exceeds the tax
  actually withheld after refunds and corrections.
- **Limitation:** the ISIN prefix is a proxy. A depositary receipt carries the ISIN of the depositary's country
  while the dividend is taxed in the company's country.

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
for a mix. Pitly rounds each stream by its own rule and adds the results.

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

Art. 30b income is listed per issuer country (ISIN prefix): przychód, koszty, dochód and tax paid abroad for
section C.3 of the annex. Dividends and their withholding are shown per country for reference only; they are not
entered in PIT/ZG.

## Danina solidarnościowa (art. 30h)

When the capital-gains tax base exceeds 1 000 000 PLN, 4% of the excess is shown as the solidarity levy,
declared on DSF-1. Prior-year losses reduce the base. Art. 30a income is not included. Only income known to
Pitly counts, while the real threshold also covers employment and business income.

## What the code does not do

- Instruments other than stocks and ETFs (options, futures, bonds, forex, crypto).
- Settlement-date conversion.
- Domestic income reported in PIT-8C (those fields are left for the user).
- Electronic submission.
