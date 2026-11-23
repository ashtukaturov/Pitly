using System.Globalization;
using System.Text;

namespace Pitly.Tests;

/// <summary>
/// Builds a synthetic Interactive Brokers Activity Statement (CSV) row by row, in the layout the real export uses.
/// No real statement data is involved; amounts are chosen so that results can be checked by hand.
/// </summary>
internal sealed class IbStatement
{
    private readonly string _account;
    private readonly string _period;
    private readonly List<string> _instruments = [];
    private readonly List<string> _trades = [];
    private readonly List<string> _dividends = [];
    private readonly List<string> _withholding = [];
    private readonly List<string> _interest = [];
    private readonly List<string> _actions = [];
    private readonly List<string> _openingPositions = [];
    private readonly List<string> _filler = [];

    public IbStatement(string account, int year)
        : this(account, $"January 1, {year} - December 31, {year}")
    {
    }

    public IbStatement(string account, string period)
    {
        _account = account;
        _period = period;
    }

    public IbStatement Instrument(string symbol, string isin)
    {
        _instruments.Add($"Financial Instrument Information,Data,Stocks,{symbol},{symbol} INC,1,{isin},{symbol},NASDAQ,1,COMMON,");
        return this;
    }

    public IbStatement Buy(string symbol, string dateTime, decimal quantity, decimal price, string currency = "USD")
        => Trade(symbol, dateTime, quantity, price, currency);

    public IbStatement Sell(string symbol, string dateTime, decimal quantity, decimal price, string currency = "USD")
        => Trade(symbol, dateTime, -quantity, price, currency);

    private IbStatement Trade(string symbol, string dateTime, decimal signedQuantity, decimal price, string currency)
    {
        var proceeds = -signedQuantity * price;
        _trades.Add($"Trades,Data,Order,Stocks,{currency},{symbol},\"{dateTime}\",{N(signedQuantity)},{N(price)},{N(price)},{N(proceeds)},0,{N(-proceeds)},0,0,O");
        return this;
    }

    public IbStatement Dividend(string symbol, string isin, string date, decimal amount, string currency = "USD")
    {
        _dividends.Add($"Dividends,Data,{currency},{date},{symbol}({isin}) Cash Dividend {currency} 0.25 per Share (Ordinary Dividend),{N(amount)}");
        return this;
    }

    /// <summary>Tax withheld on a dividend; a negative <paramref name="withheld"/> is a refund booked later.</summary>
    public IbStatement Withholding(string symbol, string isin, string date, decimal withheld, string currency = "USD")
    {
        _withholding.Add($"Withholding Tax,Data,{currency},{date},{symbol}({isin}) Cash Dividend {currency} 0.25 per Share - US Tax,{N(-withheld)},");
        return this;
    }

    public IbStatement Interest(string date, decimal amount, string currency = "USD")
    {
        _interest.Add($"Interest,Data,{currency},{date},{currency} Credit Interest for the month,{N(amount)}");
        return this;
    }

    /// <summary>Forward split: <paramref name="creditedShares"/> extra shares are booked on the account.</summary>
    public IbStatement Split(string symbol, string isin, string dateTime, int newShares, int oldShares, decimal creditedShares)
    {
        _actions.Add($"Corporate Actions,Data,Stocks,USD,{dateTime[..10]},\"{dateTime}\",\"{symbol}({isin}) Split {newShares} for {oldShares} ({symbol}, {symbol} INC, {isin})\",{N(creditedShares)},0,0,0,");
        return this;
    }

    /// <summary>Position the statement reports at the start of its year (Mark-to-Market "Prior Quantity").</summary>
    public IbStatement OpeningPosition(string symbol, decimal quantity)
    {
        _openingPositions.Add($"Mark-to-Market Performance Summary,Data,Stocks,{symbol},{N(quantity)},{N(quantity)},100,100,0,0,0,0,0,");
        return this;
    }

    /// <summary>Appends rows the parser ignores until the file is at least <paramref name="bytes"/> long.</summary>
    public IbStatement PadTo(int bytes)
    {
        const string row = "Notes/Legal Notes,Data,Synthetic filler row used only to give the test file a realistic size 0123456789";
        var current = Encoding.UTF8.GetByteCount(Build());
        while (current < bytes)
        {
            _filler.Add(row);
            current += row.Length + 1;
        }

        return this;
    }

    public string Build()
    {
        var sb = new StringBuilder();
        sb.Append("Statement,Header,Field Name,Field Value\n");
        sb.Append("Statement,Data,BrokerName,Interactive Brokers\n");
        sb.Append($"Statement,Data,Period,\"{_period}\"\n");
        sb.Append("Account Information,Header,Field Name,Field Value\n");
        sb.Append($"Account Information,Data,Account,{_account}\n");
        Section(sb, "Mark-to-Market Performance Summary,Header,Asset Category,Symbol,Prior Quantity,Current Quantity,Prior Price,Current Price,Mark-to-Market P/L Position,Mark-to-Market P/L Transaction,Mark-to-Market P/L Commissions,Mark-to-Market P/L Other,Mark-to-Market P/L Total,Code", _openingPositions);
        Section(sb, "Trades,Header,DataDiscriminator,Asset Category,Currency,Symbol,Date/Time,Quantity,T. Price,C. Price,Proceeds,Comm/Fee,Basis,Realized P/L,MTM P/L,Code", _trades);
        Section(sb, "Corporate Actions,Header,Asset Category,Currency,Report Date,Date/Time,Description,Quantity,Proceeds,Value,Realized P/L,Code", _actions);
        Section(sb, "Dividends,Header,Currency,Date,Description,Amount", _dividends);
        Section(sb, "Withholding Tax,Header,Currency,Date,Description,Amount,Code", _withholding);
        Section(sb, "Interest,Header,Currency,Date,Description,Amount", _interest);
        Section(sb, "Financial Instrument Information,Header,Asset Category,Symbol,Description,Conid,Security ID,Underlying,Listing Exch,Multiplier,Type,Code", _instruments);
        Section(sb, "Notes/Legal Notes,Header,Note", _filler);
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string header, List<string> rows)
    {
        if (rows.Count == 0)
            return;
        sb.Append(header).Append('\n');
        foreach (var row in rows)
            sb.Append(row).Append('\n');
    }

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Builds a synthetic Trading 212 history export (CSV). The export carries no account number.</summary>
internal sealed class Trading212Export
{
    private const string Header =
        "Action,Time,ISIN,Ticker,Name,Notes,ID,No. of shares,Price / share,Currency (Price / share),Exchange rate,Result,Currency (Result),Total,Currency (Total),Withholding tax,Currency (Withholding tax),Currency conversion fee,Currency (Currency conversion fee)";

    private readonly List<string> _rows = [];

    public Trading212Export Buy(string id, string time, string ticker, string isin, decimal shares, decimal price)
        => Row("Market buy", id, time, ticker, isin, shares, price, withholding: null);

    public Trading212Export Sell(string id, string time, string ticker, string isin, decimal shares, decimal price)
        => Row("Market sell", id, time, ticker, isin, shares, price, withholding: null);

    public Trading212Export Dividend(string id, string time, string ticker, string isin, decimal shares, decimal perShare, decimal withholding)
        => Row("Dividend (Dividend)", id, time, ticker, isin, shares, perShare, withholding);

    private Trading212Export Row(string action, string id, string time, string ticker, string isin, decimal shares, decimal price, decimal? withholding)
    {
        var w = withholding is null ? "," : N(withholding.Value) + ",USD";
        _rows.Add($"{action},{time},{isin},{ticker},{ticker} Inc,,{id},{N(shares)},{N(price)},USD,4.00,,,{N(shares * price * 4m)},PLN,{w},,");
        return this;
    }

    public string Build() => Header + "\n" + string.Join("\n", _rows) + "\n";

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Builds a file in the generic trades template. The broker column is the account.</summary>
internal sealed class GenericTradesCsv
{
    public const string Header =
        "broker,tx_id,direction,symbol,isin,country,currency,price,quantity,amount,commission,operation_datetime,settlement_date";

    private readonly List<string> _rows = [];

    public GenericTradesCsv Buy(string broker, string id, string dateTime, string symbol, string isin, decimal quantity, decimal price, decimal commission = 0m, string currency = "USD", string? country = null)
        => Row(broker, id, "BUY", dateTime, symbol, isin, quantity, price, commission, currency, country);

    public GenericTradesCsv Sell(string broker, string id, string dateTime, string symbol, string isin, decimal quantity, decimal price, decimal commission = 0m, string currency = "USD", string? country = null)
        => Row(broker, id, "SELL", dateTime, symbol, isin, quantity, price, commission, currency, country);

    private GenericTradesCsv Row(string broker, string id, string direction, string dateTime, string symbol, string isin, decimal quantity, decimal price, decimal commission, string currency, string? country)
    {
        _rows.Add($"{broker},{id},{direction},{symbol},{isin},{country ?? isin[..2]},{currency},{N(price)},{N(quantity)},{N(price * quantity)},{N(commission)},{dateTime},{dateTime[..10]}");
        return this;
    }

    public string Build() => Header + "\n" + string.Join("\n", _rows) + "\n";

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Builds a file in the generic income template: dividends and interest with the tax withheld in the same row.</summary>
internal sealed class GenericIncomeCsv
{
    public const string Header =
        "broker,tx_id,income_type,symbol,isin,country,currency,gross_amount,wht_amount,operation_datetime,settlement_date";

    private readonly List<string> _rows = [];

    public GenericIncomeCsv Dividend(string broker, string id, string date, string symbol, string isin, decimal gross, decimal withheld, string currency = "USD", string? country = null)
        => Row(broker, id, "DIVIDEND", date, symbol, isin, country ?? isin[..2], gross, withheld, currency);

    /// <summary>Interest has no ISIN; the country is the one the statement names.</summary>
    public GenericIncomeCsv Interest(string broker, string id, string date, string symbol, string country, decimal gross, decimal withheld, string currency)
        => Row(broker, id, "INTEREST", date, symbol, isin: string.Empty, country, gross, withheld, currency);

    private GenericIncomeCsv Row(string broker, string id, string type, string date, string symbol, string isin, string country, decimal gross, decimal withheld, string currency)
    {
        _rows.Add($"{broker},{id},{type},{symbol},{isin},{country},{currency},{N(gross)},{N(withheld)},{date} 00:00:00,{date}");
        return this;
    }

    public string Build() => Header + "\n" + string.Join("\n", _rows) + "\n";

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
