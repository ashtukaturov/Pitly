using Pitly.Core.Models;

namespace Pitly.Api.Services;

public interface IImportService
{
    /// <param name="accountLabels">
    /// The user's answer for files that carry no account number, keyed by file name: files with the same label are
    /// one account, files with different labels are separate accounts.
    /// </param>
    Task<ImportResult> ImportStatementsAsync(
        IReadOnlyList<Stream> fileStreams,
        DateTime? residencyStartDate = null,
        IReadOnlyList<PriorYearLoss>? priorYearLosses = null,
        int? taxYear = null,
        IReadOnlyList<string>? fileNames = null,
        IReadOnlyDictionary<string, string>? accountLabels = null);
}

/// <param name="AvailableYears">Years found anywhere in the upload; any of them can be requested as the tax year.</param>
/// <param name="Files">The uploaded files with the account each was attributed to.</param>
public record ImportResult(
    Guid SessionId,
    TaxSummary Summary,
    IReadOnlyList<int> AvailableYears,
    IReadOnlyList<ImportedFile> Files);

/// <param name="Account">Account the file was attributed to (statement number, user label or the broker default).</param>
/// <param name="AccountKnown">True when the statement itself names the account.</param>
/// <param name="AccountConfirmed">True when the statement names the account or the user labelled the file.</param>
public record ImportedFile(string Name, string? Broker, string Account, bool AccountKnown, bool AccountConfirmed);

/// <summary>
/// The upload was understood but cannot be calculated for the requested year. Carries the years the upload covers
/// so that the user can pick another one instead of starting over.
/// </summary>
public class ImportFailedException : Exception
{
    public ImportFailedException(string message, IReadOnlyList<int> availableYears, int? taxYear, Exception? inner = null)
        : base(message, inner)
    {
        AvailableYears = availableYears;
        TaxYear = taxYear;
    }

    public IReadOnlyList<int> AvailableYears { get; }
    public int? TaxYear { get; }
}
