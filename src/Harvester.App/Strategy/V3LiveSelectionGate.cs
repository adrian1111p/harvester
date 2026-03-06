namespace Harvester.App.Strategy;

public static class V3LiveSelectionGate
{
    public static (bool Passed, string ReasonCode) Evaluate(
        string symbol,
        V3LiveFeatureSnapshot features,
        V3LiveConfig config,
        ISet<string> tradeUniverse)
    {
        if (tradeUniverse.Count > 0 && !tradeUniverse.Contains(symbol))
            return (false, "scanner-symbol-outside-universe");

        if (!features.L1.HasQuote)
            return (false, "scanner-l1-missing");

        var spreadScore = features.L1.SpreadPct <= 0
            ? 0.0
            : Math.Clamp(((config.MaxSpreadPct - features.L1.SpreadPct) / Math.Max(1e-6, config.MaxSpreadPct)) * 100.0, 0.0, 100.0);

        var depthMin = Math.Min(features.L2.BidDepthN, features.L2.AskDepthN);
        var depthScore = Math.Clamp((depthMin / Math.Max(1.0, config.MinDepthPerSideShares)) * 100.0, 0.0, 100.0);

        var rvolScore = double.IsNaN(features.Rvol)
            ? 0.0
            : Math.Clamp((features.Rvol / Math.Max(0.1, config.RvolMin)) * 100.0, 0.0, 100.0);

        var imbalanceMid = 0.5 * (config.MinImbalanceLong + config.MaxImbalanceShort);
        var imbalancePenalty = Math.Min(1.0, Math.Abs(features.L2.ImbalanceRatio - imbalanceMid));
        var imbalanceScore = Math.Clamp((1.0 - imbalancePenalty) * 100.0, 0.0, 100.0);

        var composite = 0.35 * spreadScore + 0.30 * depthScore + 0.25 * rvolScore + 0.10 * imbalanceScore;
        if (composite < config.ScannerMinCompositeScore)
            return (false, "scanner-score-low");

        return (true, string.Empty);
    }
}
