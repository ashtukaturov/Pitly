using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pitly.Core.Models;
using static Pitly.Core.Parsing.CsvHelpers;

namespace Pitly.Broker.InteractiveBrokers;

/// <summary>
/// Parses the "Corporate Actions" section of an IB Activity Statement.
/// Column layout: Corporate Actions,Data,AssetCategory,Currency,ReportDate,Date/Time,Description,Quantity,Proceeds,Value,Realized P/L,Code
/// Description layout: "LEAD(LEADISIN) &lt;action text&gt; (ROWSYMBOL, ROW NAME, ROWISIN)" where the trailing
/// parenthetical names the instrument this row credits (quantity &gt; 0) or debits (quantity &lt; 0).
/// </summary>
public partial class InteractiveBrokersStatementParser
{
    private enum ActionKind { Unknown, Split, IsinChange, Merger, SpinOff, StockDividend, CashInLieu }

    private sealed record ActionRow(
        DateTime DateTime,
        string Currency,
        string Description,
        decimal Quantity,
        decimal Proceeds,
        string LeadSymbol,
        string? LeadIsin,
        string RowSymbol,
        string? RowIsin,
        ActionKind Kind,
        decimal? Numerator,
        decimal? Denominator,
        decimal? CashPerShare);

    private List<CorporateAction> ParseCorporateActions(
        List<List<string>> rows,
        IReadOnlyDictionary<string, string> symbolToIsin,
        List<string> warnings)
    {
        var parsed = new List<ActionRow>();
        foreach (var fields in rows)
        {
            var row = TryParseActionRow(fields, symbolToIsin, warnings);
            if (row is not null)
                parsed.Add(row);
        }

        var actions = new List<CorporateAction>();

        // Rows of one event share the timestamp, kind and ratio/price; the debit and credit halves are paired here.
        foreach (var group in parsed
                     .GroupBy(r => (r.DateTime, r.Kind, r.Numerator, r.Denominator, r.CashPerShare, Lead: r.Kind == ActionKind.SpinOff ? r.LeadSymbol : string.Empty))
                     .OrderBy(g => g.Key.DateTime))
        {
            var debits = group.Where(r => r.Quantity < 0).ToList();
            var credits = group.Where(r => r.Quantity > 0).ToList();
            var any = group.First();
            var proceeds = group.Sum(r => r.Proceeds);
            var date = any.DateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            switch (any.Kind)
            {
                case ActionKind.Split:
                {
                    var debit = debits.FirstOrDefault();
                    var credit = credits.FirstOrDefault() ?? debit;
                    if (credit is null)
                        break;
                    var (num, den) = ResolveRatio(any, credits, debits);
                    if (num is null || den is null)
                    {
                        warnings.Add($"Stock split on {date} could not be applied: ratio not found in '{any.Description}'. Check the position's lots manually.");
                        break;
                    }

                    actions.Add(new CorporateAction(
                        Symbol: credit.RowSymbol,
                        DateTime: any.DateTime,
                        Type: CorporateActionType.StockSplit,
                        Numerator: num.Value,
                        Denominator: den.Value,
                        Isin: credit.RowIsin,
                        SourceSymbol: debit?.RowSymbol ?? credit.LeadSymbol,
                        SourceIsin: debit?.RowIsin ?? credit.LeadIsin,
                        Currency: any.Currency,
                        Description: any.Description));
                    if (proceeds > 0)
                        actions.Add(CashInLieu(credit, proceeds, any));
                    break;
                }

                case ActionKind.IsinChange:
                {
                    var debit = debits.FirstOrDefault();
                    var credit = credits.FirstOrDefault();
                    if (credit is null && debit is null)
                        break;
                    var target = credit ?? debit!;
                    var source = debit ?? credit!;
                    actions.Add(new CorporateAction(
                        Symbol: target.RowSymbol,
                        DateTime: any.DateTime,
                        Type: CorporateActionType.IsinChange,
                        Numerator: 1m,
                        Denominator: 1m,
                        Isin: credit is not null ? target.RowIsin : TrailingIsin(target.Description) ?? target.RowIsin,
                        SourceSymbol: source.RowSymbol,
                        SourceIsin: debit is not null ? source.RowIsin : source.LeadIsin,
                        Currency: any.Currency,
                        Description: any.Description));
                    break;
                }

                case ActionKind.Merger:
                {
                    var debit = debits.FirstOrDefault();
                    var credit = credits.FirstOrDefault();
                    if (credit is not null)
                    {
                        var (num, den) = ResolveRatio(any, credits, debits);
                        if (num is null || den is null)
                        {
                            warnings.Add($"Merger on {date} could not be applied: exchange ratio not found in '{any.Description}'. Check the position's lots manually.");
                            break;
                        }

                        actions.Add(new CorporateAction(
                            Symbol: credit.RowSymbol,
                            DateTime: any.DateTime,
                            Type: CorporateActionType.StockMerger,
                            Numerator: num.Value,
                            Denominator: den.Value,
                            Isin: credit.RowIsin,
                            SourceSymbol: debit?.RowSymbol ?? credit.LeadSymbol,
                            SourceIsin: debit?.RowIsin ?? credit.LeadIsin,
                            Quantity: credit.Quantity,
                            CashProceeds: proceeds > 0 ? proceeds : 0m,
                            Currency: any.Currency,
                            Description: any.Description));
                        warnings.Add(
                            $"Merger on {date}: {debit?.RowSymbol ?? credit.LeadSymbol} shares were exchanged for {credit.Quantity} {credit.RowSymbol} shares. " +
                            "Treated as tax-neutral: the original acquisition cost carries over to the new shares (art. 24 ust. 8 updof)." +
                            (proceeds > 0 ? $" The cash part ({proceeds} {any.Currency}) is reported as proceeds with zero cost." : string.Empty));
                        break;
                    }

                    if (debit is null)
                        break;

                    if (proceeds <= 0)
                    {
                        warnings.Add(
                            $"Corporate action on {date} removed {Math.Abs(debit.Quantity)} {debit.RowSymbol} shares without cash or new shares " +
                            $"('{any.Description}'). Nothing was recorded; a loss on worthless or delisted shares is only deductible once they are actually sold.");
                        break;
                    }

                    actions.Add(new CorporateAction(
                        Symbol: debit.RowSymbol,
                        DateTime: any.DateTime,
                        Type: CorporateActionType.CashMerger,
                        Numerator: 1m,
                        Denominator: 1m,
                        Isin: debit.RowIsin,
                        Quantity: Math.Abs(debit.Quantity),
                        CashProceeds: proceeds,
                        Currency: any.Currency,
                        Description: any.Description));
                    warnings.Add(
                        $"Cash merger / acquisition on {date}: {Math.Abs(debit.Quantity)} {debit.RowSymbol} shares were exchanged for {proceeds} {any.Currency}. " +
                        "Treated as a sale (odplatne zbycie) and included in capital gains.");
                    break;
                }

                case ActionKind.SpinOff:
                {
                    foreach (var credit in credits)
                    {
                        actions.Add(new CorporateAction(
                            Symbol: credit.RowSymbol,
                            DateTime: any.DateTime,
                            Type: CorporateActionType.SpinOff,
                            Numerator: credit.Numerator ?? 1m,
                            Denominator: credit.Denominator ?? 1m,
                            Isin: credit.RowIsin,
                            SourceSymbol: credit.LeadSymbol,
                            SourceIsin: credit.LeadIsin,
                            Quantity: credit.Quantity,
                            CashProceeds: credit.Proceeds > 0 ? credit.Proceeds : 0m,
                            Currency: credit.Currency,
                            Description: credit.Description));
                        warnings.Add(
                            $"Spin-off on {date}: {credit.Quantity} {credit.RowSymbol} shares received from {credit.LeadSymbol} were added with zero acquisition cost " +
                            $"and {credit.LeadSymbol} keeps its full cost (conservative reading of art. 22 updof). " +
                            "If you have an individual interpretation allowing proportional cost allocation, adjust the cost of these shares manually.");
                    }

                    if (credits.Count == 0 && proceeds > 0)
                        actions.Add(CashInLieu(any, proceeds, any));
                    break;
                }

                case ActionKind.StockDividend:
                {
                    foreach (var credit in credits)
                    {
                        actions.Add(new CorporateAction(
                            Symbol: credit.RowSymbol,
                            DateTime: any.DateTime,
                            Type: CorporateActionType.StockDividend,
                            Numerator: credit.Numerator ?? 1m,
                            Denominator: credit.Denominator ?? 1m,
                            Isin: credit.RowIsin,
                            Quantity: credit.Quantity,
                            CashProceeds: credit.Proceeds > 0 ? credit.Proceeds : 0m,
                            Currency: credit.Currency,
                            Description: credit.Description));
                        warnings.Add(
                            $"Stock dividend on {date}: {credit.Quantity} {credit.RowSymbol} shares were added with zero acquisition cost. " +
                            "Depending on the issuer's country this may also be taxable as dividend income; verify with the broker's tax documents.");
                    }
                    break;
                }

                case ActionKind.CashInLieu:
                {
                    if (proceeds > 0)
                        actions.Add(CashInLieu(any, proceeds, any));
                    break;
                }

                default:
                    warnings.Add(
                        $"Corporate action on {date} was not recognised and was ignored: '{any.Description}'. " +
                        "Check the affected position's lots manually.");
                    break;
            }
        }

        return actions;
    }

    private static CorporateAction CashInLieu(ActionRow instrument, decimal proceeds, ActionRow any)
        => new(
            Symbol: instrument.RowSymbol,
            DateTime: any.DateTime,
            Type: CorporateActionType.CashInLieu,
            Numerator: 1m,
            Denominator: 1m,
            Isin: instrument.RowIsin,
            Quantity: any.Kind == ActionKind.CashInLieu ? Math.Abs(any.Quantity) : 0m,
            CashProceeds: proceeds,
            Currency: any.Currency,
            Description: any.Description);

    private static (decimal? Numerator, decimal? Denominator) ResolveRatio(ActionRow any, List<ActionRow> credits, List<ActionRow> debits)
    {
        if (any.Numerator is not null && any.Denominator is not null && any.Denominator != 0)
            return (any.Numerator, any.Denominator);

        // No "N for M" in the text: derive the ratio from the debited and credited share counts.
        var creditQty = credits.Sum(r => r.Quantity);
        var debitQty = Math.Abs(debits.Sum(r => r.Quantity));
        if (creditQty > 0 && debitQty > 0)
            return (creditQty, debitQty);

        return (null, null);
    }

    private ActionRow? TryParseActionRow(
        List<string> fields,
        IReadOnlyDictionary<string, string> symbolToIsin,
        List<string> warnings)
    {
        if (fields.Count < 8 || Clean(fields[1]) != "Data" || Clean(fields[2]) != "Stocks")
            return null;

        var description = Clean(fields[6]);
        if (string.IsNullOrWhiteSpace(description) || description.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
            return null;

        var dateTimeStr = Clean(fields[5]);
        if (!TryParseDateTime(dateTimeStr, out var dateTime) && !TryParseDate(Clean(fields[4]), out dateTime))
        {
            _logger.LogWarning("Skipping corporate action row: could not parse date '{DateStr}'", dateTimeStr);
            warnings.Add($"Corporate action row skipped because its date could not be read: '{description}'.");
            return null;
        }

        var currency = Clean(fields[3]);
        TryParseDecimal(Clean(fields[7]), out var quantity);
        var proceeds = 0m;
        if (fields.Count > 8)
            TryParseDecimal(Clean(fields[8]), out proceeds);

        var lead = LeadInstrumentRegex().Match(description);
        var leadSymbol = lead.Success ? lead.Groups["symbol"].Value : string.Empty;
        var leadIsin = lead.Success && lead.Groups["isin"].Success ? lead.Groups["isin"].Value : null;

        var trailing = TrailingInstrumentRegex().Match(description);
        var rowSymbol = trailing.Success ? trailing.Groups["symbol"].Value.Trim() : leadSymbol;
        var rowIsin = trailing.Success ? trailing.Groups["isin"].Value.Trim() : leadIsin;

        if (string.IsNullOrWhiteSpace(rowSymbol))
        {
            warnings.Add($"Corporate action row skipped because the instrument could not be read: '{description}'.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(rowIsin) && symbolToIsin.TryGetValue(rowSymbol, out var mappedIsin))
            rowIsin = mappedIsin;
        if (string.IsNullOrWhiteSpace(leadIsin) && !string.IsNullOrWhiteSpace(leadSymbol) && symbolToIsin.TryGetValue(leadSymbol, out var mappedLeadIsin))
            leadIsin = mappedLeadIsin;

        // The action text sits between the lead instrument and the trailing instrument.
        var body = description;
        if (lead.Success) body = body[lead.Length..];
        if (trailing.Success) body = body[..^trailing.Length];
        body = body.Trim();

        decimal? numerator = null, denominator = null;
        var ratio = RatioRegex().Match(body);
        if (ratio.Success &&
            TryParseDecimal(ratio.Groups["num"].Value, out var num) &&
            TryParseDecimal(ratio.Groups["den"].Value, out var den) && den != 0)
        {
            numerator = num;
            denominator = den;
        }

        decimal? cashPerShare = null;
        var cash = CashPerShareRegex().Match(body);
        if (cash.Success && TryParseDecimal(cash.Groups["price"].Value, out var price))
        {
            cashPerShare = price;
            if (string.IsNullOrWhiteSpace(currency))
                currency = cash.Groups["ccy"].Value;
        }

        return new ActionRow(
            dateTime,
            string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
            description,
            quantity,
            proceeds,
            string.IsNullOrWhiteSpace(leadSymbol) ? rowSymbol : leadSymbol,
            leadIsin,
            rowSymbol,
            string.IsNullOrWhiteSpace(rowIsin) ? null : rowIsin,
            ClassifyAction(body),
            numerator,
            denominator,
            cashPerShare);
    }

    private static ActionKind ClassifyAction(string body)
    {
        var text = body.ToLowerInvariant();
        if (text.Contains("in lieu"))
            return ActionKind.CashInLieu;
        if (text.Contains("isin change") || text.Contains("cusip change") || text.Contains("cusip/isin change") || text.Contains("symbol change") || text.Contains("ticker change"))
            return ActionKind.IsinChange;
        if (text.Contains("spinoff") || text.Contains("spin-off") || text.Contains("spin off"))
            return ActionKind.SpinOff;
        if (text.Contains("stock dividend") || text.Contains("bonus issue") || text.Contains("bonus shares"))
            return ActionKind.StockDividend;
        if (SplitWordRegex().IsMatch(text))
            return ActionKind.Split;
        if (text.Contains("merge") || text.Contains("acquisition") || text.Contains("acquired") || text.Contains("tender") ||
            text.Contains("buyback") || text.Contains("buy-back") || text.Contains("cash in exchange") || text.Contains("exchange offer"))
            return ActionKind.Merger;
        return ActionKind.Unknown;
    }

    private static string? TrailingIsin(string description)
    {
        var trailing = TrailingInstrumentRegex().Match(description);
        return trailing.Success ? trailing.Groups["isin"].Value : null;
    }

    [GeneratedRegex(@"^\s*(?<symbol>[A-Za-z0-9._\-]+)\s*\((?<isin>[A-Za-z0-9]{6,12})\)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadInstrumentRegex();

    [GeneratedRegex(@"\((?<symbol>[A-Za-z0-9._\-]+),\s*(?<name>[^,()]*),\s*(?<isin>[A-Za-z0-9]{6,12})\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingInstrumentRegex();

    [GeneratedRegex(@"(?<num>\d+(?:\.\d+)?)\s+for\s+(?<den>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex RatioRegex();

    [GeneratedRegex(@"\b(?<ccy>[A-Z]{3})\s+(?<price>\d+(?:\.\d+)?)\s+per\s+share", RegexOptions.IgnoreCase)]
    private static partial Regex CashPerShareRegex();

    [GeneratedRegex(@"\bsplit\b", RegexOptions.IgnoreCase)]
    private static partial Regex SplitWordRegex();
}
