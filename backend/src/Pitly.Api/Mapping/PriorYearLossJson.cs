using System.Text.Json;
using Pitly.Core.Models;

namespace Pitly.Api.Mapping;

/// <summary>
/// (De)serialises the user's prior-year loss entries: the import form sends them as a JSON array
/// (<c>[{"year":2023,"lossPln":1200.50,"deductedPln":0}]</c>) and the session stores the same JSON.
/// </summary>
public static class PriorYearLossJson
{
    public const int MaxEntries = 10;
    private const int MinYear = 1990;
    private const int MaxYear = 2100;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IReadOnlyList<PriorYearLoss>? losses)
        => losses is { Count: > 0 } ? JsonSerializer.Serialize(losses, Options) : null;

    public static List<PriorYearLoss> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<PriorYearLoss>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Parses and validates the import form field; returns an error message for a 400 response.</summary>
    public static bool TryParseInput(string? raw, out List<PriorYearLoss> losses, out string? error)
    {
        losses = [];
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        List<PriorYearLoss>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<PriorYearLoss>>(raw, Options);
        }
        catch (JsonException)
        {
            error = "Prior-year losses must be a JSON array of { year, lossPln, deductedPln } objects.";
            return false;
        }

        if (parsed is null || parsed.Count == 0)
            return true;

        if (parsed.Count > MaxEntries)
        {
            error = $"At most {MaxEntries} prior-year loss entries are allowed.";
            return false;
        }

        foreach (var loss in parsed)
        {
            if (loss.Year < MinYear || loss.Year > MaxYear)
            {
                error = $"Prior-year loss year {loss.Year} is out of range.";
                return false;
            }

            if (loss.LossPln <= 0m)
            {
                error = $"Prior-year loss for {loss.Year} must be a positive amount in PLN.";
                return false;
            }

            if (loss.DeductedPln < 0m)
            {
                error = $"Already-deducted amount for {loss.Year} cannot be negative.";
                return false;
            }

            if (loss.DeductedPln > loss.LossPln)
            {
                error = $"Already-deducted amount for {loss.Year} cannot exceed the loss itself.";
                return false;
            }
        }

        if (parsed.Select(l => l.Year).Distinct().Count() != parsed.Count)
        {
            error = "Each loss year can be entered only once.";
            return false;
        }

        losses = parsed
            .Select(l => new PriorYearLoss(l.Year, Math.Round(l.LossPln, 2), Math.Round(l.DeductedPln, 2), l.OneOffUsed))
            .OrderBy(l => l.Year)
            .ToList();
        return true;
    }
}
