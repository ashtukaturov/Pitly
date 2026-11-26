using System.Text.Json;
using System.Text.Json.Serialization;
using Pitly.Core.Tax;

namespace Pitly.Api.Mapping;

/// <summary>
/// Reads the optional "decisions" form field of an import: a JSON object such as
/// <c>{"dateBasis":"SettlementDate","securitiesLending":"Interest","paymentsInLieu":"Unconfirmed","crossYearRefunds":"CorrectOriginalYear"}</c>.
/// A missing field keeps its default; an unknown value is rejected, never guessed.
/// </summary>
public static class TaxDecisionsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static bool TryParse(string? raw, out TaxDecisions decisions, out string? error)
    {
        decisions = TaxDecisions.Default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        try
        {
            decisions = JsonSerializer.Deserialize<TaxDecisions>(raw, Options) ?? TaxDecisions.Default;
            return true;
        }
        catch (JsonException)
        {
            error = "decisions must be a JSON object with dateBasis (SettlementDate, TradeDate), securitiesLending (Unconfirmed, Interest, OtherSources), " +
                    "paymentsInLieu (Unconfirmed, Dividend, OtherSources) and crossYearRefunds (Unconfirmed, CorrectOriginalYear).";
            return false;
        }
    }
}
