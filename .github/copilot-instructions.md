# Copilot instructions

Follow [AGENTS.md](../AGENTS.md) in the repository root. It is the single set of instructions for every coding
agent, and it links to the documents in `docs/` (architecture, tax rules, testing, recipes).

The rules that matter most when suggesting code here:

- Money is `decimal` and is rounded only in `Pit38Fields.FromSummary`.
- Declared amounts come from the backend's `Pit38Fields`; the frontend never recomputes a tax figure.
- PIT-38 field numbers come from `Pit38FormLayout` (backend) and `formRefs()` (frontend), never from a literal.
- FIFO, duplicate detection and withholding matching work per broker account.
- Every UI string exists in `en`, `uk` and `pl` under `frontend/src/i18n/`.
- Tests use synthetic statements only; never add real broker data to the repository.
