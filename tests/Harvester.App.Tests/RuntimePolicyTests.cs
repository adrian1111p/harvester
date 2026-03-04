using Harvester.App.IBKR.Runtime;
using Harvester.App.IBKR.Wrapper;

namespace Harvester.App.Tests;

public sealed class RuntimePolicyTests
{
    [Fact]
    public void IbErrorPolicy_ClassifiesRetryableConnectivityCode()
    {
        var policy = new IbErrorPolicy();
        var error = new IbApiError(DateTime.UtcNow, 1, 1100, "Connectivity between IB and TWS has been lost", "raw");

        var decision = policy.Evaluate(error, RunMode.Connect, optionGreeksAutoFallback: false);

        Assert.Equal(IbErrorAction.Retry, decision.Action);
    }

    [Fact]
    public void OrderLifecycleModel_NormalizesStatusesAndTerminalTransition()
    {
        Assert.Equal(HarvesterOrderLifecycleState.Working, OrderLifecycleModel.NormalizeStatus("Submitted"));
        Assert.False(OrderLifecycleModel.IsTransitionAllowed(HarvesterOrderLifecycleState.Filled, HarvesterOrderLifecycleState.Working));
    }

    [Fact]
    public void OrderLifecycleModel_BuildSummaryCountsInvalidTransitions()
    {
        var transitions = new[]
        {
            new OrderLifecycleTransitionArtifactRow(DateTime.UtcNow, 1, 0, "AAPL", "open", "Submitted", HarvesterOrderLifecycleState.Unknown, HarvesterOrderLifecycleState.Working, true, "ok"),
            new OrderLifecycleTransitionArtifactRow(DateTime.UtcNow.AddSeconds(1), 1, 0, "AAPL", "update", "Submitted", HarvesterOrderLifecycleState.Filled, HarvesterOrderLifecycleState.Working, false, "invalid")
        };

        var summary = OrderLifecycleModel.BuildSummary("test", transitions);

        Assert.Equal(1, summary.OrdersObserved);
        Assert.Equal(1, summary.InvalidTransitionCount);
    }
}
