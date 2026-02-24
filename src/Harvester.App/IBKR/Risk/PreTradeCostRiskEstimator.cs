namespace Harvester.App.IBKR.Risk;

public enum PreTradeCostProfile
{
    MicroEquity,
    Conservative,
    VolumeShareImpact
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
        if (profile == PreTradeCostProfile.VolumeShareImpact)
        {
            profileMultiplier = 1.0;
        }

        var assumedDailyVolume = Math.Max(1.0, Math.Max(normalizedQty * 20.0, 25000.0));
        var volumeShare = Math.Clamp(normalizedQty / assumedDailyVolume, 0, 1);
        var impactBps = profile == PreTradeCostProfile.VolumeShareImpact
            ? 2.0 + (45.0 * volumeShare * volumeShare)
            : 0.0;

        var effectiveSlippageBps = Math.Max(0, slippageBps) * profileMultiplier + impactBps;
        var estimatedCommission = normalizedQty * Math.Max(0, commissionPerUnit) * profileMultiplier;
        var estimatedSlippage = notional * (effectiveSlippageBps / 10000.0);

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
            effectiveSlippageBps,
            volumeShare,
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
    double EffectiveSlippageBps,
    double EstimatedVolumeShare,
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
    double EstimatedSlippageBps,
    double EstimatedVolumeShare,
    double? RealizedCommission,
    double? CommissionDelta,
    double EstimatedSlippage,
    double? RealizedSlippage,
    double? SlippageDelta
);
