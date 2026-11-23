using System.Text.RegularExpressions;

namespace Pitly.Core.Models;

/// <summary>
/// Income whose legal classification the statement does not establish. Pitly keeps such income in the amounts
/// under the kind the broker's label suggests and attaches a blocking finding with the note below, so that the
/// user confirms the classification before filing. No rule is added here to make the result agree with another
/// calculator; the question is open until a source settles it.
/// </summary>
public static partial class IncomeClassification
{
    /// <summary>
    /// Fees for lending out shares (IB "Stock Yield Enhancement Program", SYEP). IB books them with interest, but
    /// a lending fee is not interest on a loan of money (art. 30a ust. 1 pkt 3); whether it is taxed at 19% in
    /// section G or as income from property rights at the scale is not settled by any source Pitly cites.
    /// </summary>
    public const string SecuritiesLendingNote =
        "income from lending out shares (stock yield enhancement) is listed with interest, but its classification is not established: " +
        "art. 30a ust. 1 pkt 3 covers interest on money lent, and no source Pitly cites settles whether a lending fee belongs there or to another source of income. " +
        "It is included as interest; confirm the classification before filing.";

    /// <summary>
    /// A payment in lieu of a dividend: a substitute paid by the borrower of the shares, not a dividend from the
    /// issuer. It is included as dividend-equivalent flat-tax income, which is not settled.
    /// </summary>
    public const string PaymentInLieuNote =
        "a payment in lieu of a dividend is a substitute paid by the borrower of the shares, not a dividend of the issuer: whether it is taxed as a dividend " +
        "(art. 30a ust. 1 pkt 4) with a creditable foreign tax, or as other income, is not established by any source Pitly cites. " +
        "It is included like a dividend; confirm the classification before filing.";

    public static bool IsSecuritiesLending(string? description)
        => !string.IsNullOrWhiteSpace(description) && LendingRegex().IsMatch(description);

    [GeneratedRegex(@"\bSYEP\b|STOCK\s+YIELD|YIELD\s+ENHANCEMENT|SECURIT(Y|IES)\s+LENDING|LENDING\s+(FEE|INCOME|INTEREST)|BORROW\s+FEE", RegexOptions.IgnoreCase)]
    private static partial Regex LendingRegex();
}
