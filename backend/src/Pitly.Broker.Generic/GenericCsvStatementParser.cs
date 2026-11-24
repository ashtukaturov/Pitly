using System.Globalization;
using Microsoft.Extensions.Logging;
using Pitly.Core.Models;
using Pitly.Core.Parsing;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Broker.Generic;

/// <summary>Which of the two generic templates a file follows.</summary>
public enum GenericCsvKind
{
    /// <summary>Trades template: one BUY or SELL per row.</summary>
    Trades,
    /// <summary>Income template: one dividend, interest or payment-in-lieu row per payment.</summary>
    Income
}

/// <summary>
/// Parses the two generic CSV templates (the "generic trades" and "generic income" templates of the PIT-38
/// assistant) for brokers Pitly has no native parser for. The user fills the template by hand or with a script,
/// so the parser is strict: a value it cannot read with certainty is an error naming the line, never a guess.
///
/// Trades template:   broker,[account_id,]tx_id,direction,symbol,isin,country,currency,price,quantity,amount,commission,operation_datetime,settlement_date
/// Income template:   broker,[account_id,]tx_id,income_type,symbol,isin,country,currency,gross_amount,wht_amount,operation_datetime,settlement_date
///
/// Every distinct pair of <c>broker</c> and <c>account_id</c> is its own statement and its own account
/// (<see cref="ParsedStatement.AccountId"/>): rows of two accounts never share FIFO lots or withholding. The
/// older template without <c>account_id</c> names only the broker; a broker name is not an account number, so
/// such a statement carries no account id and the import treats it like any file without one (the user can
/// label it, and several such files of one broker are reported as an assumption, never merged silently).
/// </summary>
public sealed class GenericCsvStatementParser : IStatementParser
{
    public const string FormatName = "Generic CSV";
    public const string ParserVersion = "generic-csv/2";

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd"
    ];

    private static readonly string[] CommonRequiredColumns = ["broker", "currency", "operation_datetime"];
    private static readonly string[] TradesRequiredColumns = ["direction", "symbol", "quantity", "price"];
    private static readonly string[] IncomeRequiredColumns = ["income_type", "gross_amount"];

    // Numbers use a dot as the decimal separator and no thousands separator: "1,50" is rejected instead of being
    // read as 150, and "1,500.00" is rejected instead of being read as 1500.
    private const NumberStyles StrictNumber =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private readonly ILogger<GenericCsvStatementParser> _logger;

    public GenericCsvStatementParser(ILogger<GenericCsvStatementParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tells from the header line whether a file follows one of the generic templates. Null for any other format.
    /// </summary>
    public static GenericCsvKind? DetectKind(string headerLine)
    {
        var names = ParseCsvLine(headerLine.TrimEnd('\r')).Select(NormalizeHeaderName).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("broker"))
            return null;
        if (names.Contains("direction"))
            return GenericCsvKind.Trades;
        if (names.Contains("income_type"))
            return GenericCsvKind.Income;
        return null;
    }

    /// <summary>
    /// The file as one statement. A file whose rows name several brokers is several accounts and cannot be one
    /// statement: use <see cref="ParseAll"/>, which the import does.
    /// </summary>
    public ParsedStatement Parse(string content)
    {
        var statements = ParseAll(content);
        if (statements.Count == 1)
            return statements[0];

        throw new FormatException(
            $"The file lists {statements.Count} accounts ({string.Join(", ", statements.Select(s => s.AccountId is null ? s.Broker : s.Broker + " " + s.AccountId))}); " +
            "each is a separate account and has to be parsed as a separate statement.");
    }

    /// <summary>One statement per distinct <c>broker</c> value, in the order the brokers first appear.</summary>
    public IReadOnlyList<ParsedStatement> ParseAll(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new FormatException("File is empty.");

        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var headerLine = lines[0];
        if (!headerLine.Contains(',') && headerLine.Contains(';'))
        {
            throw new FormatException(
                "The generic CSV template must use a comma as the column separator and a dot as the decimal separator " +
                "(the file uses semicolons). Save it again with those settings.");
        }

        var kind = DetectKind(headerLine)
            ?? throw new FormatException(
                "File does not follow the generic trades or income template: the header must contain 'broker' and " +
                "either 'direction' (trades) or 'income_type' (income).");

        var columns = BuildColumnMap(headerLine);
        var missing = CommonRequiredColumns
            .Concat(kind == GenericCsvKind.Trades ? TradesRequiredColumns : IncomeRequiredColumns)
            .Where(c => !columns.ContainsKey(c))
            .ToList();
        if (missing.Count > 0)
        {
            throw new FormatException(
                $"The generic {kind.ToString().ToLowerInvariant()} template is missing the column{(missing.Count > 1 ? "s" : string.Empty)} " +
                string.Join(", ", missing.Select(m => "'" + m + "'")) + ".");
        }

        // Rows are grouped by broker and account; the order of first appearance is kept so that messages read like the file.
        var hasAccountColumn = columns.ContainsKey("account_id");
        var groups = new List<(string Broker, string? Account)>();
        var rowsByGroup = new Dictionary<string, List<(List<string> Fields, int LineNumber)>>(StringComparer.OrdinalIgnoreCase);
        var accountsByBroker = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            if (fields.All(f => string.IsNullOrWhiteSpace(Clean(f))))
                continue;

            var lineNumber = i + 1;
            var broker = GetField(fields, columns, "broker")
                ?? throw new FormatException($"Line {lineNumber}: 'broker' is empty. Every row needs the broker it belongs to.");
            var account = hasAccountColumn ? GetField(fields, columns, "account_id") : null;

            var key = broker + KeySeparator + (account ?? string.Empty);
            if (!rowsByGroup.TryGetValue(key, out var rows))
            {
                rows = [];
                rowsByGroup[key] = rows;
                groups.Add((broker, account));
            }

            rows.Add((fields, lineNumber));

            if (!accountsByBroker.TryGetValue(broker, out var accounts))
                accountsByBroker[broker] = accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            accounts.Add(account ?? string.Empty);
        }

        if (groups.Count == 0)
            throw new FormatException("The generic CSV file has a header but no data rows.");

        // A broker whose rows sometimes name an account and sometimes do not cannot be split into accounts with
        // certainty; the user has to fill the column for every row of that broker.
        foreach (var (broker, accounts) in accountsByBroker)
        {
            if (accounts.Contains(string.Empty) && accounts.Count > 1)
            {
                throw new FormatException(
                    $"Broker {broker}: some rows name an account_id ({string.Join(", ", accounts.Where(a => a.Length > 0))}) and some leave it empty. " +
                    "Fill account_id for every row of that broker, or leave it empty for all of them.");
            }
        }

        var statements = groups
            .Select(g => BuildStatement(kind, g.Broker, g.Account, rowsByGroup[g.Broker + KeySeparator + (g.Account ?? string.Empty)], columns))
            .ToList();

        _logger.LogInformation(
            "Parsed generic {Kind} CSV: {Accounts} account(s), {Trades} trades, {Income} income rows",
            kind, statements.Count, statements.Sum(s => s.Trades.Count), statements.Sum(s => s.Dividends.Count));

        return statements;
    }

    // Unit separator: cannot occur in a broker name or account id.
    private const char KeySeparator = (char)31;

    private static ParsedStatement BuildStatement(
        GenericCsvKind kind,
        string broker,
        string? account,
        List<(List<string> Fields, int LineNumber)> rows,
        Dictionary<string, int> columns)
    {
        var trades = new List<Trade>();
        var income = new List<RawDividend>();
        var warnings = new List<string>();
        var transactionIds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (fields, lineNumber) in rows)
        {
            var row = new Row(fields, columns, lineNumber);
            if (kind == GenericCsvKind.Trades)
                trades.Add(ParseTrade(row, warnings));
            else
                income.Add(ParseIncome(row, warnings));

            var txId = row.Get("tx_id");
            if (txId is not null)
                transactionIds[txId] = transactionIds.GetValueOrDefault(txId) + 1;
        }

        var who = account is null ? broker : broker + " " + account;
        foreach (var (txId, count) in transactionIds.Where(kv => kv.Value > 1))
        {
            warnings.Add(
                $"{who}: tx_id '{txId}' appears {count} times in the file. All rows were kept; " +
                "check that they are different operations and not the same one listed twice.");
        }

        var years = trades.Select(t => t.DateTime.Year).Concat(income.Select(d => d.Date.Year)).ToList();

        return new ParsedStatement(
            trades,
            income,
            WithholdingTaxes: [],
            StatementYear: years.Max(),
            Warnings: warnings,
            // account_id when the template has it; a broker name alone is not an account number.
            AccountId: account,
            Broker: broker,
            ParserVersion: ParserVersion);
    }

    private static Trade ParseTrade(Row row, List<string> warnings)
    {
        var direction = row.Require("direction").ToUpperInvariant();
        var type = direction switch
        {
            "BUY" => TradeType.Buy,
            "SELL" => TradeType.Sell,
            _ => throw new FormatException(
                $"Line {row.LineNumber}: direction '{direction}' is not supported. Use BUY or SELL.")
        };

        var symbol = row.Require("symbol");
        var isin = row.Isin();
        var country = row.Country(symbol, isin, warnings);
        var dateTime = row.DateTime("operation_datetime");
        var settleDate = row.OptionalDate("settlement_date");
        var currency = row.Currency();

        var quantity = row.Decimal("quantity");
        if (quantity <= 0)
            throw new FormatException($"Line {row.LineNumber}: quantity must be positive (got {N(quantity)}); the direction says whether it is a buy or a sell.");

        var price = row.Money("price", currency);
        if (price < 0)
            throw new FormatException($"Line {row.LineNumber}: price cannot be negative (got {N(price)}).");

        var commission = row.OptionalMoney("commission", currency) ?? 0m;
        if (commission < 0)
        {
            throw new FormatException(
                $"Line {row.LineNumber}: commission cannot be negative (got {N(commission)}). " +
                "A rebate is not a cost; leave the column empty or enter the fee actually paid.");
        }

        // The gross value of the trade. The broker's own figure is used when it is given; price x quantity only
        // fills in for it, and a figure that does not agree with price x quantity is reported for the user to check.
        var computed = price * quantity;
        var amount = row.OptionalMoney("amount", currency);
        if (amount is null)
        {
            amount = computed;
        }
        else
        {
            if (amount < 0)
                throw new FormatException($"Line {row.LineNumber}: amount cannot be negative (got {N(amount.Value)}).");

            var tolerance = 0.01m * quantity + 0.01m;
            if (Math.Abs(amount.Value - computed) > tolerance)
            {
                warnings.Add(
                    $"{row.Broker}: {direction} {symbol} on {dateTime:yyyy-MM-dd} (line {row.LineNumber}) has amount {N(amount.Value)} {currency.Code} " +
                    $"but price x quantity gives {N(computed)} {currency.Code}. The amount was used as the value of the trade; check the row.");
            }
        }

        return new Trade(
            Symbol: symbol,
            Currency: currency.Code,
            DateTime: dateTime,
            Quantity: quantity,
            Price: price,
            Proceeds: amount.Value,
            Commission: commission,
            CommissionCurrency: currency.Code,
            RealizedPnL: 0m,
            Type: type,
            Isin: isin,
            TransactionId: row.Get("tx_id"),
            CountryCode: country,
            SettleDate: settleDate);
    }

    private static RawDividend ParseIncome(Row row, List<string> warnings)
    {
        var rawType = row.Require("income_type");
        var kind = NormalizeIncomeType(rawType) switch
        {
            "DIVIDEND" => IncomeKind.Dividend,
            "INTEREST" => IncomeKind.Interest,
            "PAYMENT_IN_LIEU" => IncomeKind.PaymentInLieu,
            _ => throw new FormatException(
                $"Line {row.LineNumber}: income_type '{rawType}' is not supported. Use DIVIDEND, INTEREST or PAYMENT_IN_LIEU.")
        };

        var currency = row.Currency();
        var isin = row.Isin();
        var symbol = row.Get("symbol");
        if (symbol is null)
        {
            if (kind != IncomeKind.Interest)
                throw new FormatException($"Line {row.LineNumber}: a {rawType} row needs a symbol.");
            symbol = currency.Code + " interest";
        }

        var country = row.Country(symbol, isin, warnings);
        var date = row.DateTime("operation_datetime").Date;
        row.OptionalDate("settlement_date");

        var gross = row.Money("gross_amount", currency);
        if (gross <= 0)
        {
            throw new FormatException(
                $"Line {row.LineNumber}: gross_amount must be positive (got {N(gross)}). " +
                "A reversed or negative payment is not supported; remove both rows or net them by hand.");
        }

        var withheld = row.OptionalMoney("wht_amount", currency) ?? 0m;
        if (withheld < 0)
            throw new FormatException($"Line {row.LineNumber}: wht_amount cannot be negative (got {N(withheld)}).");
        if (withheld > gross)
        {
            throw new FormatException(
                $"Line {row.LineNumber}: wht_amount {N(withheld)} exceeds gross_amount {N(gross)}. " +
                "gross_amount is the payment before tax; enter the tax withheld on it in wht_amount.");
        }

        // The tax withheld is printed in the row of its payment, so the pair stays together.
        return new RawDividend(
            Symbol: symbol,
            Currency: currency.Code,
            Date: date,
            Amount: gross,
            Isin: isin,
            Kind: kind,
            TransactionId: row.Get("tx_id"),
            WithheldAmount: withheld > 0 ? withheld : null,
            WithheldCurrency: withheld > 0 ? currency.Code : null,
            CountryCode: country,
            ClassificationNote: kind == IncomeKind.PaymentInLieu ? IncomeClassification.PaymentInLieuNote : null);
    }

    private static string NormalizeIncomeType(string value)
        => string.Join("_", value.ToUpperInvariant().Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, int> BuildColumnMap(string headerLine)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = ParseCsvLine(headerLine);
        for (var i = 0; i < names.Count; i++)
        {
            var name = NormalizeHeaderName(names[i]);
            if (name.Length == 0)
                continue;
            if (map.ContainsKey(name))
                throw new FormatException($"The header lists the column '{name}' twice.");
            map[name] = i;
        }

        return map;
    }

    private static string NormalizeHeaderName(string header)
        => Clean(header).ToLowerInvariant().Replace(' ', '_');

    private static string? GetField(List<string> fields, Dictionary<string, int> columns, string name)
    {
        if (!columns.TryGetValue(name, out var index) || index >= fields.Count)
            return null;
        var value = Clean(fields[index]);
        return value.Length == 0 ? null : value;
    }

    private static string N(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>A currency code and the factor that turns the row's amounts into units of that currency.</summary>
    private sealed record Money(string Code, decimal Factor);

    private sealed class Row
    {
        private readonly List<string> _fields;
        private readonly Dictionary<string, int> _columns;

        public Row(List<string> fields, Dictionary<string, int> columns, int lineNumber)
        {
            _fields = fields;
            _columns = columns;
            LineNumber = lineNumber;
            Broker = Get("broker") ?? string.Empty;
        }

        public int LineNumber { get; }
        public string Broker { get; }

        public string? Get(string name) => GetField(_fields, _columns, name);

        public string Require(string name)
            => Get(name) ?? throw new FormatException($"Line {LineNumber}: '{name}' is empty.");

        public decimal Decimal(string name) => ParseDecimal(name, Require(name));

        public decimal? OptionalDecimal(string name)
        {
            var raw = Get(name);
            return raw is null ? null : ParseDecimal(name, raw);
        }

        public decimal Money(string name, Money currency) => Decimal(name) * currency.Factor;

        public decimal? OptionalMoney(string name, Money currency) => OptionalDecimal(name) * currency.Factor;

        private decimal ParseDecimal(string name, string raw)
        {
            if (decimal.TryParse(raw, StrictNumber, CultureInfo.InvariantCulture, out var value))
                return value;
            throw new FormatException(
                $"Line {LineNumber}: '{name}' value '{raw}' is not a number. Use a dot as the decimal separator and no thousands separator.");
        }

        public DateTime DateTime(string name)
        {
            var raw = Require(name);
            if (System.DateTime.TryParseExact(raw, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                return value;
            throw new FormatException(
                $"Line {LineNumber}: '{name}' value '{raw}' is not a date. Use YYYY-MM-DD or YYYY-MM-DD HH:MM:SS.");
        }

        /// <summary>
        /// The date when the column is filled. Pitly converts at the rate of the day before the operation date and
        /// counts the row in the year of that date; a settlement date is kept for the audit trail and the year-end check.
        /// </summary>
        public DateTime? OptionalDate(string name)
            => Get(name) is null ? null : DateTime(name);

        public Money Currency()
        {
            var code = Require("currency").ToUpperInvariant();
            if (code.Length != 3 || !code.All(char.IsLetter))
                throw new FormatException($"Line {LineNumber}: currency '{code}' is not a three-letter ISO code.");
            // Pence sterling: amounts are divided by 100 and the row is treated as GBP, as for Trading 212.
            return code == "GBX" ? new Money("GBP", 0.01m) : new Money(code, 1m);
        }

        public string? Isin()
        {
            var isin = Get("isin")?.ToUpperInvariant();
            if (isin is null)
                return null;
            if (isin.Length != 12 || !isin.All(char.IsLetterOrDigit) || !char.IsLetter(isin[0]) || !char.IsLetter(isin[1]))
                throw new FormatException($"Line {LineNumber}: isin '{isin}' is not a 12-character ISIN (two letters, nine characters, a check digit).");
            return isin;
        }

        /// <summary>
        /// The country column, upper-cased. It is the source country for the PIT/ZG annex. When it disagrees
        /// with the ISIN prefix the row is reported, because one of the two is wrong unless the instrument is a
        /// depositary receipt.
        /// </summary>
        public string? Country(string symbol, string? isin, List<string> warnings)
        {
            var country = Get("country")?.ToUpperInvariant();
            if (country is null)
                return null;
            if (country.Length != 2 || !country.All(char.IsLetter))
                throw new FormatException($"Line {LineNumber}: country '{country}' is not a two-letter ISO code.");

            if (isin is not null && !isin.StartsWith(country, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"{Broker}: {symbol} (line {LineNumber}) has country {country} but ISIN {isin}, which points to {isin[..2]}. " +
                    $"PIT/ZG uses {country}; the treaty cap on the foreign tax credit still follows the ISIN. Check the row unless the instrument is a depositary receipt.");
            }

            return country;
        }
    }
}
