# Architecture

How a broker CSV becomes the fields of a PIT-38 return. Read [AGENTS.md](../AGENTS.md) first for the rules;
this document explains where things live and why.

## The pipeline

```
browser: files + options
   │  POST /api/import (multipart/form-data)
   ▼
ImportEndpoints          size limits, form fields, error shapes
   ▼
ImportService            one pass per request:
   1. StatementParserDispatcher   pick the broker parser from the first line
   2. broker parser               CSV text -> ParsedStatement per account (rows in the broker's currency)
   3. StatementMerger             resolve the account of each file, drop rows another file already gave
   4. TaxCalculator               CapitalGainsTaxCalculator + DividendTaxCalculator -> TaxSummary (PLN)
   5. Pit38Fields.FromSummary     losses, rounding, form layout -> the declared fields
   6. EntityMapper + SQLite       store the result rows and totals under a new session id
   ▼
JSON: sessionId, summary, pit38, trades, dividends, availableYears, files
```

Nothing is calculated in the browser. Changing the tax year, the residency date or the account of a file posts
the same files again with different options and replaces the result.

## Backend projects

| Project | Responsibility |
|---|---|
| `Pitly.Core` | Models, merging, all tax arithmetic, the NBP rate client. No knowledge of HTTP, SQLite or any broker's CSV format. |
| `Pitly.Broker.InteractiveBrokers` | Parses the IB Activity Statement (sections `Trades`, `Dividends`, `Withholding Tax`, `Interest`, `Corporate Actions`, opening positions). |
| `Pitly.Broker.Trading212` | Parses the Trading 212 history export (one row per operation, with an `ID` column). |
| `Pitly.Broker.Generic` | Parses the generic trades and income templates (any broker). Strict: numbers with a dot, known values only, errors name the line. Returns one statement per `broker` value through `ParseAll`. |
| `Pitly.Api` | Endpoints, `ImportService`, EF Core entities, `DbSchemaUpgrader`, session cleanup. |
| `Pitly.Tests` | xUnit tests for all of the above, including end-to-end tests through the real API. |

## Core models (`Pitly.Core/Models`)

- `ParsedStatement`: what a parser returns. Raw trades, dividends (`RawDividend` with an `IncomeKind`:
  dividend, payment in lieu, interest), withholding rows, corporate actions, opening positions, plus the
  statement's `AccountId` and `Broker`.
- `Trade`, `RawDividend`, `RawWithholdingTax`, `CorporateAction`, `CarryInPosition` each carry an `AccountId`
  stamped by `StatementMerger`. `Trade` and `RawDividend` also carry the broker's `TransactionId` when the
  export has one, and a `CountryCode` when the statement names the source country (generic CSV); it travels to
  `TradeResult` and `Dividend` and is stored with the row.
- `IStatementParser.Parse` returns one statement; `ParseAll` (default: a list with that one statement) returns
  one per account for formats whose file may hold several, and is what `ImportService` calls.
- `TaxPeriod`: the tax year and the taxable window inside it (the whole year, or from the residency start date).
- `TaxSummary`: totals in PLN, the per-row results, `Warnings` (everything worth telling the user) and
  `ResultWarnings` (the subset that puts the amounts themselves in doubt).
- `Pit38Fields`: the declared values, the `Form` layout for the year (or null), `DataWarnings`, `NeedsReimport`.
- `Pit38FormLayout`: field numbers of each verified form version, keyed by tax year.

## Accounts and merging (`Parsing/StatementMerger.cs`)

Each uploaded file is resolved to one account:

1. the account number in the statement (IB prints it; a generic CSV file names the broker, which is its
   account), upper-cased;
2. otherwise the label the user chose for that file on the dashboard, combined with the broker name;
3. otherwise `"<Broker> (no account number)"`, with an `Account check:` warning when two or more such files
   are still unconfirmed.

Rows are then stamped with that account. Duplicates are detected only inside one account: by the broker's
operation id when there is one, otherwise by the row's content. When the same row appears M times in a new file
and N times in files already merged, `max(0, M − N)` copies are taken, so a repeated or overlapping export adds
nothing and two genuinely identical trades in one file are both kept. A byte-identical file is skipped earlier,
in `ImportService`, by its SHA-256.

## Tax calculation (`Pitly.Core/Tax`)

- `CapitalGainsTaxCalculator` runs once per account: it replays trades and corporate actions in time order up
  to the last day of the tax year, keeps FIFO lots per ISIN (ticker when there is no ISIN), and compares the
  reconstructed position with the opening position the statement reports. Sales inside the taxable window
  become result rows; everything dated after the year is ignored.
- `DividendTaxCalculator` converts each payment to PLN and ties withholding to it. Withholding printed in the
  same row as the payment (Trading 212) stays with that row. Separate rows (IB) are matched by account,
  instrument, date and income kind, preferring the same currency; one row shared by several payments is split
  by PLN value and reported. Refunds and corrections net against the original; the credit is capped at the
  treaty rate of the issuer's country (`TreatyWithholdingRates`, by ISIN prefix).
- `CountryBreakdown` groups sales and income by source country for PIT/ZG: the country the statement names,
  otherwise the ISIN prefix, otherwise "??". The treaty cap on the credit is not affected by the named country.
- `PriorYearLossCalculator` checks each entered loss against the five-year window and the per-year limits.
- `TaxCalculator` runs both calculators and collects the warnings.

The details and the legal sources are in [tax-rules.md](tax-rules.md).

## Exchange rates

`NbpExchangeRateService` asks the NBP API for the mid rate of the last business day before the transaction date
and caches it in memory. It is the only network dependency. Tests replace it with `TestRateService`.

## API

| Method | Path | Notes |
|---|---|---|
| `POST` | `/api/import` | Multipart form. Returns the whole result and a new session id. |
| `GET` | `/api/session/{id}/summary` | Totals; tax figures are taken from the PIT-38 fields. |
| `GET` | `/api/session/{id}/pit38` | The declared fields, form layout, warnings. |
| `GET` | `/api/session/{id}/trades` | Paginated, sortable, filterable. |
| `GET` | `/api/session/{id}/dividends` | All income rows. |
| `GET` | `/api/session/{id}/export/csv` | Transactions as CSV. |
| `GET` | `/api/health` | Liveness. |

Form fields of `POST /api/import`:

| Field | Meaning |
|---|---|
| `files` | One or more CSV statements. |
| `taxYear` | Optional four-digit year; defaults to the latest year found in the files. |
| `residencyStartDate` | Optional date; limits the taxable window when it falls inside the tax year. |
| `priorYearLosses` | Optional JSON array of earlier losses. |
| `accountLabels` | Optional JSON object: file name to account label, for files without an account number. |

Errors:

- `413` with `code` = `file_too_large` or `request_too_large`, plus `fileName`, `maxFileMb`, `maxRequestMb`.
  The frontend turns the code into a localized message.
- `400` with `error`. When the chosen year cannot be calculated, the body also has `availableYears` and
  `taxYear`, so the UI can offer the other years.

JSON is camelCase and enums are strings.

## Upload limits

`UploadLimits` holds the three numbers: 10 MiB per file, 64 MiB per request, 60 files. Kestrel and the multipart
reader use the request limit; `frontend/nginx.conf` and `docker/frontend.nginx.conf` set the same value in
`client_max_body_size`; `frontend/src/api.ts` repeats the megabyte figures for the client-side check.
`UploadLimitTests` fails when the Nginx configs and the API disagree.

## Storage

SQLite through EF Core (`AppDbContext`), file `pitly.db` next to the API, ignored by git. A session stores the
result rows and totals, never the uploaded files. Decimals are stored as text. `SessionCleanupService` deletes
sessions older than `SessionCleanup:TtlHours` (24 by default).

There are no EF migrations. On start `EnsureCreated` builds a new database and `DbSchemaUpgrader` brings an
older one up to date:

- it adds any column from its list that the table lacks;
- it repairs sessions whose `DataVersion` is below `CurrentDataVersion`: totals are rebuilt from the stored rows
  when those rows carry the data, a zero that is correct stays zero, and a session that cannot be rebuilt
  exactly gets `NeedsReimport` instead of an estimate;
- running it twice changes nothing.

## Frontend

- `App.tsx` holds the whole state: the uploaded `File` objects, the options, and the last result. The files stay
  in memory so that the dashboard can recalculate; a page reload loses them.
- `api.ts` posts the import, checks sizes before sending and maps error codes to translation keys
  (`describeImportError`).
- `types.ts` mirrors the API responses. Change it together with the backend record.
- `pit38Form.ts` exposes `formRefs(form)`: the references to form fields that texts use as parameters ("poz. 30"
  for a verified year, the Polish field name otherwise).
- `i18n/` has one module per page with the same keys in `en`, `uk`, `pl`, merged in `translations.ts`. Use
  `useT()` and placeholders such as `{straty}`; missing keys fall back to English, then to the key itself.
- `index.css` contains the print rules. `.no-print` hides an element on paper, `.print-warning` keeps a warning
  box visible in the printout.

## Deployment

`docker-compose.yml` builds the API and an Nginx image that serves the frontend and proxies `/api`. Both ports
are published on `127.0.0.1` only. `compose.production.yml`, `compose.ghcr.yml`, `ops/` and
`.github/workflows/deploy.yml` belong to the upstream project's hosted deployment and are started manually.
