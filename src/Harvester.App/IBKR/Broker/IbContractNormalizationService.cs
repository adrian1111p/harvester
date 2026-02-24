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

        var recoveredOption = TryRecoverOptionComponents(spec, normalizedSymbol);
        var recoveredFuture = TryRecoverFutureComponents(spec, normalizedSymbol);

        return spec.AssetType switch
        {
            BrokerAssetType.Stock => ContractFactory.Stock(
                normalizedSymbol,
                exchange: normalizedExchange,
                currency: normalizedCurrency,
                primaryExchange: NormalizePrimaryExchange(spec.PrimaryExchange)),

            BrokerAssetType.Option => ContractFactory.Option(
                recoveredOption.Symbol,
                recoveredOption.Expiry,
                recoveredOption.Strike,
                recoveredOption.Right,
                exchange: normalizedExchange,
                currency: normalizedCurrency,
                multiplier: NormalizeMultiplier(spec.Multiplier)),

            BrokerAssetType.Future => ContractFactory.Future(
                recoveredFuture.Symbol,
                recoveredFuture.Expiry,
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

    private static string NormalizeMultiplier(string? multiplier)
    {
        var normalized = (multiplier ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "100" : normalized;
    }

    private static (string Symbol, string Expiry) TryRecoverFutureComponents(BrokerContractSpec spec, string normalizedSymbol)
    {
        var explicitExpiry = NormalizeFutureExpiry(spec.Expiry);
        if (!string.IsNullOrWhiteSpace(explicitExpiry))
        {
            return (normalizedSymbol, explicitExpiry);
        }

        var symbol = normalizedSymbol;
        var digits = new string(symbol.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (digits.Length >= 6)
        {
            var candidate = digits[..6];
            if (LooksLikeYearMonth(candidate))
            {
                var baseSymbol = symbol[..^digits.Length];
                if (!string.IsNullOrWhiteSpace(baseSymbol))
                {
                    return (baseSymbol, candidate);
                }
            }
        }

        throw new ArgumentException("expiry is required for future contracts (accepted formats: yyyyMM, yyyy-MM, yyyyMMdd, yyyy-MM-dd, or symbol suffix like ES202503).");
    }

    private static (string Symbol, string Expiry, double Strike, string Right) TryRecoverOptionComponents(BrokerContractSpec spec, string normalizedSymbol)
    {
        var explicitExpiry = NormalizeOptionExpiry(spec.Expiry);
        var explicitRight = NormalizeOptionRightAllowEmpty(spec.Right);
        var explicitStrike = spec.Strike;

        if (!string.IsNullOrWhiteSpace(explicitExpiry) && explicitStrike.HasValue && !string.IsNullOrWhiteSpace(explicitRight))
        {
            return (normalizedSymbol, explicitExpiry, explicitStrike.Value, explicitRight);
        }

        if (TryParseOccOptionSymbol(normalizedSymbol, out var parsed))
        {
            return (
                parsed.Symbol,
                parsed.Expiry,
                explicitStrike ?? parsed.Strike,
                string.IsNullOrWhiteSpace(explicitRight) ? parsed.Right : explicitRight);
        }

        if (string.IsNullOrWhiteSpace(explicitExpiry) || !explicitStrike.HasValue || string.IsNullOrWhiteSpace(explicitRight))
        {
            throw new ArgumentException("Option contract requires expiry/right/strike, or OCC-style symbol encoding (e.g., AAPL240621C00195000).");
        }

        return (normalizedSymbol, explicitExpiry, explicitStrike.Value, explicitRight);
    }

    private static bool TryParseOccOptionSymbol(string symbol, out (string Symbol, string Expiry, string Right, double Strike) parsed)
    {
        parsed = default;
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length < 15)
        {
            return false;
        }

        var rightIndex = normalized.LastIndexOf('C');
        if (rightIndex < 0)
        {
            rightIndex = normalized.LastIndexOf('P');
        }

        if (rightIndex < 7 || rightIndex + 9 > normalized.Length)
        {
            return false;
        }

        var strikeToken = normalized[(rightIndex + 1)..];
        if (strikeToken.Length != 8 || !strikeToken.All(char.IsDigit))
        {
            return false;
        }

        var dateToken = normalized[(rightIndex - 6)..rightIndex];
        if (!dateToken.All(char.IsDigit))
        {
            return false;
        }

        var root = normalized[..(rightIndex - 6)].Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var expiry = $"20{dateToken}";
        if (!DateTime.TryParseExact(expiry, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
        {
            return false;
        }

        if (!double.TryParse(strikeToken, out var strikeRaw))
        {
            return false;
        }

        parsed = (
            root,
            expiry,
            normalized[rightIndex].ToString(),
            strikeRaw / 1000.0);
        return true;
    }

    private static string NormalizeFutureExpiry(string? expiry)
    {
        var normalized = (expiry ?? string.Empty).Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length == 8 && DateTime.TryParseExact(normalized, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
        {
            return normalized[..6];
        }

        if (normalized.Length == 6 && LooksLikeYearMonth(normalized))
        {
            return normalized;
        }

        throw new ArgumentException($"Invalid future expiry '{expiry}'. Use yyyyMM or yyyyMMdd.");
    }

    private static string NormalizeOptionExpiry(string? expiry)
    {
        var normalized = (expiry ?? string.Empty).Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length == 8 && DateTime.TryParseExact(normalized, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
        {
            return normalized;
        }

        if (normalized.Length == 6 && DateTime.TryParseExact(normalized + "01", "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
        {
            return normalized;
        }

        throw new ArgumentException($"Invalid option expiry '{expiry}'. Use yyyyMMdd or yyyyMM.");
    }

    private static bool LooksLikeYearMonth(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 6 || !value.All(char.IsDigit))
        {
            return false;
        }

        var year = int.Parse(value[..4]);
        var month = int.Parse(value[4..]);
        return year >= 1970 && year <= 2100 && month is >= 1 and <= 12;
    }

    private static string NormalizeOptionRightAllowEmpty(string? right)
    {
        var normalized = (right ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return NormalizeOptionRight(right);
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
