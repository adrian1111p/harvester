namespace Harvester.App.IBKR.Risk;

public enum PreTradeCostProfile
{
    MicroEquity,
    Conservative
}

public sealed class PreTradeCostRiskEstimator
{
    public PreTradeCostEstimate Estimate(
        string route,
        string symbol,
        string action,
        double quantity,
        double limitPrice,
        string orderRef,
        PreTradeCostProfile profile,
        double commissionPerUnit,
        double slippageBps)
    {
        var normalizedQty = Math.Abs(quantity);
        var notional = normalizedQty * Math.Max(0, limitPrice);

        var profileMultiplier = profile == PreTradeCostProfile.Conservative ? 1.35 : 1.0;
        var estimatedCommission = normalizedQty * Math.Max(0, commissionPerUnit) * profileMultiplier;
        var estimatedSlippage = notional * (Math.Max(0, slippageBps) / 10000.0) * profileMultiplier;

        return new PreTradeCostEstimate(
            DateTime.UtcNow,
            route,
            symbol,
            action,
            normalizedQty,
            limitPrice,
            notional,
            estimatedCommission,
            estimatedSlippage,
            profile.ToString(),
            orderRef);
    }
}

public sealed record PreTradeCostEstimate(
    DateTime TimestampUtc,
    string Route,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    double EstimatedCommission,
    double EstimatedSlippage,
    string Profile,
    string OrderRef
);

public sealed record PreTradeCostTelemetryRow(
    DateTime TimestampUtc,
    string Route,
    string Symbol,
    string Action,
    int OrderId,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Profile,
    string OrderRef,
    double EstimatedCommission,
    double? RealizedCommission,
    double? CommissionDelta,
    double EstimatedSlippage,
    double? RealizedSlippage,
    double? SlippageDelta
);
