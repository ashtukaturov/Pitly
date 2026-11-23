# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex / ChatGPT, Gemini CLI, GitHub Copilot, Cursor and others)
working in this repository. `CLAUDE.md`, `GEMINI.md` and `.github/copilot-instructions.md` only point here, so
this file is the one to keep up to date.

## What this project is

Pitly turns broker CSV statements (Interactive Brokers, Trading 212, and the generic trades and income templates
for any other broker) into the values a Polish tax resident enters in the PIT-38 return and its PIT/ZG annex. A .NET 10 Minimal API does all the tax arithmetic; a React 19 app
uploads the files and shows the result in English, Ukrainian and Polish.

The numbers end up in a real tax return. A change that looks right but is off by one grosz, one field number or
one account is a bug, so every behaviour below is pinned by a test with a hand-computed example.

## Rules that are easy to break

1. **No real financial data in the repository or in any outside service.** Never commit or upload real broker
   statements, account numbers, `pitly.db` or screenshots with real figures. Tests use the builders in
   `SyntheticStatements.cs`. If the owner gives you real statements, post them only to the API on `localhost`.
2. **Money is `decimal`, and it is rounded once.** No `double`. Intermediate values keep full precision; rounding
   happens only in `Pit38Fields.FromSummary`, where the rule of the form field requires it.
3. **The backend's `Pit38Fields` is the only source of declared amounts.** The frontend shows those fields and
   never re-derives a tax figure. The dashboard, the PIT-38 page and the printout must always agree.
4. **No fixed form field numbers in code or texts.** Field numbers ("poz. 30") depend on the form version of the
   tax year. Take them from `Pit38FormLayout` on the backend and `formRefs()` on the frontend. A year without a
   verified form shows field names, never guessed numbers.
5. **Broker accounts are isolated.** FIFO lots, splits, opening positions, duplicate detection and withholding
   matching all work inside one account; results are summed afterwards. An empty account id does not prove that
   two files belong to the same account. A generic CSV file may list several brokers: each is its own account and
   its own statement (`IStatementParser.ParseAll`).
6. **Three interface languages.** Every translation key exists in `en`, `uk` and `pl`. Polish names of form
   fields stay Polish in every language. Messages produced by the backend are English.
7. **A tax rule needs a source.** When you change how something is taxed, cite the statute article or the
   Ministry of Finance document in a code comment and add a test whose expected value you computed by hand.
8. **Never guess when data is missing.** Incomplete purchase history, an unmatched withholding row or an old
   stored session that cannot be rebuilt exactly produce a warning or a re-import flag, not an estimate.
9. **Local deployment stays local.** Docker publishes ports on `127.0.0.1` only, and the upload limit is the same
   in the API and in both Nginx configs. `UploadLimitTests` checks both.
10. **Git.** Commit, push and open pull requests only when the person you work for asks.

## Commands

Run from the repository root.

| Task | Command |
|---|---|
| Backend tests | `dotnet test backend/tests/Pitly.Tests/Pitly.Tests.csproj --nologo` |
| One test class | `dotnet test backend/tests/Pitly.Tests/Pitly.Tests.csproj --filter FullyQualifiedName~EndToEndImportTests` |
| API build | `dotnet build backend/src/Pitly.Api/Pitly.Api.csproj -c Release --nologo` |
| Run the API | `dotnet run --project backend/src/Pitly.Api --launch-profile https` (https://localhost:7001) |
| Frontend install | `npm ci --legacy-peer-deps --prefix frontend` |
| Run the frontend | `npm run dev --prefix frontend` (http://localhost:5173, proxies `/api` to the API) |
| Frontend lint | `npm run lint --prefix frontend` |
| Frontend build | `npm run build --prefix frontend` |
| Everything in Docker | `docker-compose up --build` (http://localhost:3000) |

The tests need no network and no Docker: exchange rates are fixed and each test gets its own temporary SQLite
file. On Windows, stop a running API before building the same configuration, because the running process locks
its DLLs.

## Before you say a change is done

- `dotnet test` passes, the API builds in Release without warnings, `npm run lint` and `npm run build` pass.
  This is what CI runs (`.github/workflows/ci.yml`).
- A change in tax behaviour has a test with a hand-computed expected value and a line in the README section
  "How it works".
- A UI change was looked at in the browser, in all three languages when it adds or moves text. There are no
  frontend tests, so this is the only check.
- State plainly what you could not verify (for example anything that needs Docker when Docker is not installed).

## Layout

```
backend/
  src/Pitly.Core/                       tax logic, no I/O except the NBP rate client
    Models/                             ParsedStatement, Trade, Pit38Fields, Pit38FormLayout, TaxSummary
    Parsing/StatementMerger.cs          accounts, duplicate rows, merging several files
    Tax/                                FIFO, dividends and withholding, prior-year losses, treaty rates
    Services/NbpExchangeRateService.cs  official NBP mid rates
  src/Pitly.Broker.InteractiveBrokers/  IB Activity Statement parser and corporate actions
  src/Pitly.Broker.Trading212/          Trading 212 history export parser
  src/Pitly.Broker.Generic/             generic trades and income CSV templates (one account per broker column)
  src/Pitly.Api/                        endpoints, import service, SQLite storage, schema upgrade
  tests/Pitly.Tests/                    xUnit: unit tests and end-to-end tests through the real API
frontend/
  src/pages/                            Import, Dashboard, Transactions, Dividends, PIT-38
  src/i18n/                             one module per page, each with en / uk / pl
  src/pit38Form.ts                      how texts refer to form fields
  src/api.ts, src/types.ts              API client and the response types
docker/, docker-compose.yml             local run, ports on 127.0.0.1
docs/                                   the documents listed below
```

## Read more

- [docs/architecture.md](docs/architecture.md): how a CSV becomes PIT-38 fields, the API, storage, the frontend.
- [docs/tax-rules.md](docs/tax-rules.md): every tax rule the code implements, its source and a control example.
- [docs/testing.md](docs/testing.md): what each test file pins, the test helpers, how to write a new test.
- [docs/recipes.md](docs/recipes.md): step lists for common changes (new broker, new form year, new text,
  new database column, new warning).
- [README.md](README.md): user-facing description, broker export instructions, limitations.
