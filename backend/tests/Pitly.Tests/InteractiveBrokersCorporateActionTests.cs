using Microsoft.Extensions.Logging.Abstractions;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Core.Models;

namespace Pitly.Tests;

public class InteractiveBrokersCorporateActionTests
{
    private static readonly InteractiveBrokersStatementParser Parser =
        new(NullLogger<InteractiveBrokersStatementParser>.Instance);

    private const string TradesHeader =
        "Trades,Header,DataDiscriminator,Asset Category,Currency,Symbol,Date/Time,Quantity,T. Price,C. Price,Proceeds,Comm/Fee,Basis,Realized P/L,MTM P/L,Code";

    private const string ActionsHeader =
        "Corporate Actions,Header,Asset Category,Currency,Report Date,Date/Time,Description,Quantity,Proceeds,Value,Realized P/L,Code";

    [Fact]
    public void Parse_SplitWithNewIsinKeepsSourceAndTargetIdentifiers()
    {
        var csv = $"""
                   {TradesHeader}
                   Trades,Data,Order,Stocks,USD,BOIL,"2024-02-07, 15:00:00",20,10,10,-200,-1,201,0,0,O
                   {ActionsHeader}
                   Corporate Actions,Data,Stocks,USD,2024-11-07,"2024-11-06, 20:25:00","BOIL(US74347Y7489) Split 1 for 5 (BOIL, PROSHARES ULTRA, US74347Y7489)",4,0,0,0,
                   Corporate Actions,Data,Stocks,USD,2024-11-07,"2024-11-06, 20:25:00","BOIL(US74347Y7638) Split 1 for 5 (BOIL.OLD, PROSHARES ULTRA, US74347Y7638)",-20,0,0,0,
                   """;

        var parsed = Parser.Parse(csv);

        var split = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.StockSplit, split.Type);
        Assert.Equal("BOIL", split.Symbol);
        Assert.Equal("US74347Y7489", split.Isin);
        Assert.Equal("BOIL.OLD", split.SourceSymbol);
        Assert.Equal("US74347Y7638", split.SourceIsin);
        Assert.Equal(0.2m, split.Factor);
        Assert.Empty(parsed.Warnings!);
    }

    [Fact]
    public void Parse_SpinOffBecomesZeroCostLotWithWarning()
    {
        var csv = $"""
                   {TradesHeader}
                   Trades,Data,Order,Stocks,USD,GE,"2022-05-02, 10:00:00",30,80,80,-2400,-1,2401,0,0,O
                   {ActionsHeader}
                   Corporate Actions,Data,Stocks,USD,2023-01-04,"2023-01-03, 20:25:00","GE(US3696043013) Spinoff  1 for 3 (GEHC, GE HEALTHCARE TECHNOLOGIES INC, US36266G1076)",10,0,586.5,0,
                   """;

        var parsed = Parser.Parse(csv);

        var spinOff = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.SpinOff, spinOff.Type);
        Assert.Equal("GEHC", spinOff.Symbol);
        Assert.Equal("US36266G1076", spinOff.Isin);
        Assert.Equal("GE", spinOff.SourceSymbol);
        Assert.Equal("US3696043013", spinOff.SourceIsin);
        Assert.Equal(10m, spinOff.Quantity);
        Assert.Equal(new DateTime(2023, 1, 3, 20, 25, 0), spinOff.DateTime);
        Assert.Contains(parsed.Warnings!, w => w.Contains("Spin-off") && w.Contains("zero acquisition cost"));
    }

    [Fact]
    public void Parse_CashAcquisitionBecomesCashMerger()
    {
        var csv = $"""
                   {TradesHeader}
                   Trades,Data,Order,Stocks,USD,ATVI,"2023-01-10, 10:00:00",15,75,75,-1125,-1,1126,0,0,O
                   {ActionsHeader}
                   Corporate Actions,Data,Stocks,USD,2023-10-16,"2023-10-13, 20:25:00","ATVI(US00507V1098) Merged(Acquisition) FOR USD 95.00 PER SHARE (ATVI, ACTIVISION BLIZZARD INC, US00507V1098)",-15,1425,0,300,
                   """;

        var parsed = Parser.Parse(csv);

        var merger = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.CashMerger, merger.Type);
        Assert.Equal("ATVI", merger.Symbol);
        Assert.Equal("US00507V1098", merger.Isin);
        Assert.Equal(15m, merger.Quantity);
        Assert.Equal(1425m, merger.CashProceeds);
        Assert.Equal("USD", merger.Currency);
        Assert.Contains(parsed.Warnings!, w => w.Contains("Cash merger") && w.Contains("Treated as a sale"));
    }

    [Fact]
    public void Parse_StockForStockMergerCarriesLotsToNewIsin()
    {
        var csv = $"""
                   {TradesHeader}
                   Trades,Data,Order,Stocks,USD,SIVB,"2022-03-01, 10:00:00",10,50,50,-500,-1,501,0,0,O
                   {ActionsHeader}
                   Corporate Actions,Data,Stocks,USD,2022-09-02,"2022-09-01, 20:25:00","OLDCO(US1111111111) Merged(Acquisition) WITH NEWCO 1 FOR 2 (OLDCO, OLD COMPANY, US1111111111)",-10,0,0,0,
                   Corporate Actions,Data,Stocks,USD,2022-09-02,"2022-09-01, 20:25:00","OLDCO(US1111111111) Merged(Acquisition) WITH NEWCO 1 FOR 2 (NEWCO, NEW COMPANY, US2222222222)",5,0,0,0,
                   """;

        var parsed = Parser.Parse(csv);

        var merger = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.StockMerger, merger.Type);
        Assert.Equal("NEWCO", merger.Symbol);
        Assert.Equal("US2222222222", merger.Isin);
        Assert.Equal("OLDCO", merger.SourceSymbol);
        Assert.Equal("US1111111111", merger.SourceIsin);
        Assert.Equal(0.5m, merger.Factor);
        Assert.Equal(0m, merger.CashProceeds);
        Assert.Contains(parsed.Warnings!, w => w.Contains("tax-neutral"));
    }

    [Fact]
    public void Parse_IsinChangeMovesLots()
    {
        var csv = $"""
                   {TradesHeader}
                   Trades,Data,Order,Stocks,USD,IPL,"2021-03-01, 10:00:00",100,3,3,-300,-1,301,0,0,O
                   {ActionsHeader}
                   Corporate Actions,Data,Stocks,AUD,2021-06-02,"2021-06-01, 20:25:00","IPL(AU000000IPL1) CUSIP/ISIN CHANGE TO (AU0000390544) (IPL.OLD, INCITEC PIVOT LTD, AU000000IPL1)",-100,0,0,0,
                   Corporate Actions,Data,Stocks,AUD,2021-06-02,"2021-06-01, 20:25:00","IPL(AU000000IPL1) CUSIP/ISIN CHANGE TO (AU0000390544) (IPL, INCITEC PIVOT LTD, AU0000390544)",100,0,0,0,
                   """;

        var parsed = Parser.Parse(csv);

        var change = Assert.Single(parsed.CorporateActions!);
        Assert.Equal(CorporateActionType.IsinChange, change.Type);
        Assert.Equal("IPL", change.Symbol);
        Assert.Equal("AU0000390544", change.Isin);
        Assert.Equal("IPL.OLD", change.SourceSymbol);
        Assert.Equal("AU000000IPL1", change.SourceIsin);
        Assert.Empty(parsed.Warnings!);
    }

    [Fact]
    public void Parse_UnknownCorporateActionIsReportedNotFatal()
    {
        var csv = $"""
                   {TradesHeader}
                   Trades,Data,Order,Stocks,USD,XYZ,"2024-03-01, 10:00:00",10,5,5,-50,-1,51,0,0,O
                   {ActionsHeader}
                   Corporate Actions,Data,Stocks,USD,2024-06-02,"2024-06-01, 20:25:00","XYZ(US3333333333) Rights Issue Subscription (XYZ, XYZ CORP, US3333333333)",2,0,0,0,
                   """;

        var parsed = Parser.Parse(csv);

        Assert.Empty(parsed.CorporateActions!);
        Assert.Single(parsed.Trades);
        Assert.Contains(parsed.Warnings!, w => w.Contains("not recognised") && w.Contains("Rights Issue"));
    }
}
