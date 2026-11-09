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
    string? Isin = null);

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
    string? Isin = null);
