namespace Pitly.Core.Models;

public record Trade(
    string Symbol,
    string Currency,
    DateTime DateTime,
    decimal Quantity,
    decimal Price,
    decimal Proceeds,
    decimal Commission,
    string CommissionCurrency,
    decimal RealizedPnL,
    TradeType Type,
    string? Isin = null,
    // Account whose FIFO queue the trade belongs to; stamped by StatementMerger
    string? AccountId = null,
    // Broker's own id of the trade (Trading 212 "ID", generic CSV "tx_id"), used to recognise the same row in
    // overlapping exports
    string? TransactionId = null,
    // Source country named by the statement (generic CSV "country"), used for PIT/ZG instead of the ISIN prefix
    string? CountryCode = null);

public enum TradeType { Buy, Sell }

public record TradeResult(
    string Symbol,
    DateTime DateTime,
    TradeType Type,
    decimal Quantity,
    decimal PriceOriginal,
    decimal ProceedsOriginal,
    decimal CommissionOriginal,
    string Currency,
    decimal ExchangeRate,
    decimal ProceedsPln,
    decimal CostPln,
    decimal GainLossPln,
    bool RateUnavailable = false,
    // Explains synthetic rows created from corporate actions (cash merger, spin-off shares, cash in lieu)
    string? Note = null,
    // Issuer ISIN, used to attribute the income to a country for PIT/ZG
    string? Isin = null,
    // Country named by the statement itself; takes precedence over the ISIN prefix for PIT/ZG
    string? CountryCode = null);
