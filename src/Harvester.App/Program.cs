using Harvester.App.IBKR.Runtime;
using Harvester.App.Strategy;

var options = AppOptions.Parse(args);
IStrategyRuntime? strategyRuntime = null;

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

var runtime = new SnapshotRuntime(options, strategyRuntime);
var exitCode = await runtime.RunAsync();
Environment.Exit(exitCode);
