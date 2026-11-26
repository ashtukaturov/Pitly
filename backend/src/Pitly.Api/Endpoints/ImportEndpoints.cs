using System.Globalization;
using System.Text.Json;
using Pitly.Api.Mapping;
using Pitly.Api.Services;
using Pitly.Core.Models;
using Pitly.Core.Tax;

namespace Pitly.Api.Endpoints;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this WebApplication app)
    {
        app.MapPost("/api/import", async (HttpRequest request, IImportService importService) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            if (request.ContentLength > UploadLimits.MaxRequestBytes)
                return RequestTooLarge();

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync();
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return RequestTooLarge();
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
            {
                return RequestTooLarge();
            }

            DateTime? residencyStartDate = null;
            var residencyStartDateRaw = form["residencyStartDate"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(residencyStartDateRaw))
            {
                if (!DateTime.TryParseExact(
                        residencyStartDateRaw,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedResidencyStartDate))
                {
                    return Results.BadRequest(new
                    {
                        error = "Residency start date must use YYYY-MM-DD format."
                    });
                }

                residencyStartDate = parsedResidencyStartDate.Date;
            }

            if (!PriorYearLossJson.TryParseInput(form["priorYearLosses"].FirstOrDefault(), out var priorYearLosses, out var lossError))
                return Results.BadRequest(new { error = lossError });

            if (!TryParseAccountLabels(form["accountLabels"].FirstOrDefault(), out var accountLabels, out var labelError))
                return Results.BadRequest(new { error = labelError });

            int? taxYear = null;
            var taxYearRaw = form["taxYear"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(taxYearRaw))
            {
                if (!int.TryParse(taxYearRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTaxYear) || parsedTaxYear < 1990 || parsedTaxYear > 2100)
                    return Results.BadRequest(new { error = "Tax year must be a four-digit year." });
                taxYear = parsedTaxYear;
            }

            var creditMethod = CreditMethod.TreatyCap;
            var creditMethodRaw = form["creditMethod"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(creditMethodRaw))
            {
                if (!Enum.TryParse(creditMethodRaw, ignoreCase: true, out creditMethod))
                    return Results.BadRequest(new { error = "creditMethod must be TreatyCap or StatutoryCap." });
            }

            var files = form.Files
                .Where(f => f.Length > 0)
                .ToList();

            if (files.Count == 0)
                return Results.BadRequest(new { error = "No file uploaded" });

            if (files.Count > UploadLimits.MaxFiles)
                return Results.BadRequest(new { error = $"Too many files ({files.Count}). At most {UploadLimits.MaxFiles} statements can be uploaded at once." });

            var oversizedFile = files.FirstOrDefault(file => file.Length > UploadLimits.MaxFileBytes);
            if (oversizedFile is not null)
            {
                return Results.Json(new
                {
                    error = $"File '{oversizedFile.FileName}' is too large ({oversizedFile.Length / 1024 / 1024} MB). Maximum allowed size is {UploadLimits.MaxFileMegabytes} MB per file.",
                    code = "file_too_large",
                    fileName = oversizedFile.FileName,
                    maxFileMb = UploadLimits.MaxFileMegabytes,
                    maxRequestMb = UploadLimits.MaxRequestMegabytes
                }, statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            try
            {
                var streams = files.Select(file => file.OpenReadStream()).ToList();
                try
                {
                    var result = await importService.ImportStatementsAsync(
                        streams, residencyStartDate, priorYearLosses, taxYear, files.Select(f => f.FileName).ToList(), accountLabels, creditMethod);
                    // Single source of truth for the form values: computed here with decimal arithmetic
                    // instead of being re-derived on the frontend with floating point.
                    var pit38 = Pit38Fields.FromSummary(result.Summary);

                    return Results.Ok(new
                    {
                        sessionId = result.SessionId,
                        availableYears = result.AvailableYears,
                        files = result.Files,
                        summary = EntityMapper.ToSummaryResponse(result.Summary, pit38),
                        pit38,
                        trades = result.Summary.TradeResults,
                        dividends = result.Summary.Dividends,
                        withholdingEvents = result.Summary.WithholdingEvents ?? []
                    });
                }
                finally
                {
                    foreach (var stream in streams)
                        stream.Dispose();
                }
            }
            catch (ImportFailedException ex)
            {
                // The years found in the upload let the user switch to one that can be calculated.
                return Results.BadRequest(new { error = ex.Message, availableYears = ex.AvailableYears, taxYear = ex.TaxYear });
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
            {
                // ObjectDisposedException is an InvalidOperationException too, but a server failure, not a bad request.
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();
    }

    private static IResult RequestTooLarge() => Results.Json(new
    {
        error = $"The upload is too large. All files together may not exceed {UploadLimits.MaxRequestMegabytes} MB, and each file {UploadLimits.MaxFileMegabytes} MB.",
        code = "request_too_large",
        maxFileMb = UploadLimits.MaxFileMegabytes,
        maxRequestMb = UploadLimits.MaxRequestMegabytes
    }, statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Parses the optional "accountLabels" form field: a JSON object mapping a file name to the label of the account
    /// the user says it belongs to (only used for statements that carry no account number).
    /// </summary>
    private static bool TryParseAccountLabels(string? raw, out Dictionary<string, string>? labels, out string? error)
    {
        labels = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        Dictionary<string, string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
        }
        catch (JsonException)
        {
            error = "Account labels must be a JSON object of { \"file name\": \"account label\" }.";
            return false;
        }

        if (parsed is null)
            return true;

        if (parsed.Count > UploadLimits.MaxFiles || parsed.Any(kv => kv.Value is null || kv.Value.Length > 64))
        {
            error = "Account labels are limited to 64 characters each.";
            return false;
        }

        labels = parsed
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
        return true;
    }
}
