namespace Harvester.App.Strategy;

public sealed class V3LiveRiskGuard
{
    private readonly V3LiveConfig _config;

    public V3LiveRiskGuard(V3LiveConfig config)
    {
        _config = config;
    }

    public V3LiveRiskCheckResult Evaluate(
        string symbol,
        DateTime timestampUtc,
        V3LiveFeatureSnapshot features,
        V3LiveSymbolRiskState state,
        V3LiveProposedOrder order)
    {
        var reasons = new List<string>();

        if (state.RealizedPnlToday <= -Math.Abs(_config.MaxDailyLossDollars))
        {
            reasons.Add("daily-loss-limit");
        }

        if (state.OpenRiskDollars + order.EstimatedRiskDollars > _config.MaxOpenRiskDollars)
        {
            reasons.Add("max-open-risk");
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

        var pass = reasons.Count == 0;
        return new V3LiveRiskCheckResult(pass, reasons, state.OpenRiskDollars, order.EstimatedRiskDollars);
    }
}

public sealed class V3LiveSymbolRiskState
{
    public double OpenRiskDollars { get; set; }
    public double RealizedPnlToday { get; set; }
}

public sealed record V3LiveRiskCheckResult(
    bool Passed,
    IReadOnlyList<string> Reasons,
    double CurrentOpenRiskDollars,
    double ProposedRiskDollars);
