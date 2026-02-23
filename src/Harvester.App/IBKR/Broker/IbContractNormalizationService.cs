using Harvester.App.IBKR.Contracts;
using IBApi;

namespace Harvester.App.IBKR.Broker;

public sealed class IbContractNormalizationService
{
    private static readonly HashSet<string> SupportedSecTypes =
    [
        "STK", "OPT", "FUT", "CASH", "CRYPTO", "CFD", "IND", "BAG"
    ];

    public Contract NormalizeAndBuild(BrokerContractSpec spec)
    {
        var normalizedSymbol = NormalizeSymbol(spec.Symbol);
        var normalizedExchange = NormalizeExchange(spec.Exchange);
        var normalizedCurrency = NormalizeCurrency(spec.Currency);

        return spec.AssetType switch
        {
            BrokerAssetType.Stock => ContractFactory.Stock(
                normalizedSymbol,
                exchange: normalizedExchange,
                currency: normalizedCurrency,
                primaryExchange: NormalizePrimaryExchange(spec.PrimaryExchange)),

            BrokerAssetType.Option => ContractFactory.Option(
                normalizedSymbol,
                NormalizeRequired(spec.Expiry, "expiry"),
                spec.Strike ?? throw new ArgumentException("Option strike is required."),
                NormalizeOptionRight(spec.Right),
                exchange: normalizedExchange,
                currency: normalizedCurrency,
                multiplier: NormalizeRequired(spec.Multiplier, "multiplier")),

            BrokerAssetType.Future => ContractFactory.Future(
                normalizedSymbol,
                NormalizeRequired(spec.Expiry, "expiry"),
                exchange: normalizedExchange,
                currency: normalizedCurrency),

            BrokerAssetType.Forex => ContractFactory.Forex(normalizedSymbol, exchange: normalizedExchange),

            BrokerAssetType.Crypto => ContractFactory.Crypto(normalizedSymbol, exchange: normalizedExchange, currency: normalizedCurrency),

            BrokerAssetType.Cfd => ContractFactory.Cfd(normalizedSymbol, exchange: normalizedExchange, currency: normalizedCurrency),

            BrokerAssetType.Index => ContractFactory.Index(normalizedSymbol, exchange: normalizedExchange, currency: normalizedCurrency),

            BrokerAssetType.Combo => ContractFactory.Bag(
                normalizedSymbol,
                normalizedCurrency,
                normalizedExchange,
                spec.ComboLegs ?? throw new ArgumentException("Combo contract requires combo legs.")),

            _ => throw new ArgumentException($"Unsupported broker asset type '{spec.AssetType}'.")
        };
    }

    public string NormalizeSecurityType(string secType)
    {
        var normalized = (secType ?? string.Empty).Trim().ToUpperInvariant();
        if (!SupportedSecTypes.Contains(normalized))
        {
            throw new ArgumentException($"Unsupported security type '{secType}'.");
        }

        return normalized;
    }

    public string NormalizeSymbol(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Symbol cannot be empty.");
        }

        return normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    public string NormalizeExchange(string exchange)
    {
        var normalized = (exchange ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Exchange cannot be empty.");
        }

        return normalized;
    }

    public string NormalizePrimaryExchange(string? exchange)
    {
        var normalized = (exchange ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    public string NormalizeCurrency(string currency)
    {
        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Currency cannot be empty.");
        }

        return normalized;
    }

    private static string NormalizeRequired(string? value, string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{field} is required.");
        }

        return normalized;
    }

    private static string NormalizeOptionRight(string? right)
    {
        var normalized = (right ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "C" or "CALL" => "C",
            "P" or "PUT" => "P",
            _ => throw new ArgumentException($"Invalid option right '{right}'. Use C|P.")
        };
    }
}
