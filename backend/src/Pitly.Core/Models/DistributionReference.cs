using System.Globalization;
using System.Text.RegularExpressions;

namespace Pitly.Core.Models;

/// <summary>
/// The broker's own text of one distribution inside an instrument, as Interactive Brokers prints it on every row
/// of that distribution: "TLT(US4642874329) Cash Dividend USD 0.310122 per Share - US Tax" gives
/// "USD 0.310122 PER SHARE". An Activity Statement books a correction under the day it was made and repeats this
/// text, so it is the only link the statement gives between a refund of the next year and the payment it corrects.
/// The text names a distribution, not a payment: a fixed quarterly dividend prints the same text four times, so it
/// identifies a payment only when exactly one payment of the instrument carries it (<see cref="Tax.WithholdingMatcher"/>).
/// </summary>
public static partial class DistributionReference
{
    public static string? Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var match = PerShareRegex().Match(description);
        if (!match.Success)
            return null;
        if (!decimal.TryParse(match.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            return null;
        // Normalised so that "0.250" and "0.25" are one reference.
        return match.Groups["currency"].Value.ToUpperInvariant() + " " + amount.ToString("0.############", CultureInfo.InvariantCulture) + " PER SHARE";
    }

    [GeneratedRegex(@"\b(?<currency>[A-Z]{3})\s+(?<amount>\d+(?:\.\d+)?)\s+PER\s+SHARE\b", RegexOptions.IgnoreCase)]
    private static partial Regex PerShareRegex();
}
