using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pitly.Api.Data;
using Pitly.Api.Mapping;
using Pitly.Core.Models;
using Pitly.Core.Parsing;
using Pitly.Core.Tax;

namespace Pitly.Api.Services;

public class ImportService : IImportService
{
    private readonly IStatementParser _parser;
    private readonly ITaxCalculator _calculator;
    private readonly AppDbContext _db;
    private readonly ILogger<ImportService> _logger;

    public ImportService(IStatementParser parser, ITaxCalculator calculator, AppDbContext db, ILogger<ImportService> logger)
    {
        _parser = parser;
        _calculator = calculator;
        _db = db;
        _logger = logger;
    }

    public async Task<ImportResult> ImportStatementsAsync(
        IReadOnlyList<Stream> fileStreams,
        DateTime? residencyStartDate = null,
        IReadOnlyList<PriorYearLoss>? priorYearLosses = null,
        int? taxYear = null,
        IReadOnlyList<string>? fileNames = null)
    {
        if (fileStreams.Count == 0)
            throw new FormatException("No files uploaded.");

        var statements = new List<ParsedStatement>();
        var names = new List<string>();
        var importWarnings = new List<string>();
        var seenContent = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < fileStreams.Count; i++)
        {
            using var reader = new StreamReader(fileStreams[i], leaveOpen: true);
            var content = await reader.ReadToEndAsync();
            var name = fileNames is not null && i < fileNames.Count && !string.IsNullOrWhiteSpace(fileNames[i]) ? fileNames[i] : "file " + (i + 1);

            // The same file selected twice (or re-uploaded after an error) is skipped before parsing.
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            if (seenContent.TryGetValue(hash, out var firstName))
            {
                importWarnings.Add("'" + name + "' is identical to '" + firstName + "' and was skipped.");
                continue;
            }
            seenContent[hash] = name;
            names.Add(name);

            _logger.LogInformation("Parsing statement {Index}/{Count} ({Length} chars)", i + 1, fileStreams.Count, content.Length);
            var parsed = _parser.Parse(content);
            statements.Add(parsed);
            _logger.LogInformation(
                "Parsed statement {Index}/{Count}: {Trades} trades, {Dividends} dividends, {Taxes} withholding taxes, {Actions} corporate actions",
                i + 1,
                fileStreams.Count,
                parsed.Trades.Count,
                parsed.Dividends.Count,
                parsed.WithholdingTaxes.Count,
                parsed.CorporateActions?.Count ?? 0);
        }

        var availableYears = statements.SelectMany(GetCoveredYears).Distinct().OrderBy(y => y).ToList();
        var taxPeriod = DetermineTaxPeriod(statements, residencyStartDate, taxYear);
        var merged = StatementMerger.Merge(
            statements.Zip(names, (statement, name) => new StatementMerger.NamedStatement(name, statement)).ToList(),
            taxPeriod.Year,
            importWarnings);

        if (merged.Trades.Count == 0 && merged.Dividends.Count == 0 && merged.WithholdingTaxes.Count == 0 &&
            (merged.CorporateActions?.Count ?? 0) == 0)
        {
            var years = string.Join(", ", statements.Select(s => s.StatementYear).Where(y => y is not null).Distinct().OrderBy(y => y));
            throw new FormatException(
                $"The uploaded statement{(statements.Count > 1 ? "s" : string.Empty)} ({years}) contain{(statements.Count > 1 ? string.Empty : "s")} only cash movements such as deposits or withdrawals: " +
                "no trades, dividends or interest, so there is nothing to declare for that period. " +
                "Upload the year(s) in which you sold shares or received dividends; earlier years can be added alongside for FIFO history.");
        }

        var summary = await _calculator.CalculateAsync(merged, taxPeriod) with
        {
            PriorYearLosses = priorYearLosses ?? []
        };
        _logger.LogInformation(
            "Tax calculation complete for year {Year} from {TaxableFrom}: capital gain {Gain} PLN, dividend tax owed {DivTax} PLN",
            summary.Year,
            summary.TaxableFrom.ToString("yyyy-MM-dd"),
            summary.CapitalGainPln,
            summary.DividendTaxOwedPln);

        var session = EntityMapper.ToSessionEntity(summary);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Session {SessionId} saved", session.Id);
        return new ImportResult(session.Id, summary, availableYears);
    }

    private static int DetermineTargetYear(IReadOnlyList<ParsedStatement> statements)
    {
        var years = statements
            .Select(GetStatementYear)
            .ToList();

        if (years.Count == 0)
        {
            throw new FormatException(
                "The uploaded files contain no trades or dividends. Please check that the CSV export is valid.");
        }

        return years.Max();
    }

    private static TaxPeriod DetermineTaxPeriod(
        IReadOnlyList<ParsedStatement> statements,
        DateTime? residencyStartDate,
        int? requestedYear)
    {
        var targetYear = DetermineTargetYear(statements);
        if (requestedYear is not null)
        {
            var years = statements.SelectMany(GetCoveredYears).Distinct().OrderBy(y => y).ToList();
            if (!years.Contains(requestedYear.Value))
            {
                throw new FormatException(
                    "Tax year " + requestedYear.Value + " is not covered by the uploaded statements (" + string.Join(", ", years) + "). " +
                    "Upload the statement for that year, or pick one of the listed years.");
            }

            targetYear = requestedYear.Value;
        }

        if (residencyStartDate is null)
            return TaxPeriod.FullYear(targetYear);

        var taxableFrom = residencyStartDate.Value.Date;

        // Residency that started in an earlier year means the whole target year is taxable in Poland.
        if (taxableFrom.Year < targetYear)
            return TaxPeriod.FullYear(targetYear);

        if (taxableFrom.Year > targetYear)
        {
            throw new FormatException(
                "Polish tax residency starts on " + taxableFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                ", so there is nothing to declare in Poland for " + targetYear + ". Pick " + taxableFrom.Year + " or a later year.");
        }

        return new TaxPeriod(targetYear, taxableFrom, new DateTime(targetYear, 12, 31));
    }

    /// <summary>Every year the statement has data for: a Trading 212 export can span several years.</summary>
    private static IEnumerable<int> GetCoveredYears(ParsedStatement statement)
    {
        var years = statement.Trades.Select(t => t.DateTime.Year)
            .Concat(statement.Dividends.Select(d => d.Date.Year))
            .Concat(statement.WithholdingTaxes.Select(t => t.Date.Year))
            .Concat((statement.CorporateActions ?? []).Select(a => a.DateTime.Year))
            .ToList();
        if (statement.StatementYear is not null)
            years.Add(statement.StatementYear.Value);
        return years.Distinct();
    }

    private static int GetStatementYear(ParsedStatement statement)
    {
        if (statement.StatementYear is not null)
            return statement.StatementYear.Value;

        var years = statement.Trades.Select(t => t.DateTime.Year)
            .Concat(statement.Dividends.Select(d => d.Date.Year))
            .Concat(statement.WithholdingTaxes.Select(t => t.Date.Year))
            .Concat((statement.CorporateActions ?? []).Select(a => a.DateTime.Year))
            .Concat((statement.CarryInPositions ?? []).Select(p => p.Year))
            .ToList();

        if (years.Count == 0)
        {
            throw new FormatException(
                "The uploaded files contain no trades or dividends. Please check that the CSV export is valid.");
        }

        return years.Max();
    }

}
