using Harvester.App.IBKR.Risk;

namespace Harvester.App.Tests;

public sealed class RiskTests
{
    [Fact]
    public void PreTradeDsl_FlagsNotionalAndSessionViolations()
    {
        var dsl = new PreTradeControlDsl();
        var context = new PreTradeContext("live", "AAPL", "BUY", 10, 20, 200, 1);

        var violations = dsl.Evaluate(
            context,
            "max-notional=reject;session-window=halt",
            maxNotional: 100,
            maxQty: 100,
            maxDailyOrders: 5,
            sessionStart: new TimeOnly(13, 30),
            sessionEnd: new TimeOnly(14, 0),
            nowUtc: new DateTime(2026, 3, 4, 16, 0, 0, DateTimeKind.Utc));

        Assert.Contains(violations, v => v.Guard == "max-notional" && v.Action == PreTradeAction.Reject);
        Assert.Contains(violations, v => v.Guard == "session-window" && v.Action == PreTradeAction.Halt);
    }

    [Fact]
    public void FaRoutingValidator_RejectsGroupAndProfileTogether()
    {
        var validator = new FaRoutingValidator();

        var issues = validator.Validate(
            masterAccount: "DU123",
            faOrderAccount: "DU123",
            faOrderGroup: "G1",
            faOrderProfile: "P1",
            faOrderMethod: "EqualQuantity",
            faOrderPercentage: string.Empty);

        Assert.Contains(issues, x => x.Contains("cannot include both fa-order-group and fa-order-profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreTradeCostEstimator_ConservativeProfileInflatesCommissionAndSlippage()
    {
        var estimator = new PreTradeCostRiskEstimator();

        var micro = estimator.Estimate("route", "AAPL", "BUY", 100, 10, "ref", PreTradeCostProfile.MicroEquity, 0.01, 5);
        var conservative = estimator.Estimate("route", "AAPL", "BUY", 100, 10, "ref", PreTradeCostProfile.Conservative, 0.01, 5);

        Assert.True(conservative.EstimatedCommission > micro.EstimatedCommission);
        Assert.True(conservative.EffectiveSlippageBps > micro.EffectiveSlippageBps);
    }
}
