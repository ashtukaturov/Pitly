using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.Trading212;
using Pitly.Core.Models;

namespace Pitly.Tests;

public class Trading212StatementParserTests
{
    private static readonly Trading212StatementParser Parser =
        new(NullLogger<Trading212StatementParser>.Instance);

    [Fact]
    public void Parse_ParsesTrading212SampleRowsIncludingGbxAndDividend()
    {
        var csv = """
                  Action,Time,ISIN,Ticker,Name,Notes,ID,No. of shares,Price / share,Currency (Price / share),Exchange rate,Result,Currency (Result),Total,Currency (Total),Withholding tax,Currency (Withholding tax),Currency conversion fee,Currency (Currency conversion fee)
                  Deposit,2025-01-22 14:45:58,,,,"Transaction ID: 1",1,,,,,,,100.00,PLN,,,,
                  Market buy,2025-01-31 09:26:13,JE00BN2CJ301,GLDW,WisdomTree Core Physical Gold,,2,0.0237351700,22409.0000000000,GBX,19.85372991,,,26.83,PLN,,,0.04,PLN
                  Market sell,2025-02-10 09:01:32,JE00BN2CJ301,GLDW,WisdomTree Core Physical Gold,,3,0.0237351700,23232.0000000000,GBX,19.89586090,1.69,PLN,27.57,PLN,,,0.05,PLN
                  Dividend (Dividend),2025-12-23 12:18:17,US30303M1027,META,Meta Platforms,,4,0.2289742100,0.446250,USD,3.58310000,,,0.37,PLN,0.02,USD,,
                  Withdrawal,2025-12-24 12:18:17,,,,"Sent to card",5,,,,,,,-50.00,PLN,,,,
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Equal(2, parsed.Trades.Count);
        Assert.Single(parsed.Dividends);
        // Withholding stays attached to its dividend row instead of becoming a separate row to re-match.
        Assert.Empty(parsed.WithholdingTaxes);
        Assert.Null(parsed.AccountId);
        Assert.Equal("Trading 212", parsed.Broker);

        var firstTrade = parsed.Trades[0];
        Assert.Equal(TradeType.Buy, firstTrade.Type);
        Assert.Equal("GLDW", firstTrade.Symbol);
        Assert.Equal("JE00BN2CJ301", firstTrade.Isin);
        Assert.Equal("GBP", firstTrade.Currency);
        Assert.Equal(224.09m, firstTrade.Price);
        Assert.Equal(0.04m, firstTrade.Commission);
        Assert.Equal("PLN", firstTrade.CommissionCurrency);

        var dividend = parsed.Dividends[0];
        Assert.Equal("US30303M1027", dividend.Isin);
        Assert.Equal(0.1021797412125m, dividend.Amount);

        Assert.Equal("USD", dividend.WithheldCurrency);
        Assert.Equal(0.02m, dividend.WithheldAmount);
        Assert.Equal("4", dividend.TransactionId);
        Assert.Equal("2", firstTrade.TransactionId);
    }

    [Fact]
    public void Parse_ParsesInterestOnCashAsInterestIncome()
    {
        var csv = """
                  Action,Time,ISIN,Ticker,Name,Notes,ID,No. of shares,Price / share,Currency (Price / share),Exchange rate,Result,Currency (Result),Total,Currency (Total),Withholding tax,Currency (Withholding tax),Currency conversion fee,Currency (Currency conversion fee)
                  Interest on cash,2025-03-01 00:10:00,,,,,6,,,,,,,1.23,PLN,,,,
                  Lending interest,2025-03-15 00:10:00,,,,,7,,,,,,,0.40,PLN,,,,
                  Interest on cash,2025-04-01 00:10:00,,,,,8,,,,,,,-0.50,PLN,,,,
                  Dividend (Dividend),2025-12-23 12:18:17,US30303M1027,META,Meta Platforms,,4,0.2289742100,0.446250,USD,3.58310000,,,0.37,PLN,0.02,USD,,
                  """;

        var parsed = Parser.Parse(csv);

        var interest = parsed.Dividends.Where(d => d.Kind == IncomeKind.Interest).ToList();
        Assert.Equal(2, interest.Count);
        Assert.All(interest, i => Assert.Equal("PLN", i.Currency));
        Assert.Equal(1.23m, interest[0].Amount);
        Assert.Equal(new DateTime(2025, 3, 1), interest[0].Date);
        Assert.Equal(0.40m, interest[1].Amount);

        Assert.Single(parsed.Dividends, d => d.Kind == IncomeKind.Dividend);
    }

    [Fact]
    public void Parse_UsesCurrencyCodeFromHeaderForFeeColumns()
    {
        var csv = """
                  Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,Currency (Price / share),Exchange rate,Result (EUR),Total (EUR),Currency conversion fee (EUR)
                  Market buy,2025-01-02 10:00:00,US0000000001,TEST,Test Corp,1.0000000000,100.0000000000,USD,0.25000000,,400.00,0.15
                  """;

        var parsed = Parser.Parse(csv);
        var trade = Assert.Single(parsed.Trades);

        Assert.Equal(0.15m, trade.Commission);
        Assert.Equal("EUR", trade.CommissionCurrency);
    }

    [Fact]
    public void Parse_PairsStockSplitOpenAndCloseRowsIntoOneSplit()
    {
        var csv = """
                  Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,Currency (Price / share),Total,Currency (Total)
                  Market buy,2024-06-03 10:00:00,US88160R1014,TSLA,Tesla,2.0000000000,180.0000000000,USD,360.00,USD
                  Stock split close,2025-01-02 10:00:00,US88160R1014,TSLA,Tesla,2.0000000000,600,USD,,
                  Stock split open,2025-01-02 10:00:00,US88160R1014,TSLA,Tesla,6.0000000000,200,USD,,
                  """;

        var parsed = Parser.Parse(csv);

        var split = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.StockSplit, split.Type);
        Assert.Equal("TSLA", split.Symbol);
        Assert.Equal("US88160R1014", split.Isin);
        Assert.Equal(6m, split.Numerator);
        Assert.Equal(2m, split.Denominator);
        Assert.Equal(3m, split.Factor);
        Assert.Equal(new DateTime(2025, 1, 2, 10, 0, 0), split.DateTime);
        Assert.Single(parsed.Trades);
    }

    [Fact]
    public void Parse_RejectsStockSplitWithoutBothHalves()
    {
        var csv = """
                  Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,Currency (Price / share)
                  Market buy,2024-06-03 10:00:00,US0000000001,TEST,Test Corp,1.0000000000,100.0000000000,USD
                  Stock split,2025-01-02 10:00:00,US0000000001,TEST,Test Corp,2.0000000000,0,USD
                  """;

        var ex = Assert.Throws<FormatException>(() => Parser.Parse(csv));

        Assert.Contains("Stock split close", ex.Message);
        Assert.Contains("Stock split open", ex.Message);
    }

    [Fact]
    public void Parse_RejectsAmbiguousChargeAmountsAlongsideSpecificFees()
    {
        var csv = """
                  Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,Currency (Price / share),Total (GBP),Charge amount (GBP),Stamp duty reserve tax (GBP)
                  Market buy,2025-01-02 10:00:00,GB0000000001,TEST,Test Corp,1.0000000000,100.0000000000,GBP,100.60,0.10,0.50
                  """;

        var ex = Assert.Throws<FormatException>(() => Parser.Parse(csv));

        Assert.Contains("Unsupported Trading 212 fee combination", ex.Message);
    }
}
