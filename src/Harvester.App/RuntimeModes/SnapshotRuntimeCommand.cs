using Harvester.App.IBKR.Runtime;
using Harvester.App.Strategy;
using Microsoft.Extensions.Logging;

namespace Harvester.App.RuntimeModes;

public sealed class SnapshotRuntimeCommand : IRunModeCommand
{
    public bool CanHandle(RunMode mode) => true;

    public async Task<int> ExecuteAsync(AppOptions options, string[] args, ILoggerFactory loggerFactory)
    {
        IStrategyRuntime? strategyRuntime = null;

        if (options.Mode == RunMode.StrategyLiveV3)
        {
            var v3Logger = loggerFactory.CreateLogger<V3LiveRuntime>();
            strategyRuntime = new V3LiveRuntime(logger: v3Logger);
        }

        if (options.Mode == RunMode.StrategyReplay
            && !string.IsNullOrWhiteSpace(options.ReplayScannerCandidatesInputPath))
        {
            strategyRuntime = new ScannerCandidateReplayRuntime(
                options.ReplayScannerCandidatesInputPath,
                options.ReplayScannerTopN,
                options.ReplayScannerMinScore,
                options.ReplayScannerOrderQuantity,
                options.ReplayScannerOrderSide,
                options.ReplayScannerOrderType,
                options.ReplayScannerOrderTimeInForce,
                options.ReplayScannerLimitOffsetBps);
        }

        var runtimeLogger = loggerFactory.CreateLogger<SnapshotRuntime>();
        var runtime = new SnapshotRuntime(options, strategyRuntime, runtimeLogger);
        return await runtime.RunAsync();
    }
}
