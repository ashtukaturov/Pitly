using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pitly.Core.Models;
using Pitly.Core.Parsing;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Broker.Revolut;

/// <summary>
/// Parses the consolidated savings statement Revolut exports as CSV. The file holds several sections, one pair per
/// currency: "Summary for Savings Accounts - EUR" (one row of control totals) and "Transactions for Savings
/// Accounts - EUR" (one row per operation). Only the transaction rows labelled as interest are income
/// (art. 30a ust. 1 pkt 3); deposits, withdrawals, transfers and balances are movements of the user's own money.
/// The summary is read only to check the transactions: a difference between the summary's "Interest earned" and
/// the interest rows is reported, and no income is ever created from the summary.
///
/// The parser accepts a UTF-8 file with or without a byte-order mark, CRLF or LF line ends, quoted dates in several
/// layouts, amounts with a currency sign or code and with thousands separators. Rows of an operation type it does
/// not know are counted and reported, never guessed. Revolut's export carries no account number, no tax withheld
/// (interest is paid gross) and no source country; the country of the income is not derived from the currency.
/// Revolut changes the layout of this export from time to time; the detection and the column names below are
/// deliberately loose, and any row that cannot be read is reported with its line number.
/// </summary>
public sealed partial class RevolutSavingsStatementParser : IStatementParser
{
    public const string BrokerName = "Revolut";
    public const string ParserVersion = "revolut-savings-csv/1";

    private readonly ILogger<RevolutSavingsStatementParser> _logger;

    public RevolutSavingsStatementParser(ILogger<RevolutSavingsStatementParser> logger)
    {
        _logger = logger;
    }

    /// <summary>True when the file carries a Revolut savings section title in its first lines.</summary>
    public static bool Detect(string content)
    {
        var count = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = Clean(rawLine);
            if (line.Length == 0)
                continue;
            if (SectionRegex().IsMatch(line))
                return true;
            if (++count >= 10)
                break;
        }

        return false;
    }

    private enum SectionKind { Summary, Transactions }

    private sealed record Section(SectionKind Kind, string Currency, int Line, List<string> Header, List<(List<string> Fields, int Line)> Rows);

    public ParsedStatement Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new FormatException("File is empty.");

        var sections = ReadSections(content);
        if (sections.Count == 0)
            throw new FormatException("File does not appear to be a Revolut savings statement: no 'Summary for Savings Accounts' or 'Transactions for Savings Accounts' section was found.");

        var income = new List<RawDividend>();
        var withholding = new List<RawWithholdingTax>();
        var warnings = new List<string>();
        var diagnostics = new List<TaxDiagnostic>();
        var unknownTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var interestByCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var summaryByCurrency = new Dictionary<string, (decimal Interest, int Line)>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections.Where(s => s.Kind == SectionKind.Transactions))
            ParseTransactions(section, income, withholding, unknownTypes, interestByCurrency, warnings);

        foreach (var section in sections.Where(s => s.Kind == SectionKind.Summary))
            ParseSummary(section, summaryByCurrency);

        foreach (var (currency, (summaryInterest, line)) in summaryByCurrency)
        {
            var fromRows = interestByCurrency.GetValueOrDefault(currency);
            if (summaryInterest != fromRows)
            {
                var message =
                    $"Revolut summary for {currency} (line {line}) reports {N(summaryInterest)} {currency} of interest earned, but the transaction rows add up to {N(fromRows)} {currency}. " +
                    "The transaction rows are what Pitly declares; check that the statement covers the whole period and that every interest row was recognised.";
                warnings.Add(message);
                diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.SummaryMismatch, DiagnosticSeverity.Warning, message,
                    UnresolvedAmountPln: null, AffectsAmounts: true));
            }
        }

        foreach (var (type, count) in unknownTypes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var message = $"Revolut statement: {count} row{(count == 1 ? string.Empty : "s")} of type '{type}' {(count == 1 ? "is" : "are")} not recognised and {(count == 1 ? "was" : "were")} left out. " +
                          "If it is interest, rename it to 'Interest earned' in a copy of the file or enter it through the generic income template.";
            warnings.Add(message);
            diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.UnsupportedRow, DiagnosticSeverity.Warning, message, AffectsAmounts: true));
        }

        if (income.Count == 0 && withholding.Count == 0)
        {
            warnings.Add("Revolut statement: no interest rows were found. There is nothing to declare from it.");
        }

        var years = income.Select(d => d.Date.Year).Concat(withholding.Select(w => w.Date.Year)).ToList();

        _logger.LogInformation("Parsed Revolut savings statement: {Sections} sections, {Interest} interest rows", sections.Count, income.Count);

        return new ParsedStatement(
            Trades: [],
            Dividends: income,
            WithholdingTaxes: withholding,
            StatementYear: years.Count == 0 ? null : years.Max(),
            Warnings: warnings,
            AccountId: null,
            Broker: BrokerName,
            Diagnostics: diagnostics,
            ParserVersion: ParserVersion);
    }

    private static List<Section> ReadSections(string content)
    {
        var sections = new List<Section>();
        Section? current = null;
        var lineNumber = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            var first = Clean(fields.FirstOrDefault());
            var title = SectionRegex().Match(first);
            if (title.Success && fields.Skip(1).All(f => string.IsNullOrWhiteSpace(Clean(f))))
            {
                var kind = title.Groups["kind"].Value.StartsWith("Summary", StringComparison.OrdinalIgnoreCase) ? SectionKind.Summary : SectionKind.Transactions;
                current = new Section(kind, title.Groups["currency"].Value.ToUpperInvariant(), lineNumber, [], []);
                sections.Add(current);
                continue;
            }

            if (current is null)
                continue;

            if (current.Header.Count == 0)
            {
                current.Header.AddRange(fields.Select(f => Clean(f)));
                continue;
            }

            if (fields.All(f => string.IsNullOrWhiteSpace(Clean(f))))
                continue;

            current.Rows.Add((fields, lineNumber));
        }

        return sections;
    }

    private void ParseTransactions(
        Section section,
        List<RawDividend> income,
        List<RawWithholdingTax> withholding,
        Dictionary<string, int> unknownTypes,
        Dictionary<string, decimal> interestByCurrency,
        List<string> warnings)
    {
        var dateIndex = FindColumn(section.Header, "date");
        var descriptionIndex = FindColumn(section.Header, "description", "details", "type");
        var typeIndex = FindColumn(section.Header, "type");
        var amountIndex = FindColumn(section.Header, "amount", "value", "money");
        var currencyIndex = FindColumn(section.Header, "currency");
        if (dateIndex < 0 || amountIndex < 0 || descriptionIndex < 0)
        {
            throw new FormatException(
                $"Line {section.Line}: the Revolut transactions section for {section.Currency} has no recognisable Date, Description and Amount columns " +
                $"(header: {string.Join(", ", section.Header)}).");
        }

        var index = 0;
        foreach (var (fields, line) in section.Rows)
        {
            index++;
            var description = Field(fields, descriptionIndex);
            var type = typeIndex >= 0 ? Field(fields, typeIndex) : description;
            var amountRaw = Field(fields, amountIndex);
            var currency = currencyIndex >= 0 && Field(fields, currencyIndex).Length == 3 ? Field(fields, currencyIndex).ToUpperInvariant() : section.Currency;

            if (!TryParseDate(Field(fields, dateIndex), out var date))
                throw new FormatException($"Line {line}: the date '{Field(fields, dateIndex)}' of a Revolut transaction could not be read.");

            if (!TryParseMoney(amountRaw, out var amount, out var amountCurrency))
                throw new FormatException($"Line {line}: the amount '{amountRaw}' of a Revolut transaction could not be read.");
            if (amountCurrency is not null)
                currency = amountCurrency;

            var label = (type + " " + description).Trim();
            switch (Classify(type, description))
            {
                case RowKind.Interest:
                    if (amount <= 0m)
                    {
                        warnings.Add($"Line {line}: an interest row of {N(amount)} {currency} is not positive and was left out.");
                        break;
                    }

                    income.Add(new RawDividend(
                        Symbol: currency + " interest",
                        Currency: currency,
                        Date: date,
                        Amount: amount,
                        Kind: IncomeKind.Interest,
                        TransactionId: null,
                        Reference: null,
                        BookingDate: date,
                        RawType: type,
                        RawDescription: description));
                    interestByCurrency[currency] = interestByCurrency.GetValueOrDefault(currency) + amount;
                    break;
                case RowKind.Tax:
                    // Revolut pays interest gross to most customers; when a statement does list tax, it is kept as a
                    // withholding event tied to the interest of that day and currency.
                    withholding.Add(new RawWithholdingTax(
                        Symbol: currency + " interest",
                        Currency: currency,
                        Date: date,
                        Amount: Math.Abs(amount),
                        Kind: IncomeKind.Interest,
                        BookingDate: date,
                        SourceStatementYear: date.Year,
                        RawType: type,
                        RawDescription: description));
                    break;
                case RowKind.Movement:
                    break;
                default:
                    unknownTypes[label] = unknownTypes.GetValueOrDefault(label) + 1;
                    break;
            }
        }
    }

    private static void ParseSummary(Section section, Dictionary<string, (decimal Interest, int Line)> summaryByCurrency)
    {
        var interestIndex = FindColumn(section.Header, "interest");
        if (interestIndex < 0)
            return;

        foreach (var (fields, line) in section.Rows)
        {
            if (TryParseMoney(Field(fields, interestIndex), out var interest, out var currency))
                summaryByCurrency[currency ?? section.Currency] = (summaryByCurrency.GetValueOrDefault(currency ?? section.Currency).Interest + interest, line);
        }
    }

    private enum RowKind { Interest, Tax, Movement, Unknown }

    private static RowKind Classify(string type, string description)
    {
        var text = (type + " " + description).ToLowerInvariant();
        if (text.Contains("tax", StringComparison.Ordinal) && text.Contains("interest", StringComparison.Ordinal))
            return RowKind.Tax;
        if (text.Contains("withholding", StringComparison.Ordinal))
            return RowKind.Tax;
        if (text.Contains("interest", StringComparison.Ordinal))
            return RowKind.Interest;
        if (MovementRegex().IsMatch(text))
            return RowKind.Movement;
        return RowKind.Unknown;
    }

    private static int FindColumn(List<string> header, params string[] names)
    {
        foreach (var name in names)
        {
            for (var i = 0; i < header.Count; i++)
            {
                if (header[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return -1;
    }

    private static string Field(List<string> fields, int index) => index >= 0 && index < fields.Count ? Clean(fields[index]) : string.Empty;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss",
        "d MMM yyyy", "dd MMM yyyy", "MMM d, yyyy", "MMM dd, yyyy", "MMMM d, yyyy", "MMMM dd, yyyy",
        "d MMMM yyyy", "dd MMMM yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "d MMM yyyy, HH:mm", "MMM d, yyyy, HH:mm", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"
    ];

    private static bool TryParseDate(string value, out DateTime date)
    {
        var text = value.Trim().Trim('"').Trim();
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            date = date.Date;
            return true;
        }

        return false;
    }

    /// <summary>
    /// "€1,234.56", "-€0.01", "1 234,56 zł", "(100.01)", "EUR 100.00", "100.00 EUR". Returns the currency when the
    /// text names one. Thousands separators are removed; the last separator with one to two digits after it is the
    /// decimal point.
    /// </summary>
    private static bool TryParseMoney(string raw, out decimal value, out string? currency)
    {
        value = 0m;
        currency = null;
        var text = raw.Trim().Trim('"').Trim();
        if (text.Length == 0)
            return false;

        var negative = false;
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1];
        }

        foreach (var (symbol, code) in CurrencySymbols)
        {
            if (text.Contains(symbol, StringComparison.Ordinal))
            {
                currency = code;
                text = text.Replace(symbol, string.Empty, StringComparison.Ordinal);
            }
        }

        var codeMatch = CurrencyCodeRegex().Match(text);
        if (codeMatch.Success)
        {
            currency = codeMatch.Groups["code"].Value.ToUpperInvariant();
            text = text.Remove(codeMatch.Index, codeMatch.Length);
        }

        text = text.Replace(" ", string.Empty).Replace(" ", string.Empty).Trim();
        if (text.StartsWith('-') || text.StartsWith('−'))
        {
            negative = !negative;
            text = text[1..];
        }
        else if (text.StartsWith('+'))
        {
            text = text[1..];
        }

        // Decide which of '.' and ',' is the decimal separator: the last one, when at most two digits follow it.
        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');
        var last = Math.Max(lastDot, lastComma);
        string normalized;
        if (last < 0)
        {
            normalized = text;
        }
        else
        {
            var decimals = text.Length - last - 1;
            var separator = text[last];
            var other = separator == '.' ? ',' : '.';
            if (decimals <= 2 && text.IndexOf(separator) == last)
                normalized = text.Replace(other.ToString(), string.Empty).Replace(separator, '.');
            else
                normalized = text.Replace(",", string.Empty); // "1,234,567" style thousands only
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            return false;
        if (negative)
            value = -value;
        return true;
    }

    private static readonly (string Symbol, string Code)[] CurrencySymbols =
    [
        ("€", "EUR"), ("£", "GBP"), ("$", "USD"), ("zł", "PLN"), ("CHF", "CHF"), ("Kč", "CZK"), ("Ft", "HUF"), ("lei", "RON"), ("kr", "SEK")
    ];

    private static string N(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<kind>Summary|Transactions)\s+for\s+Savings\s+Accounts?\s*[-–:]\s*(?<currency>[A-Za-z]{3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"(?<![A-Za-z])(?<code>EUR|USD|GBP|PLN|CHF|CZK|HUF|RON|SEK|NOK|DKK)(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyCodeRegex();

    [GeneratedRegex(@"\b(deposit\w*|top[- ]?up\w*|withdraw\w*|transfer\w*|added|moved|balance|opening|closing|fee\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MovementRegex();
}
