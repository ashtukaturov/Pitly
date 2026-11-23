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
   1. StatementParserDispatcher   pick the parser from the first line (Flex: ClientAccountID; Revolut: section title)
   2. broker parser               CSV text -> ParsedStatement per account (rows in the broker's currency,
                                  every withholding row an event with its dates, ids and raw text)
   3. StatementMerger             resolve the account of each file, drop rows another file already gave
                                  (by id when both rows have one, by content otherwise), stamp the source file
   4. TaxCalculator               CapitalGainsTaxCalculator + DividendTaxCalculator -> TaxSummary (PLN, full precision)
                                  WithholdingMatcher ties every event to its payment over the WHOLE history first;
                                  the tax year is selected afterwards; findings become TaxDiagnostics
   5. Pit38Fields.FromSummary     losses, rounding (once, per field), form layout, readiness -> the declared fields
   6. EntityMapper + SQLite       store the result rows, events, findings, provenance and totals under a session id
   ▼
JSON: sessionId, summary, pit38, trades, dividends, withholdingEvents, availableYears, files
```

Nothing is calculated in the browser. Changing the tax year, the residency date or the account of a file posts
the same files again with different options and replaces the result.

## Backend projects

| Project | Responsibility |
|---|---|
| `Pitly.Core` | Models, merging, all tax arithmetic, the NBP rate client. No knowledge of HTTP, SQLite or any broker's CSV format. |
| `Pitly.Broker.InteractiveBrokers` | Parses the IB Activity Statement (sections `Trades`, `Dividends`, `Withholding Tax`, `Interest`, `Corporate Actions`, opening positions) and the Activity Flex Query CSV (`InteractiveBrokersFlexParser`: sections told apart by their columns, one statement per `ClientAccountID`, corporate-action rows rebuilt in the Activity layout and parsed by the same code). |
| `Pitly.Broker.Revolut` | Parses the consolidated Revolut savings statement: `Interest earned` rows per currency section; the summary only checks them. No account, country or tax withheld. |
| `Pitly.Broker.Trading212` | Parses the Trading 212 history export (one row per operation, with an `ID` column). |
| `Pitly.Broker.Generic` | Parses the generic trades and income templates (any broker). Strict: numbers with a dot, known values only, errors name the line. Returns one statement per `broker` value through `ParseAll`. |
| `Pitly.Api` | Endpoints, `ImportService`, EF Core entities, `DbSchemaUpgrader`, session cleanup. |
| `Pitly.Tests` | xUnit tests for all of the above, including end-to-end tests through the real API. |

## Core models (`Pitly.Core/Models`)

- `ParsedStatement`: what a parser returns. Raw trades, dividends (`RawDividend` with an `IncomeKind`:
  dividend, payment in lieu, interest), withholding rows, corporate actions, opening positions, plus the
  statement's `AccountId`, `Broker`, `ParserVersion` and its structured `Diagnostics`.
- `RawWithholdingTax` is one withholding event: signed amount (positive withheld, negative refunded), the
  reference `Date` (the income date the broker refers it to), `BookingDate` when the statement gives one,
  `SourceStatementYear` (the only sign of a later booking in an Activity Statement), `TransactionId`,
  `ActionId`, `Reference` (the interest period), the source file and the broker's raw type and text. `Kind`
  may be `Unknown` for a row whose income kind the statement does not say; such a row is reported, never matched.
- `TaxDiagnostic`: one finding with a `Code` (`DiagnosticCodes`), a severity, the English message, the source
  file, the account, the affected tax years, the unresolved PLN amount and `AffectsAmounts`; `BlocksFiling`
  is "affects amounts and at least a warning".
- `WithholdingEvent`: one event of the selected year as stored with the session: what was read, its status
  (matched, shared, unresolved), the payment it was tied to and the unresolved amount.
- `Trade`, `RawDividend`, `RawWithholdingTax`, `CorporateAction`, `CarryInPosition` each carry an `AccountId`
  stamped by `StatementMerger`. `Trade` and `RawDividend` also carry the broker's `TransactionId` when the
  export has one, and a `CountryCode` when the statement names the source country (generic CSV); it travels to
  `TradeResult` and `Dividend` and is stored with the row.
- `IStatementParser.Parse` returns one statement; `ParseAll` (default: a list with that one statement) returns
  one per account for formats whose file may hold several, and is what `ImportService` calls.
- `TaxPeriod`: the tax year and the taxable window inside it (the whole year, or from the residency start date).
- `TaxSummary`: totals in PLN at full precision, the per-row results, `Warnings` (everything worth telling
  the user), `ResultWarnings` (the subset that puts the amounts themselves in doubt), `Diagnostics`,
  `WithholdingEvents`, the `CreditMethod` and the `SourceFiles` (name, SHA-256, broker, account, year, parser).
- `Pit38Fields`: the declared values (rounded here and nowhere earlier), the `Form` layout for the year (or
  null), `DataWarnings`, `Diagnostics`, `ReadyToFile`, `UnresolvedAmountPln`, `RoundingPolicy`,
  `CreditMethod`, `NeedsReimport`.
- `CalculationPolicy.cs`: `CreditMethod` (treaty cap by default, statutory cap for comparison),
  `RoundingPolicy.Current` (version, the three rules, which one is an assumption, sources), `RuleVersions`.
- `Pit38FormLayout`: field numbers of each verified form version, keyed by tax year.

## Accounts and merging (`Parsing/StatementMerger.cs`)

Each uploaded file is resolved to one account:

1. the account number in the statement (IB prints it; a generic CSV file names the broker, which is its
   account), upper-cased;
2. otherwise the label the user chose for that file on the dashboard, combined with the broker name;
3. otherwise `"<Broker> (no account number)"`, with an `Account check:` warning when two or more such files
   are still unconfirmed.

Rows are then stamped with that account and with the file they came from. Duplicates are detected only inside
one account. Two rows that both carry the broker's operation id are the same row only when the ids are equal; a
row without an id is the same as an earlier row with the same content (instrument, date, amount), whether or not
that earlier row had an id, so an event uploaded once as an Activity Statement row and once as a Flex Query row
is counted once whichever file comes first. When the same content appears M times in a new file and N times in
files already merged, `max(0, M − N)` copies are taken, so a repeated or overlapping export adds nothing and two
genuinely identical trades in one file are both kept. A byte-identical file is skipped earlier, in
`ImportService`, by its SHA-256. The merger also produces diagnostics (`OVERLAP_SKIPPED`, `ACCOUNT_ASSUMED`).

## Tax calculation (`Pitly.Core/Tax`)

- `CapitalGainsTaxCalculator` runs once per account: it replays trades and corporate actions in time order up
  to the last day of the tax year, keeps FIFO lots per ISIN (ticker when there is no ISIN), and compares the
  reconstructed position with the opening position the statement reports. Sales inside the taxable window
  become result rows; everything dated after the year is ignored.
- `WithholdingMatcher` ties every withholding event to its payment over the whole uploaded history, inside one
  account: first by the broker's own `ActionId`, then by a complete match (kind of income, instrument with ISIN
  against ISIN or ticker against ticker, reference date or interest period, currency), then the same match in
  another currency when exactly one payment fits. Several dividend rows of one instrument, account, pay date and
  currency are one distribution event and share one tax row in proportion to their gross amounts (reported as
  `SHARED_WITHHOLDING`); interest rows are never shared. Anything else is `Unresolved` with a code and a reason.
- `DividendTaxCalculator` converts each payment to PLN, nets the events tied to it per currency (a refund cancels
  the tax it refunds at the original rate; tax paid on another day is converted at the rate of that day and noted
  as `RATE_DATE`), turns a net refund into an `EXCESS_REFUND` finding instead of touching another payment, flags
  an event booked in another year than its payment as `CROSS_YEAR_ADJUSTMENT` for both years, and only then
  selects the payments of the tax period. The credit is capped at the treaty rate of the issuer's country
  (`TreatyWithholdingRates`, by ISIN prefix) for dividends under `CreditMethod.TreatyCap`, at 19% for interest
  always, and at 19% for everything under `CreditMethod.StatutoryCap`. Same-row withholding (Trading 212, generic
  CSV) stays with its row and needs no matching.
- `CountryBreakdown` groups sales and income by source country for PIT/ZG: the country the statement names,
  otherwise the ISIN prefix, otherwise "??". The treaty cap on the credit is not affected by the named country.
- `PriorYearLossCalculator` checks each entered loss against the five-year window and the per-year limits.
- `TaxCalculator` runs both calculators, turns the position check and the account assumption into diagnostics,
  adds the `MIXED_STREAM_ROUNDING` note when a year has both streams, and builds the summary without rounding
  anything.

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
result rows, the withholding events of its year (`WithholdingEvents` table), the findings (`DiagnosticsJson`),
the provenance of the upload (`SourceFilesJson`: name, SHA-256, broker, account, year, parser version),
`RulesVersion`, `FormVersion`, `CreditMethod` and the totals at full precision, never the uploaded files.
Decimals are stored as text. `SessionCleanupService` deletes sessions older than `SessionCleanup:TtlHours` (24
by default).

There are no EF migrations. On start `EnsureCreated` builds a new database and `DbSchemaUpgrader` brings an
older one up to date:

- it adds any column from its list that the table lacks;
- it creates the `WithholdingEvents` table when it is missing;
- it repairs sessions whose `DataVersion` is below `CurrentDataVersion` (3): totals are rebuilt from the stored
  rows at full precision when those rows carry the data, a zero that is correct stays zero, and a session that
  cannot be rebuilt exactly, or whose stored warnings show that its rows came from the superseded matching (an
  excess refund offset elsewhere, an ambiguous tax row split), gets `NeedsReimport` instead of an estimate. A
  flagged session answers with a `REIMPORT_REQUIRED` finding and `ReadyToFile = false`; nothing in it is
  recomputed or rewritten;
- running it twice changes nothing.

## Frontend

- `App.tsx` holds the whole state: the uploaded `File` objects, the options, and the last result. The files stay
  in memory so that the dashboard can recalculate; a page reload loses them.
- `api.ts` posts the import, checks sizes before sending and maps error codes to translation keys
  (`describeImportError`).
- `types.ts` mirrors the API responses. Change it together with the backend record.
- `components/DiagnosticsPanel.tsx` renders the findings: the readiness banner with the unresolved total, the
  blocking findings with their translated code, account, file, years and amount, and the informational notes
  behind a toggle (expanded in print). The dashboard and the PIT-38 page both use it; the PIT-38 page also prints
  the rounding policy and the credit method under section G, and the Dividends page lists the withholding
  events of the year.
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
