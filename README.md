<p align="center">
  <img src="frontend/public/favicon.svg" width="80" height="80" alt="Pitly logo">
</p>

<h1 align="center">Pitly</h1>

<p align="center">
  PIT-38 tax calculator for foreign stock investors in Poland.<br>
  Upload your broker statement CSV and get exact PIT-38 field values in seconds.
</p>

<p align="center">
  <a href="https://pitly.xyz"><strong>pitly.xyz</strong></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/React-19-61DAFB?logo=react" alt="React 19">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
</p>

---

## Why this exists

If you invest through a foreign broker from Poland, every year by **April 30** you must file a **PIT-38** tax declaration. This means manually converting dozens (or hundreds) of transactions to PLN using the correct NBP exchange rate, calculating capital gains with FIFO, figuring out dividend tax with foreign withholding credits, and filling in the right fields on the form. Doing this by hand is tedious, time-consuming, and error-prone.

**Pitly automates the entire process.** Upload your broker statement CSV and get the exact values to enter in your PIT-38 — in seconds, not hours.

> Supported statements: **Interactive Brokers** (Activity Statement and Flex Query CSV), **Trading 212**, **Revolut** savings statements (interest). Any other broker works through the two **generic CSV templates** (trades and income), the format the PIT-38 assistant uses.

## Features

- **One-click CSV import** — drag & drop one or more broker statements, the app merges prior years when FIFO history is needed
- **Automatic PLN conversion** — fetches official [NBP mid exchange rates](https://api.nbp.pl/) for every transaction date (last business day before, per Polish tax law)
- **FIFO capital gains calculation** — buy lots are queued per symbol, sells dequeue first-in-first-out to compute cost basis in PLN
- **Dividend tax with foreign credit** — calculates Polish 19% tax and offsets US withholding (typically 15% under the PL-US treaty)
- **Every withholding row explained** — tax withheld, refunded or re-charged is tied to its payment over the whole uploaded history; what cannot be tied is listed with its amount and the return is marked "not ready" until it is resolved
- **Ready-to-use PIT-38 values** — generates exact amounts for fields C.20–C.24, D.25–D.26, E.27–E.28
- **Interactive dashboard** — summary cards, monthly bar chart, tax breakdown pie chart
- **Detailed transaction tables** — all trades and dividends with PLN conversion, filterable by symbol, sortable by any column
- **CSV export** — download your processed transactions for your records

## Screenshots

**Import** — drag & drop your CSV

![Import page](docs/screenshots/01-import.png)

**Dashboard** — capital gains, dividends, tax owed at a glance

![Dashboard](docs/screenshots/02-dashboard.png)

**Transactions** — every trade with PLN conversion, filterable and sortable

![Transactions](docs/screenshots/03-transactions.png)

**Dividends** — dividend payments with US withholding and net Polish tax

![Dividends](docs/screenshots/04-dividends.png)

**PIT-38 Guide** — pre-filled field values ready to enter in e-Urzad Skarbowy

![PIT-38 Guide](docs/screenshots/05-pit38.png)

## Quick start with Docker

If you don't have Docker installed, download and install [Docker Desktop](https://www.docker.com/products/docker-desktop/) for your OS. Make sure it's running before proceeding.

```bash
docker-compose up --build
```

Open [http://localhost:3000](http://localhost:3000) in your browser. That's it.

When you're done, stop and remove the containers:

```bash
docker-compose down
```

## Manual setup

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 20+](https://nodejs.org/)

```bash
# Terminal 1 — Backend API
cd backend/src/Pitly.Api
dotnet run --launch-profile https
# https://localhost:7001

# Terminal 2 — Frontend
cd frontend
npm install && npm run dev
# http://localhost:5173
```

## How to export your broker statement

### Interactive Brokers

1. Log in to [IB Client Portal](https://www.interactivebrokers.com/sso/Login)
2. Go to **Performance & Reports > Statements**
3. Click **Activity** statement
4. Set the period to the full tax year (e.g. January 1 – December 31, 2024)
5. Select format: **CSV**
6. Upload the downloaded file in the app
7. If you became a Polish tax resident mid-year, still upload the full calendar-year CSV and set your residency start date in the app
8. If you sold shares bought in earlier years, upload those earlier yearly CSVs together so Pitly can reconstruct FIFO lots and apply corporate actions (splits, ISIN changes, mergers, spin-offs)

### Interactive Brokers: Flex Query CSV

1. In Client Portal go to **Performance & Reports > Flex Queries** and create an Activity Flex Query
2. Include the sections **Trades**, **Cash Transactions** (detail level, not summary) and **Corporate Actions**; format CSV, dates as `yyyyMMdd`
3. Run it for the full tax year and upload the file. Each section has its own header row starting with `ClientAccountID`; a file with several accounts gives one statement per account
4. The Flex Query carries the row ids (`TransactionID`, `ActionID`) that tie a dividend to its tax and to a refund booked in a later year. Uploaded next to the Activity Statement of the same period, every event is counted once
5. Only the CSV flavour is read, not the XML one. Trades of asset classes other than stocks and cash rows of unknown types are listed as left out, never counted

### Revolut: savings statement CSV

1. In the Revolut app open the savings account, choose **Statement**, the tax year and the CSV format (the consolidated statement with a `Summary for Savings Accounts - EUR` and a `Transactions for Savings Accounts - EUR` section per currency)
2. Only the rows labelled `Interest earned` are income; deposits, withdrawals, transfers and balances are your own money and are skipped. The summary is used only to check that the interest rows add up to its "Interest earned"; a difference is reported
3. The statement names no account, no country and no tax withheld. The interest is listed under an unknown country in PIT/ZG; to name a country, enter the rows through the generic income template instead
4. Revolut changes this export from time to time: a row of a type Pitly does not know is listed as left out with a warning, and a value it cannot read stops the import with its line number

### Trading 212

1. Log in to Trading 212
2. Go to **History** (clock icon)
3. Click the **Download** icon
4. Select date range (full tax year) and export as CSV
5. If you became a Polish tax resident mid-year, still export the full calendar-year CSV and set your residency start date in the app
6. Upload the downloaded file in the app
7. Upload earlier yearly CSVs together when you need prior-year FIFO history; stock splits are read from the paired "Stock split close" / "Stock split open" rows

### Any other broker: the generic CSV templates

For a broker without a native parser, fill in the two generic templates (the same format the PIT-38 assistant uses) and upload them like a statement. The import page offers them for download; the files are [`frontend/public/templates/generic-trades-template.csv`](frontend/public/templates/generic-trades-template.csv) and [`frontend/public/templates/generic-income-template.csv`](frontend/public/templates/generic-income-template.csv).

```
broker,tx_id,direction,symbol,isin,country,currency,price,quantity,amount,commission,operation_datetime,settlement_date
DEGIRO,6B439AA86919,BUY,AAPL,US0378331005,US,USD,150.00,10,1500.00,1.50,2024-03-15 10:30:00,2024-03-17
```

```
broker,tx_id,income_type,symbol,isin,country,currency,gross_amount,wht_amount,operation_datetime,settlement_date
DEGIRO,6FE97C3181FB,DIVIDEND,AAPL,US0378331005,US,USD,25.00,3.75,2024-05-10 00:00:00,2024-05-10
SWISSQUOTE,90F820E9CDFD,INTEREST,SAVINGS-EUR,,CH,EUR,120.00,22.80,2024-12-31 00:00:00,2024-12-31
```

- Comma-separated, a dot as the decimal separator and no thousands separator; dates as `YYYY-MM-DD` or `YYYY-MM-DD HH:MM:SS`. Keep the header row; column order does not matter. A value that cannot be read stops the import with the line number
- `direction` is `BUY` or `SELL`; `income_type` is `DIVIDEND`, `INTEREST` or `PAYMENT_IN_LIEU`. Amounts are positive and in the row's `currency` (`GBX` is converted to GBP): `amount` is the gross value of the trade (filled in from `price` x `quantity` when empty, reported when it disagrees with them), `commission` the fee of that trade, `gross_amount` the payment before tax and `wht_amount` the tax withheld on it
- `broker` is the account. Rows with the same broker in the trades file and in the income file are one account; different brokers never share FIFO lots or withholding, also inside one file. Two accounts at one broker need two names (`DEGIRO-1`, `DEGIRO-2`)
- `operation_datetime` is the date of the NBP rate; `settlement_date` is checked for format but not used. `tx_id` lets a repeated or overlapping upload be recognised
- `country` is the source country for the PIT/ZG annex (needed for interest, which has no ISIN); the treaty cap on the foreign tax credit still follows the ISIN. A country that disagrees with the ISIN prefix is reported, since only a depositary receipt should do that
- `isin` is optional for trades (lots are then keyed by symbol) and required for nothing else; interest rows may leave `symbol` empty
- Not covered by the templates: corporate actions, opening positions, reversed or negative payments

A sample IB statement is provided at [`samples/sample-activity-statement.csv`](samples/sample-activity-statement.csv) for testing.

## How it works

- **Przychód vs koszty** — the sale value is reported gross as przychód (row "Inne przychody"); buy and sell commissions are both treated as koszty uzyskania przychodu
- **Foreign tax credit** — withholding tax is credited only up to the rate set by the double-tax treaty with the issuer's country (derived from the ISIN prefix: 15% for US, 10% for UK, 15% for Germany, ...). Tax withheld above that rate (e.g. 30% US withholding without a W-8BEN) is shown but not credited, since it must be reclaimed abroad. Without an ISIN or treaty rate on file, the statutory 19% cap from art. 30a ust. 9 applies
- **Interest and payments in lieu** — credit interest on cash (IB `Interest` section, Trading 212 `Interest on cash` / `Lending interest`) and IB payments in lieu of dividends (the `Payment In Lieu Of Dividends` section, or rows labelled "Payment in Lieu of Dividend" inside the `Dividends` section) are parsed as separate income kinds, taxed at 19% under art. 30a and added to the same PIT-38 field as dividends. Debit (margin) interest is ignored. The treatment of payments in lieu is not settled in Polish practice; confirm with an advisor if material
- **Prior-year losses** — enter each earlier PIT-38 loss (year, amount, part already deducted); Pitly checks the 5-year carry-forward window (art. 9 ust. 3), the 50% per-year limit for pre-2019 losses and the 5 000 000 PLN one-off limit, deducts the oldest losses first, caps the total at this year's income and warns about losses that expire unused
- **Opening-position check** — the lots reconstructed from your uploads are compared with the positions the tax-year statement reports at the start of the year (IB "Prior Quantity"); a mismatch is flagged on the dashboard, which catches a missing year or an unrecognised corporate action
- **Tax year and residency** — upload every year from your first purchase onwards; the dashboard lets you switch the declared year and set the Polish tax residency start date without re-uploading. A residency date in an earlier year means the whole chosen year is taxable; a date inside the year limits the calculation to sells and dividends from that date. Nothing dated after the end of the chosen year takes part in its calculation, so a later sale with missing purchase history does not block an earlier year (the error then offers the other years found in the upload). Upload the following year too: IB posts withholding-tax corrections there, dated back to the dividend
- **Overlapping or repeated uploads** — statements can be added in several batches; an identical file is skipped, and rows that another uploaded file of the same broker account already contributed (a partial-year export next to the full year, an Activity Statement next to a Flex Query) are skipped and reported, so nothing is counted twice. Two rows that both carry the broker's operation id (Trading 212 `ID`, generic CSV `tx_id`, Flex `TransactionID`) are the same row only when the ids are equal; a row without an id matches an earlier row by its content. Rows of different accounts never match, even with the same id, and an empty account id never makes two files one account
- **Withholding events** — every tax withheld, refunded or re-charged is one event with its account, dates, ids, currency and the broker's text. Events are tied to payments over the whole uploaded history, before the tax year is selected, in this order: the broker's own link (`ActionID`), then a complete match on account, kind of income, instrument (ISIN against ISIN, ticker against ticker), reference date and currency, then the same match in another currency when exactly one payment fits. Tax withheld on credit interest, which IB books without a ticker or ISIN (`WITHHOLDING @ 10% ON CREDIT INT FOR JAN-2025`), is tied to the interest of the same account, currency and period. A row that fits no payment, or several interest payments, is left unresolved with a reason; it is never attributed by proximity and never split by assumption
- **Refunds and corrections** — a refund is netted against the tax it refunds on the payment it points at, in the currency of the tax. A refund larger than that tax leaves a credit of 0 on the payment and an unresolved excess; the excess is never taken off another payment, account or instrument. A refund whose payment is in an earlier year stays with that payment (so the earlier year's credit already reflects it) and is reported as a cross-year adjustment for both years, because whether its tax effect belongs to the payment's year or to the booking year is a question of policy Pitly does not decide
- **Ready to file or not** — each finding carries a code (`CROSS_YEAR_ADJUSTMENT`, `UNMATCHED_WHT`, `AMBIGUOUS_MATCH`, `EXCESS_REFUND`, `UNKNOWN_INCOME_KIND`, `UNSUPPORTED_ROW`, `SUMMARY_MISMATCH`, `POSITION_CHECK`, `ACCOUNT_ASSUMED`, `REIMPORT_REQUIRED`, ...), the account and file it comes from, the tax years it touches and the amount that is not in the declared figures because of it. While any finding affects the amounts, the dashboard and the PIT-38 page say "not ready to file" with the total of unresolved money; informational notes (`SHARED_WITHHOLDING`, `MIXED_STREAM_ROUNDING`, `RATE_DATE`, `OVERLAP_SKIPPED`, `CREDIT_METHOD`) are listed separately. The Dividends page lists every withholding event of the year with its status and the payment it was tied to
- **Full precision until the form field** — totals in the summary and in the stored session keep every decimal; rounding happens once, in the PIT-38 fields, by the rule of each field. Example: 0.01 EUR of interest at a rate of 10.51 is 0.1051 PLN, 19% of it 0.019969 PLN, entered as 0.02 PLN; rounding the income to 0.11 PLN first would give 0.03 PLN
- **Rate of the day the tax was paid** — tax withheld on the day of the payment is converted at the payment's rate. Tax paid or re-charged on another day is converted at the rate of the day before that day (art. 11a ust. 3 ustawy o PIT) and a note says so; a refund in the same currency cancels the tax it refunds at the original rate, so a full refund leaves exactly zero
- **Interest never borrows a dividend treaty rate** — the treaty rates on file are dividend rates. Foreign tax on interest (credit interest, bond interest) is credited up to 19% of the income (art. 30a ust. 9), whatever ISIN the row carries, and a note says the classification was not verified. The legal classification of securities-lending interest and payments in lieu is not established by Pitly either; both are flagged
- **Credit method** — by default the foreign tax credit on dividends is capped at the treaty rate of the issuer's country, the position of the Ministry of Finance and KIS. For comparison with the line of NSA rulings that read art. 30a ust. 9 literally (tax actually paid, up to 19%), the import page offers the statutory cap as an explicit choice; it is recorded with the session, shown with the result and never applied to an older session silently
- **Rounding policy with a version** — the result carries the rounding policy (version `2025.1`) with the dividend rule, the interest rule, the rule for a mix of both and its sources, and states that the mixed rule is an assumption of Pitly, not of the Ministry
- **PIT/ZG annex** — the PIT-38 page lists art. 30b income per source country (the `country` column of a generic CSV file when there is one, otherwise the ISIN prefix): przychod, koszty, dochod to enter in PIT/ZG section C.3 and tax paid abroad, with dividends and withholding by country shown for reference (they are not entered in PIT/ZG; the credit goes to "Podatek zapłacony za granicą" in section G)
- **Languages** — the interface is available in English, Ukrainian and Polish (switcher on the landing page and in the sidebar; the choice is remembered in the browser). Polish form field names stay Polish in every language because they name fields on the official form; messages generated by the backend (statement warnings, trade notes) are English
- **Form version by year** — field numbers are shown only for tax years whose form was checked field by field against the Ministry's PDF: PIT-38(15) for 2021, (16) for 2022-2023, (17) for 2024 and (18) for 2025. Versions 15-17 share one numbering; version 18 added a row to section C, which moved "Razem" and every later field by two (prior-year losses are poz. 28 for 2024 and poz. 30 for 2025). For any other year the amounts are still calculated and shown by field name, with a note that the numbering is not verified
- **Rounding** — following the form and the Ministry's brochure (part G), the flat tax on foreign dividends and the tax paid abroad are entered in zł, gr and only their difference is rounded: to full PLN (art. 63 § 1 O.p.), or up to the grosz for interest (art. 30a ust. 1 pkt 1-3, § 1a). Example: 103 PLN of dividends give 19.57 PLN of tax, 15.10 PLN paid abroad, difference 4.47 → 4 PLN. The Ministry gives no rule for a mix of both streams in the one field; Pitly rounds each stream by its own rule and adds them
- **Danina solidarnościowa** — if the capital-gains base ("Podstawa obliczenia podatku") exceeds 1 000 000 PLN, the 4% solidarity levy (art. 30h, declared on DSF-1) is computed and shown. Only income known to Pitly is included; employment and business income also count toward the threshold
- **PIT-38 values are computed on the backend** with decimal arithmetic and returned to the UI as-is; the frontend does not re-derive them. The dashboard total, its chart and the printout all show the PIT-38 fields (after the prior-year loss deduction), never a separate sum

- **Warnings next to the result** — checks that put the amounts themselves in doubt (opening positions that do not match the uploaded history, withholding that could not be attributed, accounts that were only assumed) are repeated at the top of the PIT-38 page and in its printout, also when a saved session is opened again
- **Upload size** — up to 10 MB per file and 64 MB per upload; the same total is set in the API and in the Nginx that fronts it in Docker (`client_max_body_size`), and a test keeps them in step
- **Sessions saved by an older version** — on start the database is upgraded in place; totals are rebuilt from the stored rows at full precision when those carry the data, and a session that cannot be rebuilt exactly, or whose rows were produced by the superseded matching (an excess refund spread over other payments, an ambiguous tax row split), is flagged for re-import instead of being guessed. No stored declaration is rewritten and no correction (korekta) is created
- **Provenance** — a session stores the name, SHA-256, broker, account, year and parser version of every uploaded file, the version of the rules and of the form layout it was computed with, the credit method, every withholding event of the year with its raw type and description, and the structured findings. `GET /api/session/{id}/withholding-events` returns the events; a reopened session gives the same fields, findings and readiness as the import did

- **NBP exchange rates** — for each transaction, the app currently fetches the official mid rate from the last business day before the **trade** date. Rates are cached in memory.
- **Capital gains (FIFO)** — buy lots are queued per broker account and ISIN (falling back to the ticker when the statement has no ISIN): a purchase on one account is never the cost of a sale on another, and a split reported by two statements changes each account's position once. An IB statement names its account and a generic CSV file names it in the `broker` column; a Trading 212 export does not, so when several such files are uploaded the dashboard asks which of them belong to one account (until you answer they are treated as one account and a warning says so). On sell, lots are dequeued first-in-first-out to compute cost basis in PLN. Net gains are taxed at 19%.
- **Dividend tax** — Polish 19% tax minus foreign withholding credit (typically 15% under the PL-US treaty), resulting in ~4% net tax owed.

## API

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/import` | Upload CSV, returns session ID + results |
| GET | `/api/session/{id}/trades` | Paginated trades (sortable, filterable) |
| GET | `/api/session/{id}/dividends` | All dividends for session |
| GET | `/api/session/{id}/summary` | Tax summary totals |
| GET | `/api/session/{id}/pit38` | PIT-38 field values, findings, readiness, rounding policy |
| GET | `/api/session/{id}/withholding-events` | Every withholding event of the session's year with its attribution |
| GET | `/api/session/{id}/export/csv` | Export transactions as CSV |

## Tech stack

**Backend:** .NET 10, C#, Minimal API, EF Core, SQLite
**Frontend:** React 19, TypeScript, Vite, Tailwind CSS v4, Recharts
**External:** NBP API (no authentication required)

## Limitations

- Only **Stocks** trades — forex, options, futures, bonds, and crypto are not supported
- Only **USD** and **EUR** currencies
- Capital gains are currently calculated using the **trade date**, not the **settlement date**. This is a commonly used and generally safe practical approach for most same-year retail cases, but some stricter interpretations use settlement date instead, especially around year-end and exchange-holiday edge cases. See: [Investor.gov — New T+1 settlement cycle](https://www.investor.gov/newT1settlement-cycle)
- If you sell a position opened in earlier years, upload those earlier yearly statements too so FIFO cost basis can be reconstructed correctly
- The generic CSV templates carry no corporate actions and no opening positions: splits, mergers and spin-offs at such a broker have to be reflected in the rows you enter, and the opening-position check does not run for them
- The XML flavour of the IB Flex Query is not read. A Flex Query dates a refund of last year's tax on the day it was booked while the Activity Statement dates it back to the dividend, so the same refund uploaded in both formats is two events (the result then shows an excess refund to check); upload one format per period for corrections
- The Revolut parser was written from the layout of the consolidated savings statement, not against every version of the export: an unknown row type is reported, not guessed, and a layout change may need the generic income template
- Which tax year a cross-year refund or re-charge belongs to (a corrected return for the payment's year, or an event of the booking year) is not decided by Pitly; such rows are attributed to their payment and flagged for both years
- Losses from previous years are deducted only when you enter them on the import page; Pitly does not read them from earlier returns
- Does not submit PIT-38 electronically — you enter the values yourself
- Always verify results and consult a tax advisor if unsure

## Development

```bash
dotnet test backend/tests/Pitly.Tests/Pitly.Tests.csproj --nologo
```

```bash
npm run lint --prefix frontend
```

```bash
npm run build --prefix frontend
```

The tests need no network: exchange rates are fixed and statements are synthetic.

- [AGENTS.md](AGENTS.md): rules and commands for contributors and AI coding agents (`CLAUDE.md`, `GEMINI.md` and `.github/copilot-instructions.md` point to it)
- [docs/architecture.md](docs/architecture.md): how a CSV becomes PIT-38 fields
- [docs/tax-rules.md](docs/tax-rules.md): the tax rules implemented, their sources and control examples
- [docs/testing.md](docs/testing.md): what the tests pin and how to add one
- [docs/recipes.md](docs/recipes.md): step lists for common changes

## Support

If you find Pitly useful, consider buying me a coffee:

<a href="https://buymeacoffee.com/volodymyr_kovtun" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="48"></a>

## License

[MIT](LICENSE)
