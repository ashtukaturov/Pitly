using Pitly.Core.Tax;

namespace Pitly.Core.Models;

/// <summary>
/// The result of one calculation. Every PLN total here keeps its full precision: rounding happens only in
/// <see cref="Pit38Fields.FromSummary"/>, where the rule of the form field requires it.
/// </summary>
public record TaxSummary(
    decimal TotalProceedsPln,
    decimal TotalCostPln,
    decimal CapitalGainPln,
    decimal CapitalGainTaxPln,
    decimal TotalDividendsPln,
    decimal TotalWithholdingPln,
    decimal TotalCreditableWithholdingPln,
    decimal DividendTaxOwedPln,
    int Year,
    DateTime TaxableFrom,
    DateTime TaxableTo,
    List<TradeResult> TradeResults,
    List<Dividend> Dividends,
    // Prior-year capital losses entered by the user; checked and applied to poz. 30 by PriorYearLossCalculator.
    IReadOnlyList<PriorYearLoss>? PriorYearLosses = null,
    // Parser/calculation warnings the user should review (corporate actions treated conservatively, ignored rows).
    IReadOnlyList<string>? Warnings = null,
    // Income by issuer country for the PIT/ZG annex.
    IReadOnlyList<CountryIncome>? Countries = null,
    // Interest part of TotalDividendsPln (art. 30a ust. 1 pkt 1-3), rounded to grosz up instead of full PLN
    decimal TotalInterestPln = 0m,
    decimal TotalInterestCreditableWithholdingPln = 0m,
    // Subset of Warnings that puts the result itself in doubt (opening positions that do not match the uploaded
    // history, withholding that could not be attributed). Repeated on PIT-38 and in the printed report.
    IReadOnlyList<string>? ResultWarnings = null,
    // Structured findings: every ResultWarning and every informational note, with a code, a scope and the amount
    // that depends on it
    IReadOnlyList<TaxDiagnostic>? Diagnostics = null,
    // Every withholding event of the selected year and what was done with it
    IReadOnlyList<WithholdingEvent>? WithholdingEvents = null,
    // How the foreign tax credit was limited
    CreditMethod CreditMethod = CreditMethod.TreatyCap,
    // The uploaded files, hashed, for provenance
    IReadOnlyList<SourceFile>? SourceFiles = null,
    // The user's answers to the questions no source settles (date of a trade, classifications, cross-year refunds);
    // null for a session stored before they existed, which was computed as TaxDecisions.Legacy
    TaxDecisions? Decisions = null,
    // Payments the user classified as income from other sources: declared in PIT-36, not in section G
    IReadOnlyList<OtherSourceIncome>? OtherSourcesIncome = null);

/// <summary>One uploaded file as the session remembers it: its hash and what it was read as, never its content.</summary>
public record SourceFile(
    string Name,
    string Sha256,
    string? Broker,
    string? Account,
    int? StatementYear,
    string? ParserVersion);
