using Pitly.Core.Models;

namespace Pitly.Api.Services;

public interface IImportService
{
    Task<ImportResult> ImportStatementsAsync(
        IReadOnlyList<Stream> fileStreams,
        DateTime? residencyStartDate = null,
        IReadOnlyList<PriorYearLoss>? priorYearLosses = null,
        int? taxYear = null,
        IReadOnlyList<string>? fileNames = null);
}

/// <param name="AvailableYears">Statement years found in the upload; any of them can be requested as the tax year.</param>
public record ImportResult(Guid SessionId, TaxSummary Summary, IReadOnlyList<int> AvailableYears);
