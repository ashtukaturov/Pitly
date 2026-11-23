using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pitly.Core.Models;
using Pitly.Core.Parsing;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Broker.InteractiveBrokers;

/// <summary>
/// Parses an Interactive Brokers Activity Flex Query exported as CSV. A Flex CSV is several tables in one file:
/// each section (Trades, Cash Transactions, Corporate Actions, ...) starts with its own header row, and every
/// header begins with <c>ClientAccountID</c>. Sections are told apart by their columns, never by position, and the
/// file is never read as one flat table. One file can hold several accounts; <see cref="ParseAll"/> returns one
/// statement per <c>ClientAccountID</c>.
///
/// Signs follow the Flex format: a trade's <c>Proceeds</c> is negative for a buy, <c>IBCommission</c> is negative,
/// a withholding row's <c>Amount</c> is negative when tax is withheld and positive when it is refunded, a dividend
/// row is positive and its reversal negative. Amounts are read in <c>CurrencyPrimary</c>; the base-currency
/// equivalents IB adds (<c>FXRateToBase</c>) are ignored, because Pitly converts at the NBP rate itself.
///
/// The sections this parser reads: Trades (executions of stocks), Cash Transactions (dividends, payments in lieu,
/// withholding tax, broker and bond interest), Corporate Actions. Rows of other kinds are counted and reported,
/// not guessed. The XML flavour of the Flex Query is not supported.
/// </summary>
public sealed partial class InteractiveBrokersFlexParser : IStatementParser
{
    public const string FormatName = "Interactive Brokers Flex Query";
    public const string ParserVersion = "ib-flex-csv/1";

    private readonly ILogger<InteractiveBrokersFlexParser> _logger;
    private readonly InteractiveBrokersStatementParser _activityParser;

    public InteractiveBrokersFlexParser(ILogger<InteractiveBrokersFlexParser> logger, InteractiveBrokersStatementParser activityParser)
    {
        _logger = logger;
        _activityParser = activityParser;
    }

    /// <summary>True when the first line of the file is a Flex section header.</summary>
    public static bool Detect(string firstLine)
    {
        var first = ParseCsvLine(firstLine.TrimEnd('\r')).FirstOrDefault();
        return string.Equals(Clean(first), "ClientAccountID", StringComparison.OrdinalIgnoreCase);
    }

    public ParsedStatement Parse(string content)
    {
        var statements = ParseAll(content);
        if (statements.Count == 1)
            return statements[0];

        throw new FormatException(
            $"The Flex Query lists {statements.Count} accounts ({string.Join(", ", statements.Select(s => s.AccountId))}); " +
            "each account is a separate statement.");
    }

    private enum Section { Unknown, Trades, Cash, CorporateActions }

    private sealed class AccountData
    {
        public List<Trade> Trades { get; } = [];
        public List<RawDividend> Income { get; } = [];
        public List<RawWithholdingTax> Withholding { get; } = [];
        public List<List<string>> CorporateActionRows { get; } = [];
        public Dictionary<string, string> SymbolToIsin { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Unsupported { get; } = new(StringComparer.Ordinal);
        public List<string> Warnings { get; } = [];
        public List<TaxDiagnostic> Diagnostics { get; } = [];

        public void Count(string what) => Unsupported[what] = Unsupported.GetValueOrDefault(what) + 1;
    }

    public IReadOnlyList<ParsedStatement> ParseAll(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new FormatException("File is empty.");

        var accounts = new Dictionary<string, AccountData>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        Dictionary<string, int>? columns = null;
        var section = Section.Unknown;
        var lineNumber = 0;
        var dataRows = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            if (fields.Count == 0)
                continue;

            if (string.Equals(Clean(fields[0]), "ClientAccountID", StringComparison.OrdinalIgnoreCase))
            {
                columns = BuildColumnMap(fields);
                section = DetectSection(columns);
                if (section == Section.Unknown)
                    _logger.LogInformation("Flex section starting on line {Line} has no columns Pitly reads; it is skipped", lineNumber);
                continue;
            }

            if (columns is null)
                throw new FormatException($"Line {lineNumber} comes before any Flex section header (a row starting with ClientAccountID).");

            if (section == Section.Unknown)
                continue;

            var row = new Row(fields, columns, lineNumber);
            var accountId = row.Get("ClientAccountID");
            if (string.IsNullOrWhiteSpace(accountId))
            {
                // A total line or a row of a summary query: nothing to attribute to an account.
                continue;
            }

            if (!accounts.TryGetValue(accountId, out var data))
            {
                data = new AccountData();
                accounts[accountId] = data;
                order.Add(accountId);
            }

            dataRows++;
            switch (section)
            {
                case Section.Trades:
                    ParseTrade(row, data);
                    break;
                case Section.Cash:
                    ParseCashTransaction(row, data);
                    break;
                case Section.CorporateActions:
                    ParseCorporateActionRow(row, data);
                    break;
            }
        }

        if (columns is null)
            throw new FormatException("File does not appear to be an IB Flex Query CSV: no section header starting with ClientAccountID was found.");

        if (dataRows == 0)
            throw new FormatException("The Flex Query CSV has section headers but no data rows.");

        var statements = new List<ParsedStatement>();
        foreach (var accountId in order)
        {
            var data = accounts[accountId];
            foreach (var (what, count) in data.Unsupported.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var message = $"Flex Query, account {accountId}: {count} {what} row{(count == 1 ? string.Empty : "s")} {(count == 1 ? "was" : "were")} left out; Pitly does not handle them.";
                data.Warnings.Add(message);
                data.Diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.UnsupportedRow, DiagnosticSeverity.Info, message, AccountId: accountId));
            }

            var actions = _activityParser.ParseCorporateActionRows(data.CorporateActionRows, data.SymbolToIsin, data.Warnings);

            var years = data.Trades.Select(t => t.DateTime.Year)
                .Concat(data.Income.Select(d => d.Date.Year))
                .Concat(data.Withholding.Select(w => w.Date.Year))
                .Concat(actions.Select(a => a.DateTime.Year))
                .ToList();
            if (years.Count == 0)
            {
                data.Warnings.Add($"Flex Query, account {accountId}: no trades, dividends, interest or corporate actions were found. There is nothing to declare from it.");
            }

            // A Flex row carries its own booking date; the statement year is the last year it covers.
            var statementYear = years.Count == 0 ? (int?)null : years.Max();

            statements.Add(new ParsedStatement(
                data.Trades,
                NetIncome(data.Income),
                data.Withholding,
                actions,
                CarryInPositions: [],
                StatementYear: statementYear,
                Warnings: data.Warnings,
                AccountId: accountId,
                Broker: InteractiveBrokersStatementParser.BrokerName,
                Diagnostics: data.Diagnostics,
                ParserVersion: ParserVersion));
        }

        _logger.LogInformation("Parsed Flex Query CSV: {Accounts} account(s), {Trades} trades, {Income} income rows, {Withholding} withholding rows",
            statements.Count, statements.Sum(s => s.Trades.Count), statements.Sum(s => s.Dividends.Count), statements.Sum(s => s.WithholdingTaxes.Count));

        return statements;
    }

    private static Section DetectSection(Dictionary<string, int> columns)
    {
        if (columns.ContainsKey("Buy/Sell") && (columns.ContainsKey("TradeID") || columns.ContainsKey("TradePrice")))
            return Section.Trades;
        if (columns.ContainsKey("ActionDescription"))
            return Section.CorporateActions;
        if (columns.ContainsKey("Type") && columns.ContainsKey("Amount") && (columns.ContainsKey("SettleDate") || columns.ContainsKey("Date/Time")))
            return Section.Cash;
        return Section.Unknown;
    }

    private void ParseTrade(Row row, AccountData data)
    {
        // Orders and closed lots repeat the executions.
        var level = row.Get("LevelOfDetail");
        if (!string.IsNullOrEmpty(level) && !level.Equals("EXECUTION", StringComparison.OrdinalIgnoreCase))
            return;

        var assetClass = row.Get("AssetClass");
        if (!string.IsNullOrEmpty(assetClass) && !assetClass.Equals("STK", StringComparison.OrdinalIgnoreCase))
        {
            data.Count(assetClass.Equals("CASH", StringComparison.OrdinalIgnoreCase) ? "currency conversion" : "trade of asset class " + assetClass);
            return;
        }

        var side = (row.Get("Buy/Sell") ?? string.Empty).ToUpperInvariant();
        if (side.Contains("CA.", StringComparison.Ordinal) || side.Contains("CANCEL", StringComparison.Ordinal))
        {
            data.Count("cancelled trade");
            return;
        }

        var symbol = row.Get("Symbol") ?? string.Empty;
        var currency = row.Get("CurrencyPrimary") ?? string.Empty;
        if (!TryParseFlexDateTime(row.Get("DateTime") ?? row.Get("TradeDate"), out var dateTime))
        {
            _logger.LogWarning("Skipping Flex trade row on line {Line}: could not parse date '{Date}'", row.LineNumber, row.Get("DateTime"));
            data.Count("unreadable trade");
            return;
        }

        if (!TryParseDecimal(row.Get("Quantity"), out var quantity) || !TryParseDecimal(row.Get("TradePrice"), out var price))
        {
            _logger.LogWarning("Skipping Flex trade row on line {Line}: could not parse quantity or price", row.LineNumber);
            data.Count("unreadable trade");
            return;
        }

        if (!TryParseDecimal(row.Get("Proceeds"), out var proceeds))
            proceeds = -quantity * price;
        TryParseDecimal(row.Get("IBCommission"), out var commission);
        TryParseDecimal(row.Get("Taxes"), out var taxes);
        TryParseDecimal(row.Get("FifoPnlRealized"), out var realized);

        var type = side.StartsWith("SELL", StringComparison.Ordinal) || (side.Length == 0 && quantity < 0) ? TradeType.Sell : TradeType.Buy;
        var isin = Blank(row.Get("ISIN"));
        if (isin is not null && symbol.Length > 0)
            data.SymbolToIsin[symbol] = isin;

        data.Trades.Add(new Trade(
            Symbol: symbol,
            Currency: currency,
            DateTime: dateTime,
            Quantity: Math.Abs(quantity),
            Price: price,
            Proceeds: Math.Abs(proceeds),
            Commission: Math.Abs(commission) + Math.Abs(taxes),
            CommissionCurrency: Blank(row.Get("IBCommissionCurrency")) ?? currency,
            RealizedPnL: realized,
            Type: type,
            Isin: isin,
            TransactionId: Blank(row.Get("TradeID")) ?? Blank(row.Get("TransactionID")),
            CountryCode: Blank(row.Get("IssuerCountryCode"))?.ToUpperInvariant()));
    }

    private void ParseCashTransaction(Row row, AccountData data)
    {
        var level = row.Get("LevelOfDetail");
        if (!string.IsNullOrEmpty(level) && !level.Equals("DETAIL", StringComparison.OrdinalIgnoreCase))
            return; // SUMMARY rows repeat the details per currency

        var type = row.Get("Type") ?? string.Empty;
        var description = row.Get("Description") ?? string.Empty;
        var currency = row.Get("CurrencyPrimary") ?? string.Empty;
        var symbol = Blank(row.Get("Symbol"));
        var isin = Blank(row.Get("ISIN"));
        var country = Blank(row.Get("IssuerCountryCode"))?.ToUpperInvariant();
        var actionId = Blank(row.Get("ActionID"));
        var transactionId = Blank(row.Get("TransactionID"));

        if (!TryParseFlexDateTime(row.Get("Date/Time") ?? row.Get("SettleDate") ?? row.Get("ReportDate"), out var bookingDateTime))
        {
            _logger.LogWarning("Skipping Flex cash row on line {Line}: could not parse date", row.LineNumber);
            data.Count("unreadable cash transaction");
            return;
        }
        var bookingDate = bookingDateTime.Date;
        // The three dates of a Flex cash row are kept apart: Date/Time is the booking, SettleDate the cash movement,
        // ReportDate the day the row entered the reports. A correction is dated back to the payment it corrects and
        // reported on the day it was made, so the report date is the evidence of a late correction.
        DateTime? settleDate = TryParseFlexDateTime(row.Get("SettleDate"), out var settle) ? settle.Date : null;
        DateTime? reportDate = TryParseFlexDateTime(row.Get("ReportDate"), out var report) ? report.Date : null;
        if (!TryParseDecimal(row.Get("Amount"), out var amount))
        {
            _logger.LogWarning("Skipping Flex cash row on line {Line}: could not parse amount '{Amount}'", row.LineNumber, row.Get("Amount"));
            data.Count("unreadable cash transaction");
            return;
        }

        if (isin is not null && symbol is not null)
            data.SymbolToIsin[symbol] = isin;

        switch (type)
        {
            case "Dividends":
            case "Payment In Lieu Of Dividends":
            {
                var kind = type.StartsWith("Payment", StringComparison.Ordinal) || description.Contains("Payment in Lieu", StringComparison.OrdinalIgnoreCase)
                    ? IncomeKind.PaymentInLieu
                    : IncomeKind.Dividend;
                data.Income.Add(new RawDividend(
                    ClassificationNote: kind == IncomeKind.PaymentInLieu ? IncomeClassification.PaymentInLieuNote : null,
                    Symbol: symbol ?? SymbolFromDescription(description) ?? "?",
                    Currency: currency,
                    Date: bookingDate,
                    Amount: amount,
                    Isin: isin,
                    Kind: kind,
                    TransactionId: transactionId,
                    CountryCode: country,
                    ActionId: actionId,
                    BookingDate: bookingDate,
                    RawType: type,
                    RawDescription: description));
                return;
            }
            case "Broker Interest Received":
            case "Bond Interest Received":
            {
                if (amount <= 0m)
                {
                    data.Count("negative interest");
                    return;
                }

                var isBond = type.StartsWith("Bond", StringComparison.Ordinal);
                var lending = IncomeClassification.IsSecuritiesLending(description);
                data.Income.Add(new RawDividend(
                    ClassificationNote: lending ? IncomeClassification.SecuritiesLendingNote : null,
                    Symbol: isBond && symbol is not null ? symbol : lending ? currency + " lending income" : currency + " interest",
                    Currency: currency,
                    Date: bookingDate,
                    Amount: amount,
                    Isin: isBond ? isin : null,
                    Kind: IncomeKind.Interest,
                    TransactionId: transactionId,
                    CountryCode: isBond ? country : null,
                    ActionId: actionId,
                    Reference: InterestPeriod(description),
                    BookingDate: bookingDate,
                    RawType: type,
                    RawDescription: description));
                if (isBond)
                {
                    data.Diagnostics.Add(new TaxDiagnostic(DiagnosticCodes.ClassificationUnverified, DiagnosticSeverity.Info,
                        $"Bond interest on {symbol ?? isin ?? "?"} ({bookingDate:yyyy-MM-dd}) is treated as interest under art. 30a ust. 1 pkt 2; verify the classification of the instrument.",
                        AffectedYears: [bookingDate.Year]));
                }
                return;
            }
            case "Withholding Tax":
            {
                // Flex books tax as a negative amount; Pitly keeps positive = withheld, negative = refunded.
                var withheld = -amount;
                IncomeKind kind;
                string eventSymbol;
                string? reference = null;
                if (isin is not null || symbol is not null)
                {
                    kind = description.Contains("Payment in Lieu", StringComparison.OrdinalIgnoreCase) ? IncomeKind.PaymentInLieu : IncomeKind.Dividend;
                    eventSymbol = symbol ?? SymbolFromDescription(description) ?? isin!;
                }
                else if (InterestRegex().IsMatch(description))
                {
                    kind = IncomeKind.Interest;
                    eventSymbol = currency + " interest";
                    reference = InterestPeriod(description);
                }
                else
                {
                    kind = IncomeKind.Unknown;
                    eventSymbol = description.Length > 40 ? description[..40] : description;
                }

                data.Withholding.Add(new RawWithholdingTax(
                    Symbol: eventSymbol,
                    Currency: currency,
                    Date: bookingDate,
                    Amount: withheld,
                    Isin: isin,
                    Kind: kind,
                    TransactionId: transactionId,
                    ActionId: actionId,
                    Reference: reference,
                    BookingDate: bookingDate,
                    SourceStatementYear: bookingDate.Year,
                    RawType: type,
                    RawDescription: description,
                    SettleDate: settleDate,
                    ReportDate: reportDate));
                return;
            }
            case "Broker Interest Paid":
            case "Bond Interest Paid":
            case "Deposits/Withdrawals":
            case "Deposits & Withdrawals":
            case "Other Fees":
            case "Commission Adjustments":
            case "Advisor Fees":
            case "Price Adjustments":
                return; // not income under art. 30a, not deductible for individuals
            default:
                data.Count("cash transaction of type '" + type + "'");
                return;
        }
    }

    private void ParseCorporateActionRow(Row row, AccountData data)
    {
        var assetClass = row.Get("AssetClass");
        if (!string.IsNullOrEmpty(assetClass) && !assetClass.Equals("STK", StringComparison.OrdinalIgnoreCase))
        {
            data.Count("corporate action of asset class " + assetClass);
            return;
        }

        var level = row.Get("LevelOfDetail");
        if (!string.IsNullOrEmpty(level) && !level.Equals("DETAIL", StringComparison.OrdinalIgnoreCase))
            return;

        if (!TryParseFlexDateTime(row.Get("Date/Time") ?? row.Get("ReportDate"), out var dateTime))
        {
            data.Count("unreadable corporate action");
            return;
        }

        var symbol = Blank(row.Get("Symbol"));
        var isin = Blank(row.Get("ISIN"));
        if (symbol is not null && isin is not null)
            data.SymbolToIsin[symbol] = isin;

        // Rebuilt in the layout of the Activity Statement's Corporate Actions section, which the Activity parser
        // already understands: Corporate Actions,Data,Stocks,Currency,ReportDate,Date/Time,Description,Quantity,Proceeds,Value,Realized P/L,Code
        data.CorporateActionRows.Add(
        [
            "Corporate Actions", "Data", "Stocks",
            row.Get("CurrencyPrimary") ?? string.Empty,
            dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dateTime.ToString("yyyy-MM-dd, HH:mm:ss", CultureInfo.InvariantCulture),
            row.Get("ActionDescription") ?? string.Empty,
            row.Get("Quantity") ?? "0",
            row.Get("Proceeds") ?? "0",
            row.Get("Value") ?? "0",
            row.Get("FifoPnlRealized") ?? "0",
            row.Get("Code") ?? string.Empty
        ]);
    }

    /// <summary>Rows of one distribution (same ActionID, or the same instrument, day and kind) are one payment; a reversed one disappears.</summary>
    private static List<RawDividend> NetIncome(List<RawDividend> income)
    {
        return income
            .GroupBy(d => string.IsNullOrEmpty(d.ActionId)
                ? "key|" + d.Symbol.ToUpperInvariant() + "|" + d.Isin + "|" + d.Currency + "|" + d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + d.Kind + "|" + d.Reference
                : "action|" + d.ActionId + "|" + d.Kind)
            .Select(g =>
            {
                var first = g.First();
                return first with
                {
                    Amount = g.Sum(d => d.Amount),
                    TransactionId = g.Count() == 1 ? first.TransactionId : null,
                    RawDescription = string.Join(" | ", g.Select(d => d.RawDescription).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct())
                };
            })
            .Where(d => d.Amount > 0m)
            .ToList();
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = Clean(header[i]);
            if (name.Length > 0 && !map.ContainsKey(name))
                map[name] = i;
        }

        return map;
    }

    private static bool TryParseFlexDateTime(string? value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var formats = new[]
        {
            "yyyyMMdd;HHmmss", "yyyyMMdd;HH:mm:ss", "yyyyMMdd", "yyyy-MM-dd;HH:mm:ss", "yyyy-MM-dd;HHmmss",
            "yyyy-MM-dd, HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyyMMddHHmmss"
        };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SymbolFromDescription(string description)
    {
        var match = SymbolRegex().Match(description);
        return match.Success ? match.Groups["symbol"].Value : null;
    }

    private static string? InterestPeriod(string description)
    {
        var match = PeriodRegex().Match(description);
        return match.Success ? match.Groups["month"].Value.ToUpperInvariant() + "-" + match.Groups["year"].Value : null;
    }

    [GeneratedRegex(@"^\s*(?<symbol>[A-Za-z0-9.\-]+)\s*\([A-Za-z]{2}[A-Za-z0-9]{9}[0-9]\)")]
    private static partial Regex SymbolRegex();

    [GeneratedRegex(@"\b(CREDIT\s+INT(EREST)?|INTEREST|INT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InterestRegex();

    [GeneratedRegex(@"\bFOR\s+(?<month>[A-Z]{3})-(?<year>\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRegex();

    private sealed class Row
    {
        private readonly List<string> _fields;
        private readonly Dictionary<string, int> _columns;

        public Row(List<string> fields, Dictionary<string, int> columns, int lineNumber)
        {
            _fields = fields;
            _columns = columns;
            LineNumber = lineNumber;
        }

        public int LineNumber { get; }

        public string? Get(string name)
        {
            if (!_columns.TryGetValue(name, out var index) || index >= _fields.Count)
                return null;
            return Clean(_fields[index]);
        }
    }
}

