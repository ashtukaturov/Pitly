using System.Globalization;
using System.Text;

namespace Pitly.Tests;

/// <summary>
/// Builds a synthetic Interactive Brokers Activity Flex Query (CSV): several sections, each with its own header row
/// starting with ClientAccountID, in the signs the real export uses. Every row names its account, so one file can
/// hold several accounts. No real statement data is involved.
/// </summary>
internal sealed class IbFlexCsv
{
    private const string TradesHeader =
        "\"ClientAccountID\",\"CurrencyPrimary\",\"AssetClass\",\"Symbol\",\"ISIN\",\"IssuerCountryCode\",\"TradeID\",\"DateTime\",\"TradeDate\",\"SettleDateTarget\",\"Quantity\",\"TradePrice\",\"Proceeds\",\"Taxes\",\"IBCommission\",\"IBCommissionCurrency\",\"Buy/Sell\",\"TransactionID\",\"LevelOfDetail\",\"FifoPnlRealized\"";
    private const string CashHeader =
        "\"ClientAccountID\",\"CurrencyPrimary\",\"AssetClass\",\"Symbol\",\"ISIN\",\"IssuerCountryCode\",\"Date/Time\",\"SettleDate\",\"ReportDate\",\"Amount\",\"Type\",\"TransactionID\",\"ActionID\",\"Description\",\"LevelOfDetail\"";
    private const string ActionsHeader =
        "\"ClientAccountID\",\"CurrencyPrimary\",\"AssetClass\",\"Symbol\",\"ISIN\",\"ReportDate\",\"Date/Time\",\"ActionDescription\",\"Quantity\",\"Proceeds\",\"Value\",\"FifoPnlRealized\",\"Code\",\"Type\",\"LevelOfDetail\",\"ActionID\"";

    private readonly List<string> _trades = [];
    private readonly List<string> _cash = [];
    private readonly List<string> _actions = [];
    // Shared by every builder so that two files of one test never repeat an id, as IB's ids never repeat.
    private static int _nextId = 1000;

    private static string NextId() => Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);

    /// <param name="dateTime">"yyyyMMdd;HHmmss" as the export writes it.</param>
    public IbFlexCsv Buy(string account, string symbol, string isin, string dateTime, decimal quantity, decimal price, decimal commission = 0m, string currency = "USD", string? tradeId = null)
        => Trade(account, symbol, isin, dateTime, quantity, price, commission, currency, tradeId, "BUY");

    public IbFlexCsv Sell(string account, string symbol, string isin, string dateTime, decimal quantity, decimal price, decimal commission = 0m, string currency = "USD", string? tradeId = null)
        => Trade(account, symbol, isin, dateTime, -quantity, price, commission, currency, tradeId, "SELL");

    private IbFlexCsv Trade(string account, string symbol, string isin, string dateTime, decimal signedQuantity, decimal price, decimal commission, string currency, string? tradeId, string side)
    {
        var day = dateTime[..8];
        var proceeds = -signedQuantity * price;
        _trades.Add(Q(account, currency, "STK", symbol, isin, isin[..2], tradeId ?? NextId(), dateTime, day, day, N(signedQuantity), N(price), N(proceeds), "0", N(-commission), currency, side, NextId(), "EXECUTION", "0"));
        return this;
    }

    public IbFlexCsv Dividend(string account, string symbol, string isin, string date, decimal amount, string actionId, string currency = "USD", string? transactionId = null)
    {
        _cash.Add(Q(account, currency, "STK", symbol, isin, isin[..2], date + ";202000", date, date, N(amount), "Dividends", transactionId ?? NextId(), actionId,
            $"{symbol}({isin}) CASH DIVIDEND {currency} 0.25 PER SHARE (Ordinary Dividend)", "DETAIL"));
        return this;
    }

    /// <summary>Tax on a dividend; a negative <paramref name="withheld"/> is a refund. Flex writes withheld tax as a negative amount.</summary>
    public IbFlexCsv Withholding(string account, string symbol, string isin, string date, decimal withheld, string actionId, string currency = "USD", string? transactionId = null)
    {
        _cash.Add(Q(account, currency, "STK", symbol, isin, isin[..2], date + ";202000", date, date, N(-withheld), "Withholding Tax", transactionId ?? NextId(), actionId,
            $"{symbol}({isin}) CASH DIVIDEND {currency} 0.25 PER SHARE - {isin[..2]} TAX", "DETAIL"));
        return this;
    }

    /// <summary>
    /// One Cash Transactions row written field by field: the type, the three dates and the description exactly as
    /// given, for rows the other builders do not produce (a payment in lieu, a refund reported in a later year).
    /// </summary>
    public IbFlexCsv CashRow(string account, string symbol, string isin, string dateTime, string settleDate, string reportDate, decimal amount, string type, string description, string actionId = "", string? transactionId = null, string currency = "USD")
    {
        _cash.Add(Q(account, currency, symbol.Length == 0 ? "CASH" : "STK", symbol, isin, isin.Length >= 2 ? isin[..2] : "", dateTime, settleDate, reportDate, N(amount), type, transactionId ?? NextId(), actionId, description, "DETAIL"));
        return this;
    }

    public IbFlexCsv Interest(string account, string date, decimal amount, string currency, string period, string? transactionId = null)
    {
        _cash.Add(Q(account, currency, "CASH", "", "", "", date + ";202000", date, date, N(amount), "Broker Interest Received", transactionId ?? NextId(), "",
            $"{currency} CREDIT INT FOR {period}", "DETAIL"));
        return this;
    }

    public IbFlexCsv InterestWithholding(string account, string date, decimal withheld, string currency, string period, string? transactionId = null)
    {
        _cash.Add(Q(account, currency, "CASH", "", "", "", date + ";202000", date, date, N(-withheld), "Withholding Tax", transactionId ?? NextId(), "",
            $"WITHHOLDING @ 10% ON CREDIT INT FOR {period}", "DETAIL"));
        return this;
    }

    /// <summary>A row of a type Pitly does not read (a deposit, a fee): must be reported or ignored, never counted.</summary>
    public IbFlexCsv OtherCash(string account, string date, decimal amount, string type, string currency = "USD")
    {
        _cash.Add(Q(account, currency, "CASH", "", "", "", date + ";202000", date, date, N(amount), type, NextId(), "", type.ToUpperInvariant(), "DETAIL"));
        return this;
    }

    public IbFlexCsv Split(string account, string symbol, string isin, string dateTime, int newShares, int oldShares, decimal creditedShares, string actionId)
    {
        var day = dateTime[..8];
        _actions.Add(Q(account, "USD", "STK", symbol, isin, day, dateTime, $"{symbol}({isin}) SPLIT {newShares} FOR {oldShares} ({symbol}, {symbol} INC, {isin})",
            N(creditedShares), "0", "0", "0", "", "FS", "DETAIL", actionId));
        return this;
    }

    public string Build()
    {
        var sb = new StringBuilder();
        void Section(string header, List<string> rows)
        {
            if (rows.Count == 0) return;
            sb.Append(header).Append("\r\n");
            foreach (var row in rows) sb.Append(row).Append("\r\n");
        }

        Section(TradesHeader, _trades);
        Section(CashHeader, _cash);
        Section(ActionsHeader, _actions);
        return sb.ToString();
    }

    private static string Q(params string[] fields) => string.Join(",", fields.Select(f => "\"" + f + "\""));

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Builds a synthetic Revolut consolidated savings statement (CSV): a summary and a transactions section per
/// currency, with a byte-order mark, CRLF line ends, quoted dates, currency signs and thousands separators.
/// </summary>
internal sealed class RevolutSavingsCsv
{
    private sealed class Account
    {
        public decimal Deposits, Withdrawals, Interest;
        public List<string> Rows { get; } = [];
    }

    private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);
    private readonly List<string> _currencies = [];
    private decimal? _summaryInterestOverride;

    private Account For(string currency)
    {
        if (!_accounts.TryGetValue(currency, out var account))
        {
            account = new Account();
            _accounts[currency] = account;
            _currencies.Add(currency);
        }

        return account;
    }

    public RevolutSavingsCsv Deposit(string currency, string date, decimal amount)
    {
        var a = For(currency);
        a.Deposits += amount;
        a.Rows.Add(Row(date, "Deposit", "Deposit", Money(currency, amount), Money(currency, Balance(a))));
        return this;
    }

    public RevolutSavingsCsv Interest(string currency, string date, decimal amount)
    {
        var a = For(currency);
        a.Interest += amount;
        a.Rows.Add(Row(date, "Interest earned", "Interest", Money(currency, amount), Money(currency, Balance(a))));
        return this;
    }

    public RevolutSavingsCsv Withdrawal(string currency, string date, decimal amount)
    {
        var a = For(currency);
        a.Withdrawals += amount;
        a.Rows.Add(Row(date, "Withdrawal", "Withdrawal", Money(currency, -amount), Money(currency, Balance(a))));
        return this;
    }

    public RevolutSavingsCsv Other(string currency, string date, string description, decimal amount)
    {
        var a = For(currency);
        a.Rows.Add(Row(date, description, description, Money(currency, amount), Money(currency, Balance(a))));
        return this;
    }

    /// <summary>Makes the summary disagree with the rows, as a truncated export would.</summary>
    public RevolutSavingsCsv SummaryInterest(decimal interest)
    {
        _summaryInterestOverride = interest;
        return this;
    }

    private static decimal Balance(Account a) => a.Deposits + a.Interest - a.Withdrawals;

    private static string Row(string date, string description, string type, string amount, string balance)
        => $"\"{date}\",\"{description}\",\"{type}\",\"{amount}\",\"{balance}\"";

    private static string Money(string currency, decimal amount)
    {
        var sign = amount < 0 ? "-" : string.Empty;
        var symbol = currency switch { "EUR" => "€", "GBP" => "£", "USD" => "$", _ => string.Empty };
        var suffix = currency == "PLN" ? " zł" : string.Empty;
        return sign + symbol + Math.Abs(amount).ToString("#,##0.00", CultureInfo.InvariantCulture) + suffix;
    }

    public string Build()
    {
        var sb = new StringBuilder("﻿");
        foreach (var currency in _currencies)
        {
            var a = _accounts[currency];
            sb.Append($"Summary for Savings Accounts - {currency}\r\n");
            sb.Append("Product,Start Date,End Date,Opening Balance,Deposits,Withdrawals,Interest earned,Closing Balance\r\n");
            sb.Append($"\"Savings {currency}\",\"Jan 1, 2025\",\"Dec 31, 2025\",\"{Money(currency, 0m)}\",\"{Money(currency, a.Deposits)}\",\"{Money(currency, a.Withdrawals)}\",\"{Money(currency, _summaryInterestOverride ?? a.Interest)}\",\"{Money(currency, Balance(a))}\"\r\n");
            sb.Append("\r\n");
            sb.Append($"Transactions for Savings Accounts - {currency}\r\n");
            sb.Append("Date,Description,Type,Amount,Balance\r\n");
            foreach (var row in a.Rows)
                sb.Append(row).Append("\r\n");
            sb.Append("\r\n");
        }

        return sb.ToString();
    }
}
