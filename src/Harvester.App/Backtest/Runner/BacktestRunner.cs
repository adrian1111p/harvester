// BacktestRunner.cs — Main backtest runner: load cached CSV data → run strategy → print results.
// Port of backtest/run_backtest.py

using Harvester.App.Backtest.DataFetcher;
using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Backtest.Runner;

/// <summary>
/// Main backtest runner. Loads cached CSV data for multiple symbols,
/// runs the selected strategy, and prints per-symbol + aggregate results.
/// </summary>
public static class BacktestRunner
{
    public static readonly string[] DefaultSymbols = ["AAPL", "TSLA", "NVDA", "AMD", "META"];
    public const string TriggerTf = "1m";
    public static readonly string[] ContextTfs = ["5m", "15m", "1h", "1D"];

    /// <summary>
    /// Returns the optimized V2 config (best from parameter sweep: Sharpe=1.57, PF=1.36).
    /// </summary>
    public static StrategyConfig OptimizedConfig() => new()
    {
        TrailR = 1.5,
        GivebackPct = 0.70,
        Tp1R = 2.0,
        Tp2R = 4.0,
        HardStopR = 1.5,
        BreakevenR = 1.2,
        RvolMin = 1.3,
        AdxThreshold = 20.0,
        RiskPerTradeDollars = 50.0,
        AccountSize = 25_000.0,
    };

    /// <summary>
    /// Run backtest for all symbols and return results along with console output.
    /// </summary>
    public static List<BacktestResult> RunAll(
        string[] symbols,
        IBacktestStrategy strategy,
        StrategyConfig? config = null,
        Action<string>? log = null)
    {
        var cfg = config ?? OptimizedConfig();
        log ??= Console.WriteLine;
        var results = new List<BacktestResult>();

        foreach (var sym in symbols)
        {
            log($"\n{"",0}{"=",-60}");
            log($"  BACKTEST: {sym}  (trigger={TriggerTf})");
            log($"{"=",-60}");

            // Load trigger timeframe
            BacktestBar[]? triggerBars;
            if (!CsvBarStorage.Exists(sym, TriggerTf))
            {
                log($"  [SKIP] No {TriggerTf} data for {sym}");
                continue;
            }
            triggerBars = CsvBarStorage.LoadBars(sym, TriggerTf);
            if (triggerBars.Length == 0)
            {
                log($"  [SKIP] Empty {TriggerTf} data for {sym}");
                continue;
            }

            // Enrich trigger bars
            var enrichedTrigger = Indicators.TechnicalIndicators.EnrichWithIndicators(triggerBars);

            // Load and enrich context timeframes
            EnrichedBar[]? ctx5m = null, ctx15m = null, ctx1h = null, ctx1d = null;
            foreach (var tf in ContextTfs)
            {
                if (!CsvBarStorage.Exists(sym, tf)) continue;
                var bars = CsvBarStorage.LoadBars(sym, tf);
                if (bars.Length == 0) continue;
                var enriched = Indicators.TechnicalIndicators.EnrichWithIndicators(bars);
                switch (tf)
                {
                    case "5m": ctx5m = enriched; break;
                    case "15m": ctx15m = enriched; break;
                    case "1h": ctx1h = enriched; break;
                    case "1D": ctx1d = enriched; break;
                }
            }

            // Run backtest
            var result = BacktestEngine.RunBacktest(
                symbol: sym,
                strategy: strategy,
                triggerBars: enrichedTrigger,
                triggerTf: TriggerTf,
                bars5m: ctx5m,
                bars15m: ctx15m,
                bars1h: ctx1h,
                bars1d: ctx1d,
                initialCapital: cfg.AccountSize);

            results.Add(result);

            // Print per-symbol report
            log($"\n{result.SummaryTable()}");
            log("\nLast 15 trades:");
            log(result.TradesTable(15));
        }

        // Aggregate summary
        if (results.Count > 1)
        {
            PrintAggregate(results, log);
        }

        return results;
    }

    /// <summary>
    /// Run backtest using V2 Conduct strategy with default optimized config.
    /// </summary>
    public static List<BacktestResult> RunV2(string[]? symbols = null, Action<string>? log = null)
    {
        var syms = symbols ?? DefaultSymbols;
        var cfg = OptimizedConfig();
        var strategy = new ConductStrategyV2(cfg);
        return RunAll(syms, strategy, cfg, log);
    }

    private static void PrintAggregate(List<BacktestResult> results, Action<string> log)
    {
        log($"\n{"=",-60}");
        log($"  AGGREGATE SUMMARY  ({results.Count} symbols)");
        log($"{"=",-60}");

        var totalTrades = results.Sum(r => r.Stats.TotalTrades);
        var totalWinners = results.Sum(r => r.Stats.Winners);
        var totalPnl = results.Sum(r => r.Stats.TotalPnl);
        var allPnlR = results.SelectMany(r => r.Trades.Select(t => t.PnlR)).ToArray();
        var maxDD = results.Max(r => r.Stats.MaxDrawdown);

        log($"  {"Symbols",-20} {string.Join(", ", results.Select(r => r.Symbol))}");
        log($"  {"Total Trades",-20} {totalTrades}");
        log($"  {"Total Winners",-20} {totalWinners}");
        log($"  {"Overall Win Rate",-20} {(totalTrades > 0 ? $"{100.0 * totalWinners / totalTrades:F1}%" : "N/A")}");
        log($"  {"Total PnL ($)",-20} ${totalPnl:F2}");
        log($"  {"Avg Expectancy (R)",-20} {(allPnlR.Length > 0 ? $"{allPnlR.Average():F2}R" : "N/A")}");
        log($"  {"Max Single DD ($)",-20} ${maxDD:F2}");

        // Per-symbol one-liner
        foreach (var r in results)
        {
            var s = r.Stats;
            log($"    {r.Symbol,-6} {s.TotalTrades} trades | ${s.TotalPnl:F2} | " +
                $"WR {s.WinRate:P0} | PF {s.ProfitFactor:F2} | Sharpe {s.Sharpe:F2}");
        }
    }

    /// <summary>
    /// Print a final verdict line.
    /// </summary>
    public static void PrintVerdict(List<BacktestResult> results, Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        var totalPnl = results.Sum(r => r.Stats.TotalPnl);
        var totalTrades = results.Sum(r => r.Stats.TotalTrades);
        log($"\n{"=",-60}");
        var verdict = totalPnl > 0 ? "PROFITABLE" : "UNPROFITABLE";
        log($"  VERDICT: {verdict} — ${totalPnl:F2} over {totalTrades} trades");
        log($"{"=",-60}");
    }
}
