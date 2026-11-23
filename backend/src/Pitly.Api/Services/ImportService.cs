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
        IReadOnlyList<string>? fileNames = null,
        IReadOnlyDictionary<string, string>? accountLabels = null)
    {
        if (fileStreams.Count == 0)
            throw new FormatException("No files uploaded.");

        var files = new List<StatementMerger.NamedStatement>();
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

            _logger.LogInformation("Parsing statement {Index}/{Count} ({Length} chars)", i + 1, fileStreams.Count, content.Length);
            // A broker export is one statement. A generic CSV file may list several brokers; each is its own account
            // and therefore its own statement, named after the file and the broker so that warnings stay readable.
            var parsedStatements = _parser.ParseAll(content);
            string? label = null;
            accountLabels?.TryGetValue(name, out label);
            foreach (var parsed in parsedStatements)
            {
                var statementName = parsedStatements.Count > 1 ? name + " (" + parsed.Broker + ")" : name;
                files.Add(new StatementMerger.NamedStatement(statementName, parsed, label));
                _logger.LogInformation(
                    "Parsed statement {Index}/{Count} ({Name}): {Trades} trades, {Dividends} dividends, {Taxes} withholding taxes, {Actions} corporate actions",
                    i + 1,
                    fileStreams.Count,
                    statementName,
                    parsed.Trades.Count,
                    parsed.Dividends.Count,
                    parsed.WithholdingTaxes.Count,
                    parsed.CorporateActions?.Count ?? 0);
            }
        }

        var statements = files.Select(f => f.Statement).ToList();
        // Collected from every row of every file, so a year can be offered even if the latest one cannot be calculated.
        var availableYears = statements.SelectMany(GetCoveredYears).Distinct().OrderBy(y => y).ToList();
        int? attemptedYear = taxYear;

        try
        {
            var taxPeriod = DetermineTaxPeriod(statements, availableYears, residencyStartDate, taxYear);
            attemptedYear = taxPeriod.Year;
            var merged = StatementMerger.Merge(files, taxPeriod.Year, importWarnings);

            if (merged.Trades.Count == 0 && merged.Dividends.Count == 0 && merged.WithholdingTaxes.Count == 0 &&
                (merged.CorporateActions?.Count ?? 0) == 0)
            {
                var years = string.Join(", ", statements.Select(s => s.StatementYear).Where(y => y is not null).Distinct().OrderBy(y => y));
                throw new FormatException(
                    $"The uploaded statement{(statements.Count > 1 ? "s" : string.Empty)} ({years}) contain{(statements.Count > 1 ? string.Empty : "s")} only cash movements such as deposits or withdrawals: " +
                    "no trades, dividends or interest, so there is nothing to declare for that period. " +
                    "Upload the year(s) in which you sold shares or received dividends; earlier years can be added alongside for FIFO history.");
            }

            var calculated = await _calculator.CalculateAsync(merged, taxPeriod) with
            {
                PriorYearLosses = priorYearLosses ?? []
            };

            // PIT-38 is the single source of the tax amounts: the summary carries its figures (after the loss
            // deduction and the form's rounding), never a second calculation of its own.
            var pit38 = Pit38Fields.FromSummary(calculated);
            var summary = calculated with
            {
                CapitalGainTaxPln = pit38.PodatekNalezny,
                DividendTaxOwedPln = pit38.Roznica
            };

            _logger.LogInformation(
                "Tax calculation complete for year {Year} from {TaxableFrom}: capital gain {Gain} PLN, tax to pay {Tax} PLN",
                summary.Year,
                summary.TaxableFrom.ToString("yyyy-MM-dd"),
                summary.CapitalGainPln,
                pit38.PodatekDoZaplaty);

            var session = EntityMapper.ToSessionEntity(summary);
            _db.Sessions.Add(session);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Session {SessionId} saved", session.Id);
            return new ImportResult(session.Id, summary, availableYears, files.Select(DescribeFile).ToList());
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new ImportFailedException(ex.Message, availableYears, attemptedYear, ex);
        }
    }

    private static ImportedFile DescribeFile(StatementMerger.NamedStatement file)
    {
        var resolution = StatementMerger.ResolveAccount(file);
        return new ImportedFile(
            file.Name,
            file.Statement.Broker,
            resolution.FromUser ? file.AccountLabel!.Trim() : resolution.Account,
            AccountKnown: resolution.FromStatement,
            AccountConfirmed: resolution.FromStatement || resolution.FromUser);
    }

    private static TaxPeriod DetermineTaxPeriod(
        IReadOnlyList<ParsedStatement> statements,
        IReadOnlyList<int> availableYears,
        DateTime? residencyStartDate,
        int? requestedYear)
    {
        int targetYear;
        if (requestedYear is not null)
        {
            if (!availableYears.Contains(requestedYear.Value))
            {
                throw new FormatException(
                    "Tax year " + requestedYear.Value + " is not covered by the uploaded statements (" + string.Join(", ", availableYears) + "). " +
                    "Upload the statement for that year, or pick one of the listed years.");
            }

            targetYear = requestedYear.Value;
        }
        else
        {
            targetYear = statements.Select(GetStatementYear).DefaultIfEmpty(0).Max();
            if (targetYear == 0)
            {
                throw new FormatException(
                    "The uploaded files contain no trades or dividends. Please check that the CSV export is valid.");
            }
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
