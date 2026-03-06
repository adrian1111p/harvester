using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public static class V3LiveSymbolResolver
{
    public static string Resolve(
        string? contextSymbol,
        IReadOnlyList<PositionRow> positions,
        IReadOnlyList<string> configuredSymbols)
    {
        if (!string.IsNullOrWhiteSpace(contextSymbol))
            return contextSymbol.Trim().ToUpperInvariant();

        var posSymbol = positions
            .Where(p => !string.IsNullOrWhiteSpace(p.Symbol)
                && string.Equals(p.SecurityType, "STK", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Symbol.Trim().ToUpperInvariant())
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(posSymbol))
            return posSymbol;

        return configuredSymbols.FirstOrDefault() ?? string.Empty;
    }
}
