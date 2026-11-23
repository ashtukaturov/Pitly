using Pitly.Core.Models;

namespace Pitly.Core.Parsing;

public interface IStatementParser
{
    ParsedStatement Parse(string content);

    /// <summary>
    /// Every statement in the file. A broker export holds one account and gives one statement; a generic CSV file
    /// may list several brokers, and each of them is its own account and therefore its own statement.
    /// </summary>
    IReadOnlyList<ParsedStatement> ParseAll(string content) => [Parse(content)];
}
