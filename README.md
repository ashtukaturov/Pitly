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

> Supported brokers: **Interactive Brokers** and **Trading 212**.

## Features

- **One-click CSV import** — drag & drop one or more broker statements, the app merges prior years when FIFO history is needed
- **Automatic PLN conversion** — fetches official [NBP mid exchange rates](https://api.nbp.pl/) for every transaction date (last business day before, per Polish tax law)
- **FIFO capital gains calculation** — buy lots are queued per symbol, sells dequeue first-in-first-out to compute cost basis in PLN
- **Dividend tax with foreign credit** — calculates Polish 19% tax and offsets US withholding (typically 15% under the PL-US treaty)
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

### Trading 212

1. Log in to Trading 212
2. Go to **History** (clock icon)
3. Click the **Download** icon
4. Select date range (full tax year) and export as CSV
5. If you became a Polish tax resident mid-year, still export the full calendar-year CSV and set your residency start date in the app
6. Upload the downloaded file in the app
7. Upload earlier yearly CSVs together when you need prior-year FIFO history; stock splits are read from the paired "Stock split close" / "Stock split open" rows

A sample IB statement is provided at [`samples/sample-activity-statement.csv`](samples/sample-activity-statement.csv) for testing.

## How it works

- **Przychód vs koszty** — the sale value is reported gross as przychód (poz. 22); buy and sell commissions are both treated as koszty uzyskania przychodu (poz. 23)
- **Foreign tax credit** — withholding tax is credited only up to the rate set by the double-tax treaty with the issuer's country (derived from the ISIN prefix: 15% for US, 10% for UK, 15% for Germany, ...). Tax withheld above that rate (e.g. 30% US withholding without a W-8BEN) is shown but not credited, since it must be reclaimed abroad. Without an ISIN or treaty rate on file, the statutory 19% cap from art. 30a ust. 9 applies
- **Interest and payments in lieu** — credit interest on cash (IB `Interest` section, Trading 212 `Interest on cash` / `Lending interest`) and IB `Payment In Lieu Of Dividends` are parsed as separate income kinds, taxed at 19% under art. 30a and added to the same PIT-38 field as dividends (poz. 47). Debit (margin) interest is ignored. The treatment of payments in lieu is not settled in Polish practice; confirm with an advisor if material
- **Prior-year losses** — enter each earlier PIT-38 loss (year, amount, part already deducted); Pitly checks the 5-year carry-forward window (art. 9 ust. 3), the 50% per-year limit for pre-2019 losses and the 5 000 000 PLN one-off limit, deducts the oldest losses first, caps the total at this year's income (poz. 30) and warns about losses that expire unused
- **Opening-position check** — the lots reconstructed from your uploads are compared with the positions the tax-year statement reports at the start of the year (IB "Prior Quantity"); a mismatch is flagged on the dashboard, which catches a missing year or an unrecognised corporate action
- **Tax year and residency** — upload every year from your first purchase onwards; the dashboard lets you switch the declared year and set the Polish tax residency start date without re-uploading. A residency date in an earlier year means the whole chosen year is taxable; a date inside the year limits the calculation to sells and dividends from that date. Upload the following year too: IB posts withholding-tax corrections for the previous year in it
- **Overlapping or repeated uploads** — statements can be added in several batches; an identical file is skipped, and rows that another uploaded file already contributed (a partial-year export next to the full year) are skipped and reported, so nothing is counted twice. Withholding-tax corrections that IB books in the following year (dated back to the dividend) net against the original amount
- **PIT/ZG annex** — the PIT-38 page lists art. 30b income per issuer country (ISIN prefix): przychod, koszty, dochod to enter in PIT/ZG section C.3 and tax paid abroad, with dividends and withholding by country shown for reference (they are not entered in PIT/ZG; the credit goes to poz. 48)
- **Languages** — the interface is available in English, Ukrainian and Polish (switcher on the landing page and in the sidebar; the choice is remembered in the browser). Polish form field names stay Polish in every language because they name fields on the official form; messages generated by the backend (statement warnings, trade notes) are English
- **Danina solidarnościowa** — if the capital-gains base (poz. 31) exceeds 1 000 000 PLN, the 4% solidarity levy (art. 30h, declared on DSF-1) is computed and shown. Only income known to Pitly is included; employment and business income also count toward the threshold
- **PIT-38 values are computed on the backend** with decimal arithmetic and returned to the UI as-is; the frontend does not re-derive them

- **NBP exchange rates** — for each transaction, the app currently fetches the official mid rate from the last business day before the **trade** date. Rates are cached in memory.
- **Capital gains (FIFO)** — buy lots are queued per ISIN (falling back to the ticker when the statement has no ISIN). On sell, lots are dequeued first-in-first-out to compute cost basis in PLN. Net gains are taxed at 19%.
- **Dividend tax** — Polish 19% tax minus foreign withholding credit (typically 15% under the PL-US treaty), resulting in ~4% net tax owed.

## API

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/import` | Upload CSV, returns session ID + results |
| GET | `/api/session/{id}/trades` | Paginated trades (sortable, filterable) |
| GET | `/api/session/{id}/dividends` | All dividends for session |
| GET | `/api/session/{id}/summary` | Tax summary totals |
| GET | `/api/session/{id}/pit38` | PIT-38 field values |
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
- Loss carryforward from previous years must be applied manually
- Does not submit PIT-38 electronically — you enter the values yourself
- Always verify results and consult a tax advisor if unsure

## Support

If you find Pitly useful, consider buying me a coffee:

<a href="https://buymeacoffee.com/volodymyr_kovtun" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="48"></a>

## License

[MIT](LICENSE)
