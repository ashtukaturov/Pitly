# Testing

All automated tests are in `backend/tests/Pitly.Tests` (xUnit). They need no network and no Docker.

```bash
dotnet test backend/tests/Pitly.Tests/Pitly.Tests.csproj --nologo
```

One class or one test:

```bash
dotnet test backend/tests/Pitly.Tests/Pitly.Tests.csproj --filter FullyQualifiedName~EndToEndImportTests
```

```bash
dotnet test backend/tests/Pitly.Tests/Pitly.Tests.csproj --filter FullyQualifiedName~Import_DividendTaxIsRoundedOnlyInTheDifference
```

The frontend has no tests. `npm run lint` and `npm run build` (which type-checks) are its automated checks;
everything visual is checked in the browser.

## What each file pins

| File | What breaks it |
|---|---|
| `EndToEndImportTests.cs` | Anything between a CSV and the final PIT-38 fields: per-account FIFO and splits, duplicate uploads, withholding credit, the loss deduction, rounding of section G, the year cut-off, form numbering per year, warnings surviving a reopened session. |
| `WithholdingAttributionTests.cs` | How withholding is tied to a payment: same-row withholding, other currencies, no crossing between accounts, an ambiguous row left unresolved, interest matched by period, unmatched rows, an excess refund that is never offset elsewhere, account labels, operation ids. |
| `WithholdingReconciliationTests.cs` | The defects of the 2025 audit, end to end: tax on credit interest without an ISIN, a tax row for another interest period, a row of unknown kind, an excess refund on account A that leaves account B alone, a refund booked next year that stays with last year's payment (both tax years), a refund whose payment is not uploaded, dividend and interest rounding kept apart, 0.01 EUR of interest not rounded before the tax. |
| `CreditRulesTests.cs` | The rate of the day the tax was paid, a refund netted at the original rate, interest never taking a dividend treaty rate, the statutory-cap comparison method (chosen, recorded, reopened, rejected when invalid), the rounding policy and the source-file provenance in the response. |
| `RecheckTests.cs` | The defects of the 23 November 2026 recheck: the generic `account_id` column (per-account FIFO, assumed accounts flagged, labels, repeated uploads), one Flex `ActionID` over a dividend and a payment in lieu, the three Flex dates and a correction reported next year, the Revolut `Money in`/`Money out` and vertical-summary layouts and negative rows, Activity orders against Flex executions and netted tax rows against single rows (agreeing, conflicting, another broker never counting as coverage), each withholding at its own rate and refunds netted against the withholding they reverse, art. 63 § 1a for the base and the tax of interest per payment and the zł, gr precision of every field after a reload, an unknown treaty rate blocking instead of falling back, lending income and payments in lieu blocking until classified. |
| `SecondRecheckTests.cs` | The findings of the 24 November 2026 recheck: a refund booked by the Activity Statement in the next year under the broker's text of the distribution (tied to its payment, the same fields as the Flex Query and their mix; several payments with the text ambiguous and flagged for their year; no text flagged for every earlier year; a text no payment carries flagged for its own year only; two same-day distributions kept apart so the treaty cap applies to each), executions left after a partial row-level match reconciled with the orders left (no false `SOURCE_CONFLICT`, a real one in either order), a conflict of a later year not blocking an earlier return, the `INTEREST_ROUNDING_LEVEL` note with both figures, the settlement and NBP rate dates on trade rows, and a year-end sale flagged for both years (`RECOGNITION_DATE`). |
| `FlexAndRevolutImportTests.cs` | The IB Flex Query CSV (sections, signs, accounts, unknown rows, alone and next to the Activity Statement of the same account, partial overlap, the same transaction id on two accounts, a refund linked by `ActionID` across years) and the Revolut savings statement (interest only across currencies, the summary check, unknown rows, the same result as the generic template, a repeated upload in another order). |
| `CapitalGainsTaxCalculatorTests.cs` | FIFO matching by ISIN, commissions as costs, the buy-date rate for costs, splits, the message for missing history. |
| `CorporateActionCalculatorTests.cs` | Reverse splits, ISIN changes, cash and stock mergers, spin-offs, cash in lieu, the opening-position check. |
| `DividendTaxCalculatorTests.cs` | Summing and netting withholding rows, matching by income kind, the treaty cap and the statutory cap. |
| `PriorYearLossCalculatorTests.cs` | The five-year window and the per-year limits. |
| `TaxCalculatorTests.cs`, `ReviewFixesTests.cs` | `Pit38Fields.FromSummary`: rounding, the form layout of each year, years without a verified form. |
| `StatementMergerTests.cs` | Overlapping and repeated statements. |
| `InteractiveBrokersStatementParserTests.cs`, `InteractiveBrokersCorporateActionTests.cs`, `Trading212StatementParserTests.cs` | The CSV formats of both brokers. |
| `GenericCsvStatementParserTests.cs` | The generic trades and income templates: the template rows, one statement per broker and account (no account id without the `account_id` column), the strict number and date rules, the values accepted for `direction` and `income_type`, the amount check, the country/ISIN check. |
| `CountryBreakdownTests.cs` | Which country a row goes to in PIT/ZG: the named country, else the ISIN prefix. |
| `DbSchemaUpgraderTests.cs` | Upgrading a database of an older schema: restored totals, a correct zero left alone, sessions flagged for re-import (rows without kind or credit; rows produced by the superseded matching), a second run changing nothing. |
| `UploadLimitTests.cs` | The Nginx limit equal to the API limit, Docker ports on `127.0.0.1`, a valid 2 MiB statement accepted, a file above 10 MiB rejected with `file_too_large`. |

## Helpers

- **`PitlyApp`** hosts the real application in-process (`WebApplicationFactory<Program>`): real endpoints,
  parsers, calculators, SQLite and schema upgrade. Only two things are replaced: exchange rates are fixed
  (USD = 4 PLN, EUR = 4.50 PLN, PLN = 1; `new PitlyApp(extraRates)` adds or overrides a rate, also per day with a
  key such as `"EUR@2025-01-31"`) and the database is a temporary file deleted afterwards.
  `ImportAsync(files, taxYear, residencyStartDate, priorYearLossesJson, accountLabelsJson, creditMethod)` posts
  multipart form data the way the browser does; `GetAsync(url)` reads a stored session. The response wrapper
  gives `Field("dochod")` for a PIT-38 field, `Pit38`, `Summary`, `Error`. The findings are in
  `Pit38.GetProperty("diagnostics")` (code, message, accountId, affectedYears, unresolvedAmountPln) and the
  readiness in `Pit38.GetProperty("readyToFile")`.
- **`IbStatement`** builds an Interactive Brokers Activity Statement: `Instrument`, `Buy`, `Sell`, `Dividend`,
  `Withholding`, `Interest` (with an optional description such as `CREDIT INT FOR JAN-2025`),
  `InterestWithholding` (a Withholding Tax row without ticker or ISIN), `Split`, `OpeningPosition`,
  `PadTo(bytes)`, `Build()`.
- **`IbFlexCsv`** builds an Activity Flex Query CSV with its own header per section: `Buy`, `Sell` (dates as
  `yyyyMMdd;HHmmss`), `Dividend`, `Withholding` (an `actionId` links them; a negative amount is a refund),
  `Interest`, `InterestWithholding`, `OtherCash` (a row of another type), `Split`, `Build()`. Every row names its
  account, so one file can hold several.
- **`RevolutSavingsCsv`** builds a consolidated Revolut savings statement with a BOM, CRLF, quoted dates, currency
  signs and thousands separators: `Deposit`, `Interest`, `Withdrawal`, `Other` (an unknown row),
  `SummaryInterest` (a summary that disagrees with the rows), `Build()`.
- **`Trading212Export`** builds a Trading 212 history export: `Buy`, `Sell`, `Dividend` (each with an operation
  id), `Build()`.
- **`GenericTradesCsv`** and **`GenericIncomeCsv`** build files in the generic templates: `Buy`, `Sell` and
  `Dividend`, `Interest`, each with the broker (the account) and a `tx_id`, `Build()`.
- **`TestRateService`** returns fixed rates. A key such as `"USD@2025-05-15"` sets the rate of one day.

## Writing an end-to-end test

Choose amounts that make the arithmetic obvious at 4 PLN per USD, and write the hand calculation next to the
assertion.

```csharp
[Fact]
public async Task Import_DividendTaxIsRoundedOnlyInTheDifference()
{
    var statement = new IbStatement("U1111111", 2025)
        .Dividend("AAPL", "US0378331005", "2025-05-15", 25.75m)      // 103 PLN
        .Withholding("AAPL", "US0378331005", "2025-05-15", 3.775m);  // 15.10 PLN

    using var app = new PitlyApp();
    var result = await app.ImportAsync([("2025.csv", statement.Build())]);

    Assert.Equal(HttpStatusCode.OK, result.Status);
    Assert.Equal(19.57m, result.Field("zryczaltowanyPodatek"));
    Assert.Equal(15.10m, result.Field("podatekZaplaconyZaGranica"));
    Assert.Equal(4m, result.Field("roznica"));
}
```

Rules for new tests:

- Synthetic statements only. Account numbers in tests are made up (`U1111111`, `U2222222`).
- Fixed rates only; a test must not depend on the NBP API or on today's date.
- A change in tax behaviour gets an end-to-end test, because the bug may sit in the merge, in the storage or in
  the mapping rather than in the calculator.
- Assert the fields a user would copy into the form, not only intermediate values. When a case leaves money
  unresolved, assert the finding's code and `unresolvedAmountPln` and that `readyToFile` is false, so that a
  future change cannot quietly turn an open question into a declared amount.
- A change in the matching of withholding is a change in tax behaviour: it needs a case in
  `WithholdingReconciliationTests` or `CreditRulesTests` with the hand calculation, and the expected value of an
  older test is only changed with the reason written next to it (the tests changed in the 2025 audit say which
  behaviour they replaced and why).
- When the CSV format matters (a new section or column), put the test next to the parser tests and build the
  CSV text in the test.

## Control examples

These came from a review of the tax logic and must keep passing. The inputs are in
[tax-rules.md](tax-rules.md).

| Example | Expected | Test |
|---|---|---|
| A buys for 100 PLN, B buys for 300 PLN and sells for 350 PLN | income 50, A keeps cost 100 | `Import_SaleOnOneAccountDoesNotConsumeTheOtherAccountsLot`, `Import_ShareLeftOnTheFirstAccountKeepsItsOwnCost` |
| A 2:1 split in both statements | each account doubles once | `Import_SplitReportedByBothStatementsDoublesEachAccountExactlyOnce` |
| Same dividend on two accounts, one statement repeated | both count, the repeat adds nothing | `Import_SameDividendOnTwoAccountsCountsTwiceButARepeatedStatementDoesNot` |
| Refund larger than the tax withheld on that payment | credit equals the tax actually withheld | `Import_CreditNeverExceedsTheTaxActuallyWithheldAfterCorrections` |
| Income 10 000, loss deduction 10 000 | 0 PLN to pay everywhere | `Import_LossDeductionEqualToTheGainLeavesNothingToPayAnywhere` |
| Dividend 103 PLN, credit 15.10 PLN, year 2025 | 19.57 / 15.10 / 4 in poz. 47 / 48 / 49 | `Import_DividendTaxIsRoundedOnlyInTheDifference` |
| Full 2024, a 2025 sale without history | 2024 calculates, 2025 offers 2024 | `Import_LaterSaleWithoutHistoryDoesNotBlockTheEarlierYear` |
| Prior-year losses | poz. 28 for 2024, poz. 30 for 2025 | `Import_PriorYearLossesAreNumberedByTheFormOfTheTaxYear` |
| The two generic template files (buy 10 at 150 USD + 1.50, sell at 175 + 1.75; dividend 25 USD less 3.75; Swiss interest 120 EUR less 22.80) | 7000 / 6013 / 987, tax 188; flat tax 121.60, paid abroad 117.60, difference 4; interest under CH in PIT/ZG | `Import_GenericTemplatesGiveTheFormFieldsAndAttributeInterestByTheNamedCountry` |
| Generic files with `account_id` A (buy 100 in 2024) and B (buy 300, sell 350 in 2025) | costs 300, income 50, tax 10; A's lot stays | `Import_GenericFilesWithExplicitAccountsKeepTheirFifoApart` |
| Flex: one `ActionID`, dividend 80 + 12 tax, payment in lieu 20 + 3 tax | credit 15, difference 4; classification finding blocks | `Import_FlexActionIdSharedByDividendAndPaymentInLieuKeepsEachWithholdingWithItsKind` |
| Flex: tax 15 and refund 5 dated to the payment, refund reported next year | credit 10, difference 9, cross-year finding for both years | `Import_FlexRefundReportedNextYearIsFlaggedForBothYearsAndNotReadyToFile` |
| 10 USD withheld at 4.00, 5 USD at 5.00 on one dividend | 65 PLN withheld in either order | `Import_EachWithholdingOnOnePaymentIsValuedAtItsOwnRate` |
| 0.01 EUR of interest at 10.51 | base 0.11, tax 0.03 | `Import_InterestBaseAndTaxAreBothRoundedUpToTheGrosz` |
| 100 PLN of interest, 0.015 PLN creditable | 19.00 / 0.02 / 18.98, the same after a reload | `Import_FormFieldsNeverCarryFractionsOfAGrosz` |
| Dividend from country ZZ with 15 PLN withheld | treaty cap: credit 0 and blocked; statutory cap: credit 15 | `Import_UnknownTreatyRateDoesNotFallBackToTheStatutoryCapSilently` |
| Activity order (10 for 260) next to Flex executions (4 at 20, 6 at 30) | 1600 / 1040 / tax 106 in every combination | `Import_ActivityOrdersAndFlexExecutionsOfTheSameAccountReconcileAtOrderLevel` |

## Checking the UI by hand

1. Start the API and the frontend (see the commands in [AGENTS.md](../AGENTS.md)).
2. Upload `samples/sample-activity-statement.csv`, or a statement built with the helpers above and saved to a
   temporary file outside the repository.
3. Look at the dashboard, the PIT-38 page and its print preview. The amount to pay must be the same in all
   three.
4. Switch the language to English, Ukrainian and Polish. Check that new text fits, including the collapsed and
   the expanded sidebar.
5. Check the browser console for errors.

## Not covered by automated tests

- An upload through Nginx inside Docker. Only the configuration is compared with the API limit.
- The print layout. Print rules live in `frontend/src/index.css`.
- Every frontend interaction.
- The live NBP API.
