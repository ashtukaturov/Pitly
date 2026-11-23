using Pitly.Broker.Generic;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Broker.Revolut;
using Pitly.Broker.Trading212;
using Pitly.Core.Models;
using Pitly.Core.Parsing;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Api.Services;

/// <summary>Picks the parser for an uploaded file from its first line.</summary>
public class StatementParserDispatcher : IStatementParser
{
    private readonly InteractiveBrokersStatementParser _ibParser;
    private readonly InteractiveBrokersFlexParser _flexParser;
    private readonly Trading212StatementParser _t212Parser;
    private readonly GenericCsvStatementParser _genericParser;
    private readonly RevolutSavingsStatementParser _revolutParser;

    public StatementParserDispatcher(
        InteractiveBrokersStatementParser ibParser,
        InteractiveBrokersFlexParser flexParser,
        Trading212StatementParser t212Parser,
        GenericCsvStatementParser genericParser,
        RevolutSavingsStatementParser revolutParser)
    {
        _ibParser = ibParser;
        _flexParser = flexParser;
        _t212Parser = t212Parser;
        _genericParser = genericParser;
        _revolutParser = revolutParser;
    }

    public ParsedStatement Parse(string content) => Select(content).Parse(content);

    public IReadOnlyList<ParsedStatement> ParseAll(string content) => Select(content).ParseAll(content);

    private IStatementParser Select(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new FormatException("File is empty.");

        var newlineIndex = content.IndexOfAny(['\n', '\r']);
        var firstLine = newlineIndex > 0
            ? content[..newlineIndex]
            : content;

        var firstField = ParseCsvLine(firstLine)
            .FirstOrDefault();

        if (string.Equals(Clean(firstField), "Action", StringComparison.OrdinalIgnoreCase))
            return _t212Parser;

        if (InteractiveBrokersFlexParser.Detect(firstLine))
            return _flexParser;

        if (GenericCsvStatementParser.DetectKind(firstLine) is not null)
            return _genericParser;

        if (RevolutSavingsStatementParser.Detect(content))
            return _revolutParser;

        if (firstLine.Contains("Statement,", StringComparison.Ordinal) ||
            firstLine.Contains("Trades,", StringComparison.Ordinal) ||
            firstLine.Contains("Dividends,", StringComparison.Ordinal) ||
            firstLine.Contains("Withholding Tax,", StringComparison.Ordinal))
            return _ibParser;

        throw new FormatException(
            "Unrecognized file format. Please upload an Interactive Brokers Activity Statement or Flex Query CSV, a Trading 212 CSV export, " +
            "a Revolut savings statement CSV, or a file filled in from the generic trades or income template.");
    }
}
