namespace Harvester.App.Strategy;

public sealed class V3LiveRiskGuard
{
    private readonly V3LiveConfig _config;

    public V3LiveRiskGuard(V3LiveConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Evaluate pre-trade risk checks for a proposed order.
    /// Uses live position tracker data for accurate daily PnL and open risk.
    /// </summary>
    public V3LiveRiskCheckResult Evaluate(
        string symbol,
        DateTime timestampUtc,
        V3LiveFeatureSnapshot features,
        V3LiveSymbolRiskState state,
        V3LiveProposedOrder order)
    {
        var reasons = new List<string>();

        // C-02 FIX: Daily loss limit now uses live-tracked realized PnL
        if (state.RealizedPnlToday <= -Math.Abs(_config.MaxDailyLossDollars))
        {
            reasons.Add("daily-loss-limit");
        }

        // C-03 FIX: Open risk now uses live-tracked aggregate (decremented on close)
        if (state.OpenRiskDollars + order.EstimatedRiskDollars > _config.MaxOpenRiskDollars)
        {
            reasons.Add("max-open-risk");
        }

        // Duplicate position check: don't enter if already holding this symbol
        if (state.HasOpenPosition)
        {
            reasons.Add("duplicate-position");
        }

        var mid = features.L1.HasQuote ? (features.L1.Bid + features.L1.Ask) / 2.0 : features.Price;
        if (mid > 0)
        {
            var slippageBps = Math.Abs(order.EntryPrice - mid) / mid * 10_000.0;
            if (slippageBps > _config.MaxSlippageBps)
            {
                reasons.Add("slippage-bps");
            }
        }

        if (_config.RequireL2Depth && !features.L2.HasDepth)
        {
            reasons.Add("risk-l2-missing");
        }

        if (_config.RequireL2Depth)
        {
            if (order.Side == "BUY" && features.L2.ImbalanceRatio < _config.MinImbalanceLong)
            {
                reasons.Add("risk-imbalance-long");
            }

            if (order.Side == "SELL" && features.L2.ImbalanceRatio > _config.MaxImbalanceShort)
            {
                reasons.Add("risk-imbalance-short");
            }
        }

        if (features.Price < _config.MinPrice || features.Price > _config.MaxPrice)
        {
            reasons.Add("risk-price-out-of-range");
        }

        if (!double.IsNaN(features.Rvol) && features.Rvol < _config.RvolMin)
        {
            reasons.Add("risk-rvol-too-low");
        }

        if (!double.IsNaN(features.Adx14) && (features.Adx14 < _config.AdxMin || features.Adx14 > _config.AdxMax))
        {
            reasons.Add("risk-adx-out-of-range");
        }

        var pass = reasons.Count == 0;
        return new V3LiveRiskCheckResult(pass, reasons, state.OpenRiskDollars, order.EstimatedRiskDollars);
    }
}

/// <summary>
/// Risk state snapshot for the risk guard. Now populated from <see cref="V3LivePositionTracker"/>
/// which provides accurate live values that update on both entries and exits.
/// </summary>
public sealed class V3LiveSymbolRiskState
{
    /// <summary>Account-wide aggregate open risk (sum of all non-flat positions). Decremented on close.</summary>
    public double OpenRiskDollars { get; set; }

    /// <summary>Account-wide realized PnL today. Updated from position close events.</summary>
    public double RealizedPnlToday { get; set; }

    /// <summary>Whether this specific symbol already has an open position.</summary>
    public bool HasOpenPosition { get; set; }
}

public sealed record V3LiveRiskCheckResult(
    bool Passed,
    IReadOnlyList<string> Reasons,
    double CurrentOpenRiskDollars,
    double ProposedRiskDollars);
