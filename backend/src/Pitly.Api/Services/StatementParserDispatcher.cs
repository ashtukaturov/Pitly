using Pitly.Broker.Generic;
using Pitly.Broker.InteractiveBrokers;
using Pitly.Broker.Trading212;
using Pitly.Core.Models;
using Pitly.Core.Parsing;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Api.Services;

/// <summary>Picks the parser for an uploaded file from its first line.</summary>
public class StatementParserDispatcher : IStatementParser
{
    private readonly InteractiveBrokersStatementParser _ibParser;
    private readonly Trading212StatementParser _t212Parser;
    private readonly GenericCsvStatementParser _genericParser;

    public StatementParserDispatcher(
        InteractiveBrokersStatementParser ibParser,
        Trading212StatementParser t212Parser,
        GenericCsvStatementParser genericParser)
    {
        _ibParser = ibParser;
        _t212Parser = t212Parser;
        _genericParser = genericParser;
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

        if (GenericCsvStatementParser.DetectKind(firstLine) is not null)
            return _genericParser;

        if (firstLine.Contains("Statement,", StringComparison.Ordinal) ||
            firstLine.Contains("Trades,", StringComparison.Ordinal) ||
            firstLine.Contains("Dividends,", StringComparison.Ordinal) ||
            firstLine.Contains("Withholding Tax,", StringComparison.Ordinal))
            return _ibParser;

        throw new FormatException(
            "Unrecognized file format. Please upload an Interactive Brokers or Trading 212 CSV export, " +
            "or a file filled in from the generic trades or income template.");
    }
}
