# Recipes

Step lists for the changes that come up most often. Each list ends with the checks from
[AGENTS.md](../AGENTS.md): tests, API build, frontend lint and build.

## Add a broker

1. Create `backend/src/Pitly.Broker.<Name>/` with a parser that implements `IStatementParser` and returns a
   `ParsedStatement`. Add the project to `backend/Pitly.sln`.
2. Fill in what the export gives you:
   - `Broker`: the display name, used in account labels and warnings;
   - `AccountId`: the account number when the file contains one, otherwise `null` (the user then assigns the
     file to an account on the dashboard);
   - `TransactionId` on trades and income rows when the export has an operation id;
   - withholding: inside `RawDividend` (`WithheldAmount`, `WithheldCurrency`) when it is printed in the same row
     as the payment, or as separate `RawWithholdingTax` rows when the broker lists it separately;
   - `IncomeKind` for interest and payments in lieu;
   - `CountryCode` on trades and income rows when the export names the source country (otherwise PIT/ZG uses
     the ISIN prefix);
   - corporate actions and opening positions when the export has them.
   When one file can hold several accounts, override `ParseAll` and return one statement per account
   (`Pitly.Broker.Generic` does this per `broker` value); `Parse` then serves single-account files only.
3. Teach `StatementParserDispatcher` to recognise the format from the first line, and extend its error message.
4. Register the parser in `Program.cs`. Reference the project from `Pitly.Api.csproj` and `Pitly.Tests.csproj`.
5. Add a `COPY` line for the new `.csproj` to `backend/src/Pitly.Api/Dockerfile`, so that the cached restore
   layer covers the new project. (`docker/backend.Dockerfile`, used by the hosted deployment, lists projects the
   same way.)
6. Tests: parser tests with CSV text written in the test, a builder in `SyntheticStatements.cs`, and at least
   one end-to-end test through `PitlyApp`.
7. UI and docs: the broker list on the landing page, the export instructions on the import page (three
   languages), the README sections "How to export your broker statement" and "Limitations".

## Add the form of a new tax year

Do this only with the official PDF of that year's PIT-38 open.

1. Compare every field in `Pit38FormLayout` with the PDF. When the numbering is the same as an existing version,
   map the year to that version; otherwise add a new static layout.
2. Add the year to `ByTaxYear` and the PDF link to the comment at the top of `Pit38FormLayout.cs`.
3. Update `VERIFIED_FORM_YEARS` in `frontend/src/pit38Form.ts`.
4. Extend `FormLayout_ListsEveryVerifiedTaxYear` in `ReviewFixesTests.cs` and
   `Import_PriorYearLossesAreNumberedByTheFormOfTheTaxYear` in `EndToEndImportTests.cs`. Move the year out of
   the "no verified form" test cases if it was used there.
5. Read that year's brochure for changes in rounding or in the order of section G, and update
   `Pit38Fields.FromSummary` and [tax-rules.md](tax-rules.md) if anything changed.
6. Update the table in [tax-rules.md](tax-rules.md) and the README bullet "Form version by year".

## Add or change a UI text

1. Add the key to the page's module in `frontend/src/i18n/` in all three languages: `en`, `uk`, `pl`.
2. Use `const t = useT()` and `t('page.key', { name: value })`. Placeholders are written `{name}`.
3. Never write a form field number in the text. Take the reference from `formRefs(pit38.form)` and pass it as a
   parameter, for example `{straty}`.
4. Keep the Polish names of form fields ("Podatek należny") untranslated in English and Ukrainian.
5. Look at the page in all three languages. Ukrainian and Polish strings are often longer than English ones.

## Change the shape of an API response

1. Change the record in `Pitly.Core/Models` or the anonymous object in the endpoint.
2. Mirror it in `frontend/src/types.ts` (property names are camelCase, enums are strings).
3. If the value must survive reopening a session, store it: see the next recipe.
4. Update the API table in [architecture.md](architecture.md) and in the README.

## Add a stored column

There are no EF migrations; an existing `pitly.db` is upgraded on start.

1. Add the property to the entity in `backend/src/Pitly.Api/Data/Entities/` and map it in `EntityMapper`.
2. Add `("Table", "Column", "<SQLite definition with a default>")` to `Columns` in `DbSchemaUpgrader`.
3. If sessions stored by the previous version need their data rebuilt, raise `CurrentDataVersion` and extend
   `RepairStoredSessions`. Rebuild only what the stored rows prove; otherwise set `NeedsReimport`.
4. Extend `DbSchemaUpgraderTests`: build a database of the old schema with raw SQL, run the upgrade twice, and
   assert the restored values, the untouched correct values and the flagged sessions.

## Change a tax rule

1. Find the source: the statute article, the form, or the Ministry's brochure. Put the citation, with a link
   when there is one, in a comment next to the code.
2. Compute a control example by hand before touching the code.
3. Change the calculator, or `Pit38Fields.FromSummary` when the change is about rounding or about which field
   an amount goes to. Keep `decimal` precision until the rule of the field requires rounding.
4. Add a unit test for the calculator and an end-to-end test with the control example.
5. Update [tax-rules.md](tax-rules.md) and the README section "How it works". If the rule is an interpretation
   and not an explicit provision, say so in both.

## Add a warning

- A note about the import (a skipped duplicate file, an ignored row) goes to the statement's `Warnings` and is
  shown on the dashboard.
- A warning that puts the declared amounts in doubt is a `TaxDiagnostic` with `AffectsAmounts = true`, a code
  from `DiagnosticCodes` (add one there when none fits, with a `diag.code.<CODE>` label in `i18n/diagnostics.ts`
  in all three languages), the account, the source file, the affected years and the unresolved PLN amount. The
  calculators add to the diagnostics list that `TaxCalculator` passes them; parsers and the merger put theirs in
  `ParsedStatement.Diagnostics`. `TaxSummary.ResultWarnings` and `Pit38Fields.DataWarnings` are derived from the
  messages, so the old string lists stay in step and a reopened session shows the same findings.
- Backend warnings are English sentences that name the instrument, the date and the account, so the user can
  find the row in the statement.
- Add an assertion on `dataWarnings` to an end-to-end test, including a `GET /api/session/{id}/pit38` after the
  import.
