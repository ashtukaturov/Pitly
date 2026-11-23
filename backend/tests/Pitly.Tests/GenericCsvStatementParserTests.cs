using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.Generic;
using Pitly.Core.Models;

namespace Pitly.Tests;

/// <summary>
/// The generic trades and income templates. The CSV text is written in each test; the two template files shipped
/// with the frontend have the same rows as the first two tests.
/// </summary>
public class GenericCsvStatementParserTests
{
    private static readonly GenericCsvStatementParser Parser = new(NullLogger<GenericCsvStatementParser>.Instance);

    private const string TradesHeader =
        "broker,tx_id,direction,symbol,isin,country,currency,price,quantity,amount,commission,operation_datetime,settlement_date";

    private const string IncomeHeader =
        "broker,tx_id,income_type,symbol,isin,country,currency,gross_amount,wht_amount,operation_datetime,settlement_date";

    [Fact]
    public void Parse_ReadsTheTradesTemplate()
    {
        var csv = TradesHeader + "\n" +
                  "DEGIRO,6B439AA86919,BUY,AAPL,US0378331005,US,USD,150.00,10,1500.00,1.50,2024-03-15 10:30:00,2024-03-17\n" +
                  "DEGIRO,A178737D90C2,SELL,AAPL,US0378331005,US,USD,175.00,10,1750.00,1.75,2024-06-20 14:15:00,2024-06-22\n";

        var parsed = Parser.Parse(csv);

        Assert.Equal("DEGIRO", parsed.Broker);
        Assert.Equal("DEGIRO", parsed.AccountId);   // the broker column is the account
        Assert.Equal(2024, parsed.StatementYear);
        Assert.Empty(parsed.Dividends);
        Assert.Empty(parsed.WithholdingTaxes);
        Assert.Empty(parsed.Warnings!);

        Assert.Equal(2, parsed.Trades.Count);
        var buy = parsed.Trades[0];
        Assert.Equal(TradeType.Buy, buy.Type);
        Assert.Equal("AAPL", buy.Symbol);
        Assert.Equal("US0378331005", buy.Isin);
        Assert.Equal("US", buy.CountryCode);
        Assert.Equal("USD", buy.Currency);
        Assert.Equal(new DateTime(2024, 3, 15, 10, 30, 0), buy.DateTime);
        Assert.Equal(10m, buy.Quantity);
        Assert.Equal(150m, buy.Price);
        Assert.Equal(1500m, buy.Proceeds);
        Assert.Equal(1.5m, buy.Commission);
        Assert.Equal("USD", buy.CommissionCurrency);
        Assert.Equal("6B439AA86919", buy.TransactionId);

        var sell = parsed.Trades[1];
        Assert.Equal(TradeType.Sell, sell.Type);
        Assert.Equal(1750m, sell.Proceeds);
        Assert.Equal(1.75m, sell.Commission);
        Assert.Equal("A178737D90C2", sell.TransactionId);
    }

    [Fact]
    public void ParseAll_ReadsTheIncomeTemplateAsOneStatementPerBroker()
    {
        var csv = IncomeHeader + "\n" +
                  "DEGIRO,6FE97C3181FB,DIVIDEND,AAPL,US0378331005,US,USD,25.00,3.75,2024-05-10 00:00:00,2024-05-10\n" +
                  "SWISSQUOTE,90F820E9CDFD,INTEREST,SAVINGS-EUR,,CH,EUR,120.00,22.80,2024-12-31 00:00:00,2024-12-31\n";

        var statements = Parser.ParseAll(csv);

        Assert.Equal(2, statements.Count);

        var degiro = statements[0];
        Assert.Equal("DEGIRO", degiro.Broker);
        Assert.Equal("DEGIRO", degiro.AccountId);
        var dividend = Assert.Single(degiro.Dividends);
        Assert.Equal(IncomeKind.Dividend, dividend.Kind);
        Assert.Equal("AAPL", dividend.Symbol);
        Assert.Equal("US0378331005", dividend.Isin);
        Assert.Equal("US", dividend.CountryCode);
        Assert.Equal(new DateTime(2024, 5, 10), dividend.Date);
        Assert.Equal(25m, dividend.Amount);
        // The tax withheld stays in the row of its payment instead of becoming a separate row to re-match.
        Assert.Equal(3.75m, dividend.WithheldAmount);
        Assert.Equal("USD", dividend.WithheldCurrency);
        Assert.Equal("6FE97C3181FB", dividend.TransactionId);
        Assert.Empty(degiro.WithholdingTaxes);

        var swissquote = statements[1];
        Assert.Equal("SWISSQUOTE", swissquote.AccountId);
        var interest = Assert.Single(swissquote.Dividends);
        Assert.Equal(IncomeKind.Interest, interest.Kind);
        Assert.Equal("SAVINGS-EUR", interest.Symbol);
        Assert.Null(interest.Isin);
        Assert.Equal("CH", interest.CountryCode); // PIT/ZG can attribute it although there is no ISIN
        Assert.Equal("EUR", interest.Currency);
        Assert.Equal(120m, interest.Amount);
        Assert.Equal(22.80m, interest.WithheldAmount);

        // Two brokers are two accounts: the file is not one statement.
        var ex = Assert.Throws<FormatException>(() => Parser.Parse(csv));
        Assert.Contains("2 brokers", ex.Message);
        Assert.Contains("DEGIRO, SWISSQUOTE", ex.Message);
    }

    [Theory]
    [InlineData("broker,tx_id,direction,symbol", GenericCsvKind.Trades)]
    [InlineData("﻿Broker,Tx_Id,Income_Type,Symbol\r", GenericCsvKind.Income)]
    [InlineData("\"broker\",\"income type\"", GenericCsvKind.Income)]
    public void DetectKind_RecognisesTheTemplatesFromTheHeader(string header, GenericCsvKind expected)
    {
        Assert.Equal(expected, GenericCsvStatementParser.DetectKind(header));
    }

    [Theory]
    [InlineData("Action,Time,ISIN,Ticker")]
    [InlineData("Statement,Header,Field Name,Field Value")]
    [InlineData("broker,symbol,currency")]
    [InlineData("direction,symbol,quantity")]
    public void DetectKind_IgnoresOtherFormats(string header)
    {
        Assert.Null(GenericCsvStatementParser.DetectKind(header));
    }

    [Fact]
    public void Parse_FillsTheAmountFromPriceAndQuantityWhenItIsEmptyAndTreatsMissingOptionalColumnsAsEmpty()
    {
        var csv = "broker,direction,symbol,currency,price,quantity,operation_datetime\n" +
                  "XTB,buy,MSFT,USD,400.5,2,2024-02-01\n";

        var trade = Assert.Single(Parser.Parse(csv).Trades);

        Assert.Equal(801m, trade.Proceeds);
        Assert.Equal(0m, trade.Commission);
        Assert.Null(trade.Isin);
        Assert.Null(trade.CountryCode);
        Assert.Null(trade.TransactionId);
        Assert.Equal(new DateTime(2024, 2, 1), trade.DateTime);
    }

    [Fact]
    public void Parse_UsesTheAmountAndWarnsWhenItDisagreesWithPriceTimesQuantity()
    {
        // 10 shares at 150.00 are 1500.00; 1480.00 is more than a cent per share away, so the row is reported.
        var csv = TradesHeader + "\n" +
                  "DEGIRO,1,BUY,AAPL,US0378331005,US,USD,150.00,10,1480.00,0,2024-03-15 10:30:00,\n" +
                  // A rounded price: 3 x 33.33 = 99.99 against 100.00 is within tolerance and not reported.
                  "DEGIRO,2,BUY,AAPL,US0378331005,US,USD,33.33,3,100.00,0,2024-03-16 10:30:00,\n";

        var parsed = Parser.Parse(csv);

        Assert.Equal(1480m, parsed.Trades[0].Proceeds);
        Assert.Equal(100m, parsed.Trades[1].Proceeds);
        var warning = Assert.Single(parsed.Warnings!);
        Assert.Contains("BUY AAPL on 2024-03-15 (line 2)", warning);
        Assert.Contains("amount 1480 USD", warning);
        Assert.Contains("1500 USD", warning);
    }

    [Fact]
    public void Parse_RejectsDecimalCommasThousandsSeparatorsAndSemicolons()
    {
        var comma = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,US,USD,\"150,00\",10,,0,2024-03-15,\n"));
        Assert.Contains("'price' value '150,00' is not a number", comma.Message);

        var thousands = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,US,USD,150.00,10,\"1,500.00\",0,2024-03-15,\n"));
        Assert.Contains("'amount' value '1,500.00' is not a number", thousands.Message);

        var semicolons = Assert.Throws<FormatException>(() => Parser.Parse(
            "broker;tx_id;direction;symbol;currency;price;quantity;operation_datetime\nDEGIRO;1;BUY;AAPL;USD;150,00;10;2024-03-15\n"));
        Assert.Contains("comma as the column separator", semicolons.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownDirectionsAndIncomeTypes()
    {
        var direction = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,SHORT,AAPL,US0378331005,US,USD,150.00,10,1500.00,0,2024-03-15,\n"));
        Assert.Contains("direction 'SHORT' is not supported", direction.Message);
        Assert.Contains("Line 2", direction.Message);

        var incomeType = Assert.Throws<FormatException>(() => Parser.Parse(IncomeHeader + "\n" +
            "DEGIRO,1,RETURN_OF_CAPITAL,AAPL,US0378331005,US,USD,25.00,0,2024-05-10,\n"));
        Assert.Contains("income_type 'RETURN_OF_CAPITAL' is not supported", incomeType.Message);
    }

    [Fact]
    public void Parse_AcceptsIncomeTypesInAnyCaseAndSpelling()
    {
        var csv = IncomeHeader + "\n" +
                  "IBKR,1,dividend,AAPL,US0378331005,US,USD,25.00,3.75,2024-05-10,\n" +
                  "IBKR,2,Payment in lieu,AAPL,US0378331005,US,USD,10.00,,2024-05-11,\n" +
                  "IBKR,3,INTEREST,,,,USD,1.00,,2024-05-12,\n";

        var parsed = Parser.Parse(csv);

        Assert.Equal([IncomeKind.Dividend, IncomeKind.PaymentInLieu, IncomeKind.Interest], parsed.Dividends.Select(d => d.Kind));
        Assert.Null(parsed.Dividends[1].WithheldAmount);
        Assert.Equal("USD interest", parsed.Dividends[2].Symbol); // interest needs no symbol
    }

    [Fact]
    public void Parse_RejectsAmountsThatCannotBeRight()
    {
        var withheldAboveGross = Assert.Throws<FormatException>(() => Parser.Parse(IncomeHeader + "\n" +
            "DEGIRO,1,DIVIDEND,AAPL,US0378331005,US,USD,25.00,30.00,2024-05-10,\n"));
        Assert.Contains("wht_amount 30 exceeds gross_amount 25", withheldAboveGross.Message);

        var negativeGross = Assert.Throws<FormatException>(() => Parser.Parse(IncomeHeader + "\n" +
            "DEGIRO,1,DIVIDEND,AAPL,US0378331005,US,USD,-25.00,0,2024-05-10,\n"));
        Assert.Contains("gross_amount must be positive", negativeGross.Message);

        var negativeQuantity = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,SELL,AAPL,US0378331005,US,USD,150.00,-10,1500.00,0,2024-03-15,\n"));
        Assert.Contains("quantity must be positive", negativeQuantity.Message);

        var negativeCommission = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,US,USD,150.00,10,1500.00,-1.50,2024-03-15,\n"));
        Assert.Contains("commission cannot be negative", negativeCommission.Message);

        var noSymbol = Assert.Throws<FormatException>(() => Parser.Parse(IncomeHeader + "\n" +
            "DEGIRO,1,DIVIDEND,,US0378331005,US,USD,25.00,0,2024-05-10,\n"));
        Assert.Contains("needs a symbol", noSymbol.Message);
    }

    [Fact]
    public void Parse_RejectsMalformedDatesCurrenciesCountriesAndIsins()
    {
        var date = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,US,USD,150.00,10,,0,15.03.2024,\n"));
        Assert.Contains("'operation_datetime' value '15.03.2024' is not a date", date.Message);

        var settlement = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,US,USD,150.00,10,,0,2024-03-15,next week\n"));
        Assert.Contains("'settlement_date' value 'next week' is not a date", settlement.Message);

        var currency = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,US,$,150.00,10,,0,2024-03-15,\n"));
        Assert.Contains("currency '$' is not a three-letter ISO code", currency.Message);

        var country = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US0378331005,USA,USD,150.00,10,,0,2024-03-15,\n"));
        Assert.Contains("country 'USA' is not a two-letter ISO code", country.Message);

        var isin = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            "DEGIRO,1,BUY,AAPL,US037833100,US,USD,150.00,10,,0,2024-03-15,\n"));
        Assert.Contains("isin 'US037833100' is not a 12-character ISIN", isin.Message);
    }

    [Fact]
    public void Parse_WarnsWhenTheCountryDisagreesWithTheIsin()
    {
        // A depositary receipt: US ISIN, dividend taxed in Japan.
        var csv = IncomeHeader + "\n" +
                  "DEGIRO,1,DIVIDEND,TM,US8923313071,JP,USD,25.00,3.75,2024-05-10,\n";

        var parsed = Parser.Parse(csv);

        Assert.Equal("JP", Assert.Single(parsed.Dividends).CountryCode);
        var warning = Assert.Single(parsed.Warnings!);
        Assert.Contains("TM (line 2) has country JP but ISIN US8923313071", warning);
        Assert.Contains("PIT/ZG uses JP", warning);
    }

    [Fact]
    public void Parse_WarnsAboutARepeatedTransactionIdButKeepsBothRows()
    {
        var csv = TradesHeader + "\n" +
                  "DEGIRO,SAME,BUY,AAPL,US0378331005,US,USD,150.00,10,1500.00,0,2024-03-15 10:30:00,\n" +
                  "DEGIRO,SAME,BUY,AAPL,US0378331005,US,USD,150.00,10,1500.00,0,2024-03-15 10:30:00,\n";

        var parsed = Parser.Parse(csv);

        Assert.Equal(2, parsed.Trades.Count);
        Assert.Contains("tx_id 'SAME' appears 2 times", Assert.Single(parsed.Warnings!));
    }

    [Fact]
    public void Parse_ConvertsPenceSterlingToPounds()
    {
        var csv = TradesHeader + "\n" +
                  "T212,1,BUY,VUSA,IE00B3XXRP09,IE,GBX,8000,2,16000,50,2024-03-15,\n";

        var trade = Assert.Single(Parser.Parse(csv).Trades);

        Assert.Equal("GBP", trade.Currency);
        Assert.Equal(80m, trade.Price);
        Assert.Equal(160m, trade.Proceeds);
        Assert.Equal(0.5m, trade.Commission);
        Assert.Equal("GBP", trade.CommissionCurrency);
    }

    [Fact]
    public void Parse_RejectsFilesWithoutTheRequiredColumnsOrRows()
    {
        var missing = Assert.Throws<FormatException>(() => Parser.Parse("broker,direction,symbol,operation_datetime\nDEGIRO,BUY,AAPL,2024-03-15\n"));
        Assert.Contains("missing the columns 'currency', 'quantity', 'price'", missing.Message);

        var other = Assert.Throws<FormatException>(() => Parser.Parse("Action,Time,ISIN\nMarket buy,2024-03-15,US0378331005\n"));
        Assert.Contains("does not follow the generic trades or income template", other.Message);

        var headerOnly = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n\n"));
        Assert.Contains("no data rows", headerOnly.Message);

        var emptyBroker = Assert.Throws<FormatException>(() => Parser.Parse(TradesHeader + "\n" +
            ",1,BUY,AAPL,US0378331005,US,USD,150.00,10,1500.00,0,2024-03-15,\n"));
        Assert.Contains("Line 2: 'broker' is empty", emptyBroker.Message);

        Assert.Throws<FormatException>(() => Parser.Parse("   "));
    }
}
