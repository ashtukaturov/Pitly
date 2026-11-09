using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Core.Models;

namespace Pitly.Tests;

public class InteractiveBrokersStatementParserTests
{
    private static readonly InteractiveBrokersStatementParser Parser =
        new(NullLogger<InteractiveBrokersStatementParser>.Instance);

    [Fact]
    public void Parse_ExtractsStatementYearCarryInSplitAndTradeIsin()
    {
        var csv = """
                  Statement,Data,Period,"January 1, 2025 - December 31, 2025"
                  Mark-to-Market Performance Summary,Header,Asset Category,Symbol,Prior Quantity,Current Quantity,Prior Price,Current Price,Mark-to-Market P/L Position,Mark-to-Market P/L Transaction,Mark-to-Market P/L Commissions,Mark-to-Market P/L Other,Mark-to-Market P/L Total,Code
                  Mark-to-Market Performance Summary,Data,Stocks,IBKR,0.8847,43.9033,176.6700,64.3100,-120.2959,-13.720535,-2.00516057,3.65,-132.37159557,
                  Trades,Header,DataDiscriminator,Asset Category,Currency,Symbol,Date/Time,Quantity,T. Price,C. Price,Proceeds,Comm/Fee,Basis,Realized P/L,MTM P/L,Code
                  Trades,Data,Order,Stocks,USD,IBKR,"2025-09-19, 09:30:01",-3.1,65.317741935,65.03,202.485,-1.0005828,-61.624803,139.859614,0.892,C;FPA;P
                  Corporate Actions,Header,Asset Category,Currency,Report Date,Date/Time,Description,Quantity,Proceeds,Value,Realized P/L,Code
                  Corporate Actions,Data,Stocks,USD,2025-06-18,"2025-06-17, 20:25:00","IBKR(US45841N1072) Split 4 for 1 (IBKR, INTERACTIVE BROKERS GRO-CL A, US45841N1072)",2.6607,0,0,0,
                  Financial Instrument Information,Header,Asset Category,Symbol,Description,Conid,Security ID,Underlying,Listing Exch,Multiplier,Type,Code
                  Financial Instrument Information,Data,Stocks,IBKR,INTERACTIVE BROKERS GRO-CL A,43645865,US45841N1072,IBKR,NASDAQ,1,COMMON,
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Equal(2025, parsed.StatementYear);

        var trade = Assert.Single(parsed.Trades);
        Assert.Equal(TradeType.Sell, trade.Type);
        Assert.Equal("US45841N1072", trade.Isin);

        var carryIn = Assert.Single(parsed.CarryInPositions!);
        Assert.Equal("IBKR", carryIn.Symbol);
        Assert.Equal(0.8847m, carryIn.Quantity);
        Assert.Equal(2025, carryIn.Year);
        Assert.Equal("US45841N1072", carryIn.Isin);

        var split = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.StockSplit, split.Type);
        Assert.Equal("IBKR", split.Symbol);
        Assert.Equal(4m, split.Numerator);
        Assert.Equal(1m, split.Denominator);
        Assert.Equal(4m, split.Factor);
        Assert.Equal("US45841N1072", split.Isin);
    }

    [Fact]
    public void Parse_ParsesInterestAndPaymentInLieuAsSeparateIncomeKinds()
    {
        var csv = """
                  Statement,Data,Period,"January 1, 2025 - December 31, 2025"
                  Dividends,Header,Currency,Date,Description,Amount
                  Dividends,Data,USD,2025-06-14,AAPL(US0378331005) Cash Dividend USD 0.25 per Share (Ordinary Dividend),2.50
                  Dividends,Data,Total,,,2.50
                  Payment In Lieu Of Dividends,Header,Currency,Date,Description,Amount
                  Payment In Lieu Of Dividends,Data,USD,2025-06-14,AAPL(US0378331005) Payment in Lieu of Dividend (Ordinary Dividend),1.00
                  Payment In Lieu Of Dividends,Data,Total,,,1.00
                  Withholding Tax,Header,Currency,Date,Description,Amount,Code
                  Withholding Tax,Data,USD,2025-06-14,AAPL(US0378331005) Cash Dividend USD 0.25 per Share - US Tax,-0.38,
                  Withholding Tax,Data,USD,2025-06-14,AAPL(US0378331005) Payment in Lieu of Dividend - US Tax,-0.30,
                  Withholding Tax,Data,Total,,,-0.68,
                  Interest,Header,Currency,Date,Description,Amount
                  Interest,Data,USD,2025-03-05,USD Credit Interest for Feb-2025,12.34
                  Interest,Data,USD,2025-04-03,USD Debit Interest for Mar-2025,-3.00
                  Interest,Data,Total,,,9.34
                  Interest,Data,Total in USD,,,9.34
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Equal(3, parsed.Dividends.Count);

        var dividend = Assert.Single(parsed.Dividends, d => d.Kind == IncomeKind.Dividend);
        Assert.Equal("AAPL", dividend.Symbol);
        Assert.Equal(2.50m, dividend.Amount);

        var paymentInLieu = Assert.Single(parsed.Dividends, d => d.Kind == IncomeKind.PaymentInLieu);
        Assert.Equal("AAPL", paymentInLieu.Symbol);
        Assert.Equal("US0378331005", paymentInLieu.Isin);
        Assert.Equal(1.00m, paymentInLieu.Amount);

        // Only credit interest is income; debit (margin) interest is ignored.
        var interest = Assert.Single(parsed.Dividends, d => d.Kind == IncomeKind.Interest);
        Assert.Equal("USD", interest.Currency);
        Assert.Equal(new DateTime(2025, 3, 5), interest.Date);
        Assert.Equal(12.34m, interest.Amount);
        Assert.Null(interest.Isin);

        Assert.Equal(2, parsed.WithholdingTaxes.Count);
        Assert.Equal(0.38m, Assert.Single(parsed.WithholdingTaxes, t => t.Kind == IncomeKind.Dividend).Amount);
        Assert.Equal(0.30m, Assert.Single(parsed.WithholdingTaxes, t => t.Kind == IncomeKind.PaymentInLieu).Amount);
    }

    [Fact]
    public void Parse_RegistersIsinUnderEveryTickerAlias()
    {
        var csv = """
                  Statement,Data,Period,"January 1, 2025 - December 31, 2025"
                  Trades,Header,DataDiscriminator,Asset Category,Currency,Symbol,Date/Time,Quantity,T. Price,C. Price,Proceeds,Comm/Fee,Basis,Realized P/L,MTM P/L,Code
                  Trades,Data,Order,Stocks,USD,frc,"2025-03-13, 12:00:00",4,16,16,-64,-1,65,0,0,O
                  Trades,Data,Order,Stocks,USD,FRCB,"2025-11-24, 09:00:00",-4,0.001,0.001,0.004,-0.01,-65,-65.006,0,C
                  Financial Instrument Information,Header,Asset Category,Symbol,Description,Conid,Security ID,Listing Exch,Multiplier,Type,Code
                  Financial Instrument Information,Data,Stocks,"FRC, FRCB",FIRST REPUBLIC BANK,81731135,US33616C1009,PINK,1,COMMON,
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Equal(2, parsed.Trades.Count);
        Assert.All(parsed.Trades, t => Assert.Equal("US33616C1009", t.Isin));
    }

    [Fact]
    public void Parse_StockSplitDebitRowIsSkipped()
    {
        var csv = """
                  Trades,Header,DataDiscriminator,Asset Category,Currency,Symbol,Date/Time,Quantity,T. Price,C. Price,Proceeds,Comm/Fee,Basis,Realized P/L,MTM P/L,Code
                  Trades,Data,Order,Stocks,USD,BOIL,"2024-02-07, 15:00:00",20,10,10,-200,-1,201,0,0,O
                  Corporate Actions,Header,Asset Category,Currency,Report Date,Date/Time,Description,Quantity,Proceeds,Value,Realized P/L,Code
                  Corporate Actions,Data,Stocks,USD,2024-11-07,"2024-11-06, 20:25:00","BOIL(US74347Y7489) Split 1 for 5 (BOIL, PROSHARES ULTRA, US74347Y7489)",4,0,0,0,
                  Corporate Actions,Data,Stocks,USD,2024-11-07,"2024-11-06, 20:25:00","BOIL(US74347Y7638) Split 1 for 5 (BOIL.OLD, PROSHARES ULTRA, US74347Y7638)",-20,0,0,0,
                  """;

        var parsed = Parser.Parse(csv);

        var split = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.StockSplit, split.Type);
        Assert.Equal("BOIL", split.Symbol);
        Assert.Equal(1m, split.Numerator);
        Assert.Equal(5m, split.Denominator);
        Assert.Equal("US74347Y7489", split.Isin);
        Assert.Equal("US74347Y7638", split.SourceIsin);
    }

    [Fact]
    public void Parse_ReversedDividendIsNetted()
    {
        var csv = """
                  Dividends,Header,Currency,Date/Time,Description,Amount
                  Dividends,Data,USD,2024-07-02,"STM(IE00BK5BCQ80) Cash Dividend USD 0.09 per Share (Ordinary Dividend)",1.8
                  Dividends,Data,USD,2024-07-03,"STM(IE00BK5BCQ80) Cash Dividend USD 0.09 per Share (Ordinary Dividend)",1.8
                  Dividends,Data,USD,2024-07-03,"STM(IE00BK5BCQ80) Cash Dividend USD 0.09 per Share - Reversal (Ordinary Dividend)",-1.8
                  """;

        var parsed = Parser.Parse(csv);

        var dividend = Assert.Single(parsed.Dividends);
        Assert.Equal("STM", dividend.Symbol);
        Assert.Equal(new DateTime(2024, 7, 2), dividend.Date);
        Assert.Equal(1.8m, dividend.Amount);
    }

    [Fact]
    public void Parse_ReversedWithholdingTaxIsNetted()
    {
        var csv = """
                  Dividends,Header,Currency,Date/Time,Description,Amount
                  Dividends,Data,USD,2024-07-02,"STM(IE00BK5BCQ80) Cash Dividend USD 0.09 per Share (Ordinary Dividend)",1.8
                  Withholding Tax,Header,Currency,Date/Time,Description,Amount
                  Withholding Tax,Data,USD,2024-07-02,"STM(IE00BK5BCQ80) Cash Dividend USD 0.09 per Share - US Tax",-0.27
                  Withholding Tax,Data,USD,2024-07-02,"STM(IE00BK5BCQ80) Cash Dividend USD 0.09 per Share - US Tax - Reversal",0.27
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Single(parsed.Dividends);
        Assert.Empty(parsed.WithholdingTaxes);
    }

    [Fact]
    public void Parse_GrantActivityParsedAsBuyTrades()
    {
        var csv = """
                  Statement,Data,Period,"January 1, 2022 - December 31, 2022"
                  Grant Activity,Header,Symbol,Report Date,Description,Award Date,Vesting Date,Quantity,Price,Value
                  Grant Activity,Data,IBKR,2022-11-03,Stock Award Grant for Cash Deposit,2022-11-03,2023-11-03,0.7367,80.23,59.11
                  Grant Activity,Data,IBKR,2022-11-04,Stock Award Grant for Cash Deposit,2022-11-04,2023-11-03,0.31,80.67,25.01
                  Grant Activity,Data,IBKR,2022-12-15,Stock Award Grant for Cash Deposit,2022-12-15,2023-12-15,0.2105,71.27,15
                  Grant Activity,Data,Total,,,,,1.2572,,99.12
                  Financial Instrument Information,Header,Asset Category,Symbol,Description,Conid,Security ID,Underlying,Listing Exch,Multiplier,Type,Code
                  Financial Instrument Information,Data,Stocks,IBKR,INTERACTIVE BROKERS GRO-CL A,43645865,US45841N1072,IBKR,NASDAQ,1,COMMON,
                  Dividends,Header,Currency,Date/Time,Description,Amount
                  Dividends,Data,USD,2022-12-14,"IBKR(US45841N1072) Payment in Lieu of Dividend (Ordinary Dividend)",0.1
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Equal(3, parsed.Trades.Count);

        var first = parsed.Trades[0];
        Assert.Equal("IBKR", first.Symbol);
        Assert.Equal(TradeType.Buy, first.Type);
        Assert.Equal(new DateTime(2022, 11, 3), first.DateTime);
        Assert.Equal(0.7367m, first.Quantity);
        Assert.Equal(80.23m, first.Price);
        Assert.Equal(0m, first.Commission);
        Assert.Equal("USD", first.Currency);
        Assert.Equal("US45841N1072", first.Isin);

        var second = parsed.Trades[1];
        Assert.Equal(new DateTime(2022, 11, 4), second.DateTime);
        Assert.Equal(0.31m, second.Quantity);
        Assert.Equal(80.67m, second.Price);

        var third = parsed.Trades[2];
        Assert.Equal(new DateTime(2022, 12, 15), third.DateTime);
        Assert.Equal(0.2105m, third.Quantity);
        Assert.Equal(71.27m, third.Price);
    }

    [Fact]
    public void Parse_CashOnlyStatementIsAcceptedAsEmptyWithWarning()
    {
        var csv = """
                  Statement,Header,Field Name,Field Value
                  Statement,Data,Title,Activity Statement
                  Statement,Data,Period,"January 1, 2020 - December 31, 2020"
                  Account Information,Header,Field Name,Field Value
                  Account Information,Data,Account Type,Individual
                  Deposits & Withdrawals,Header,Currency,Settle Date,Description,Amount
                  Deposits & Withdrawals,Data,USD,2020-12-07,Electronic Fund Transfer,30000
                  Deposits & Withdrawals,Data,Total,,,30000
                  """;

        var parsed = Parser.Parse(csv);

        Assert.Empty(parsed.Trades);
        Assert.Empty(parsed.Dividends);
        Assert.Equal(2020, parsed.StatementYear);
        Assert.Contains(parsed.Warnings!, w => w.Contains("2020") && w.Contains("nothing to declare"));
    }

    [Fact]
    public void Parse_RejectsFilesThatAreNotIbStatements()
    {
        var ex = Assert.Throws<FormatException>(() => Parser.Parse("Foo,Bar\n1,2\n"));

        Assert.Contains("does not appear to be an IB Activity Statement", ex.Message);
    }

    [Fact]
    public void Parse_KeepsSignOfWithholdingCorrectionsDatedInEarlierYear()
    {
        // The next-year statement reverses last year's withholding (+6.69) and re-charges a smaller amount (-0.53).
        var csv = """
                  Statement,Header,Field Name,Field Value
                  Statement,Data,Period,"January 1, 2026 - November 6, 2026"
                  Dividends,Header,Currency,Date,Description,Amount
                  Dividends,Data,USD,2026-03-05,"BND(US9219378356) Cash Dividend USD 0.23 per Share (Unqualified Dividend)",44.00
                  Withholding Tax,Header,Currency,Date,Description,Amount
                  Withholding Tax,Data,USD,2025-02-05,"BND(US9219378356) Cash Dividend USD 0.234799 per Share - US Tax",6.69
                  Withholding Tax,Data,USD,2025-02-05,"BND(US9219378356) Cash Dividend USD 0.234799 per Share - US Tax",-0.53
                  Withholding Tax,Data,USD,2026-03-05,"BND(US9219378356) Cash Dividend USD 0.23 per Share - US Tax",-6.60
                  """;

        var parsed = Parser.Parse(csv);

        var correction = Assert.Single(parsed.WithholdingTaxes, t => t.Date == new DateTime(2025, 2, 5));
        Assert.Equal(-6.16m, correction.Amount);
        var current = Assert.Single(parsed.WithholdingTaxes, t => t.Date == new DateTime(2026, 3, 5));
        Assert.Equal(6.60m, current.Amount);
    }
}
