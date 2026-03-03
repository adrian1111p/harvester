using Harvester.App.Backtest.Runner;
using Harvester.App.Backtest.Strategies;
using Harvester.App.IBKR.Runtime;
using Harvester.App.Strategy;

var options = AppOptions.Parse(args);

// ── Backtest modes (no IBKR connection needed) ──────────────────────────────
if (options.Mode is RunMode.BacktestRun or RunMode.BacktestSweep
    or RunMode.BacktestOptimize or RunMode.BacktestScan
    or RunMode.BacktestLiveSim or RunMode.BacktestCompare)
{
    // Parse backtest-specific args: --backtest-symbols AAPL,TSLA,NVDA
    var backtestSymbols = BacktestRunner.DefaultSymbols;
    var backtestProfile = "conduct";
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--backtest-symbols")
            backtestSymbols = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (args[i] == "--backtest-profile")
            backtestProfile = args[i + 1].Trim().ToLowerInvariant();
    }

    switch (options.Mode)
    {
        case RunMode.BacktestRun:
            Console.WriteLine("Harvester Backtest Engine V2.0 (C#)");
            Console.WriteLine($"Symbols: {string.Join(", ", backtestSymbols)}");
            if (backtestProfile == "first")
            {
                var firstCfg = BacktestRunner.OptimizedConfig();
                firstCfg.UseNotionalGivebackCap = true;
                firstCfg.GivebackPctOfNotional = 0.01;
                firstCfg.GivebackUsdCap = 30.0;

                Console.WriteLine("Profile: FIRST (giveback=min(1% of notional, $30))");
                var firstStrategy = new ConductStrategyV2(firstCfg);
                var results = BacktestRunner.RunAll(backtestSymbols, firstStrategy, firstCfg);
                BacktestRunner.PrintVerdict(results);
            }
            else
            {
                Console.WriteLine("Profile: CONDUCT (optimized)");
                var results = BacktestRunner.RunV2(backtestSymbols);
                BacktestRunner.PrintVerdict(results);
            }
            break;

        case RunMode.BacktestSweep:
            Console.WriteLine("=== Quick Parameter Sweep (15 configs) ===");
            var sweepData = ParameterSweep.LoadAllData(backtestSymbols);
            var sweepResults = ParameterSweep.RunQuickSweep(sweepData);
            ParameterSweep.PrintRanked(sweepResults);
            break;

        case RunMode.BacktestOptimize:
            Console.WriteLine("=== Full Parameter Optimization ===");
            var optData = ParameterSweep.LoadAllData(backtestSymbols);
            var optResults = ParameterSweep.RunFullOptimize(optData);
            ParameterSweep.PrintRanked(optResults, topN: 20);
            if (optResults.Count > 0)
                ParameterSweep.PrintBestConfig(optResults[0]);
            break;

        case RunMode.BacktestScan:
            QuickScanner.ScanCached(backtestSymbols);
            break;

        case RunMode.BacktestLiveSim:
            var bot = new LivePaperBot();
            bot.SimulateFromCached(backtestSymbols);
            break;

        case RunMode.BacktestCompare:
            StrategyComparisonRunner.RunAll(backtestSymbols, minTrades: 50);
            break;
    }

    Environment.Exit(0);
}

// ── Standard IBKR modes ─────────────────────────────────────────────────────
IStrategyRuntime? strategyRuntime = null;

if (options.Mode == RunMode.StrategyLiveV3)
{
    strategyRuntime = new V3LiveRuntime();
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

var runtime = new SnapshotRuntime(options, strategyRuntime);
var exitCode = await runtime.RunAsync();
Environment.Exit(exitCode);
