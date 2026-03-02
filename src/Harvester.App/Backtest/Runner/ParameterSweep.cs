// ParameterSweep.cs — Parameter sweep and optimization engine.
// Port of backtest/quick_sweep.py + backtest/optimize.py

using Harvester.App.Backtest.DataFetcher;
using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Backtest.Runner;

/// <summary>
/// Result of a single parameter configuration sweep.
/// </summary>
public sealed record SweepResult(
    int Index,
    StrategyConfig Config,
    BacktestStatistics Stats,
    Dictionary<string, double> PerSymbolPnl);

/// <summary>
/// Parameter sweep engine: tests multiple strategy configurations
/// across all symbols and ranks by Sharpe ratio.
/// </summary>
public static class ParameterSweep
{
    public static readonly string[] DefaultSymbols = ["AAPL", "TSLA", "NVDA", "AMD", "META"];

    /// <summary>
    /// Load cached data for all symbols. Returns dict of symbol → (triggerBars, contextBars).
    /// </summary>
    public static Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)>
        LoadAllData(string[]? symbols = null, Action<string>? log = null)
    {
        var syms = symbols ?? DefaultSymbols;
        log ??= Console.WriteLine;
        var data = new Dictionary<string, (EnrichedBar[], EnrichedBar[]?, EnrichedBar[]?, EnrichedBar[]?, EnrichedBar[]?)>();

        foreach (var sym in syms)
        {
            if (!CsvBarStorage.Exists(sym, "1m"))
            {
                log($"  [SKIP] {sym} — no data");
                continue;
            }

            var trigger = Indicators.TechnicalIndicators.EnrichWithIndicators(
                CsvBarStorage.LoadBars(sym, "1m"));

            EnrichedBar[]? ctx5m = null, ctx15m = null, ctx1h = null, ctx1d = null;
            foreach (var tf in new[] { "5m", "15m", "1h", "1D" })
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

            data[sym] = (trigger, ctx5m, ctx15m, ctx1h, ctx1d);
        }

        log($"DATA LOADED ({data.Count} symbols)");
        return data;
    }

    /// <summary>
    /// Quick sweep: 15 hand-picked parameter configurations (port of quick_sweep.py).
    /// </summary>
    public static List<SweepResult> RunQuickSweep(
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        Action<string>? log = null)
    {
        log ??= Console.WriteLine;

        var configs = new List<StrategyConfig>
        {
            new() { TrailR=0.5, GivebackPct=0.50, Tp1R=1.5, Tp2R=3.0, HardStopR=1.0, BreakevenR=0.8 },
            new() { TrailR=1.0, GivebackPct=0.50, Tp1R=1.5, Tp2R=3.0, HardStopR=1.0, BreakevenR=0.8 },
            new() { TrailR=1.5, GivebackPct=0.50, Tp1R=1.5, Tp2R=3.0, HardStopR=1.0, BreakevenR=0.8 },
            new() { TrailR=2.0, GivebackPct=0.50, Tp1R=1.5, Tp2R=3.0, HardStopR=1.0, BreakevenR=0.8 },
            new() { TrailR=1.0, GivebackPct=0.65, Tp1R=2.0, Tp2R=4.0, HardStopR=1.0, BreakevenR=0.8 },
            new() { TrailR=1.0, GivebackPct=0.80, Tp1R=2.0, Tp2R=4.0, HardStopR=1.0, BreakevenR=0.8 },
            new() { TrailR=1.0, GivebackPct=0.65, Tp1R=2.0, Tp2R=4.0, HardStopR=1.5, BreakevenR=1.2 },
            new() { TrailR=1.5, GivebackPct=0.70, Tp1R=2.0, Tp2R=4.0, HardStopR=1.5, BreakevenR=1.2 },
            new() { TrailR=1.5, GivebackPct=0.70, Tp1R=2.5, Tp2R=5.0, HardStopR=1.5, BreakevenR=1.2 },
            new() { TrailR=2.0, GivebackPct=0.70, Tp1R=2.0, Tp2R=4.0, HardStopR=2.0, BreakevenR=1.5 },
            new() { TrailR=2.0, GivebackPct=0.80, Tp1R=3.0, Tp2R=5.0, HardStopR=2.0, BreakevenR=1.5 },
            new() { TrailR=1.5, GivebackPct=0.70, Tp1R=2.0, Tp2R=4.0, HardStopR=1.5, BreakevenR=1.2, RvolMin=0.8 },
            new() { TrailR=1.5, GivebackPct=0.70, Tp1R=2.0, Tp2R=4.0, HardStopR=1.5, BreakevenR=1.2, AdxThreshold=15.0 },
            new() { TrailR=1.5, GivebackPct=0.70, Tp1R=2.0, Tp2R=4.0, HardStopR=1.5, BreakevenR=1.2, RvolMin=0.8, AdxThreshold=15.0 },
            new() { TrailR=2.0, GivebackPct=0.70, Tp1R=2.5, Tp2R=5.0, HardStopR=2.0, BreakevenR=1.5, RvolMin=0.8, AdxThreshold=15.0 },
        };

        return RunSweep(allData, configs, log);
    }

    /// <summary>
    /// Full combinatorial parameter sweep (port of optimize.py).
    /// </summary>
    public static List<SweepResult> RunFullOptimize(
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        Action<string>? log = null)
    {
        log ??= Console.WriteLine;

        var trailValues = new[] { 0.5, 0.75, 1.0, 1.5 };
        var givebackValues = new[] { 0.40, 0.50, 0.60, 0.70 };
        var tp1Values = new[] { 1.5, 2.0, 2.5 };
        var hardStopValues = new[] { 1.0, 1.5 };
        var rvolValues = new[] { 0.8, 1.0, 1.3 };
        var adxValues = new[] { 15.0, 20.0, 25.0 };

        var seen = new HashSet<string>();
        var configs = new List<StrategyConfig>();

        // Add baseline
        configs.Add(new StrategyConfig());
        seen.Add(ConfigKey(configs[0]));

        // Systematic sweep of key parameters
        foreach (var trail in trailValues)
        foreach (var giveback in givebackValues)
        foreach (var tp1 in tp1Values)
        foreach (var hardStop in hardStopValues)
        {
            var cfg = new StrategyConfig
            {
                TrailR = trail,
                GivebackPct = giveback,
                Tp1R = tp1,
                Tp2R = Math.Max(tp1 + 1.0, 3.0),
                HardStopR = hardStop,
                BreakevenR = hardStop * 0.8,
                RvolMin = 1.0,
                AdxThreshold = 20.0,
            };
            var key = ConfigKey(cfg);
            if (seen.Add(key))
                configs.Add(cfg);
        }

        // Additional configs with different RVOL / ADX
        foreach (var rvol in rvolValues)
        foreach (var adx in adxValues)
        {
            var cfg = new StrategyConfig
            {
                TrailR = 1.0,
                GivebackPct = 0.60,
                Tp1R = 2.0,
                Tp2R = 4.0,
                HardStopR = 1.5,
                BreakevenR = 1.0,
                RvolMin = rvol,
                AdxThreshold = adx,
            };
            var key = ConfigKey(cfg);
            if (seen.Add(key))
                configs.Add(cfg);
        }

        log($"Testing {configs.Count} parameter combinations...\n");
        return RunSweep(allData, configs, log);
    }

    /// <summary>
    /// Run sweep over a list of configs and return ranked results.
    /// </summary>
    public static List<SweepResult> RunSweep(
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        List<StrategyConfig> configs,
        Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        var results = new List<SweepResult>();

        for (int i = 0; i < configs.Count; i++)
        {
            var cfg = configs[i];
            var strategy = new ConductStrategyV2(cfg);
            var allTrades = new List<BacktestTradeResult>();
            var symPnls = new Dictionary<string, double>();

            foreach (var (sym, (trigger, ctx5m, ctx15m, ctx1h, ctx1d)) in allData)
            {
                var bt = BacktestEngine.RunBacktest(sym, strategy, trigger, "1m", ctx5m, ctx15m, ctx1h, ctx1d, cfg.AccountSize);
                allTrades.AddRange(bt.Trades);
                symPnls[sym] = bt.Stats.TotalPnl;
            }

            var stats = BacktestEngine.ComputeStatistics(allTrades, cfg.AccountSize);
            results.Add(new SweepResult(i, cfg, stats, symPnls));

            log($"{i,2} T={cfg.TrailR:F1} G={cfg.GivebackPct:P0} TP1={cfg.Tp1R:F1} S={cfg.HardStopR:F1} | " +
                $"{stats.TotalTrades,3}tr WR={stats.WinRate:P0} PF={stats.ProfitFactor:F2} " +
                $"E={stats.ExpectancyR:F2}R PnL=${stats.TotalPnl:F0} DD=${stats.MaxDrawdown:F0} Sh={stats.Sharpe:F2}");
        }

        // Sort by Sharpe (desc), then PnL (desc)
        results.Sort((a, b) =>
        {
            int c = b.Stats.Sharpe.CompareTo(a.Stats.Sharpe);
            return c != 0 ? c : b.Stats.TotalPnl.CompareTo(a.Stats.TotalPnl);
        });

        return results;
    }

    /// <summary>
    /// Print ranked results to console.
    /// </summary>
    public static void PrintRanked(List<SweepResult> results, int topN = 5, Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        log("\n=== RANKED BY SHARPE ===");

        for (int rank = 0; rank < Math.Min(topN, results.Count); rank++)
        {
            var r = results[rank];
            var cfg = r.Config;
            var s = r.Stats;
            log($"#{rank + 1} [cfg{r.Index}] T={cfg.TrailR} G={cfg.GivebackPct:P0} TP1={cfg.Tp1R} S={cfg.HardStopR} | " +
                $"{s.TotalTrades}tr WR={s.WinRate:P0} PF={s.ProfitFactor:F2} PnL=${s.TotalPnl:F0} Sharpe={s.Sharpe:F2}");

            foreach (var (sym, pnl) in r.PerSymbolPnl)
            {
                log($"     {sym}: ${pnl:F2}");
            }
        }
    }

    /// <summary>
    /// Print detailed info about the best config.
    /// </summary>
    public static void PrintBestConfig(SweepResult best, Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        log($"\n{"=",-60}");
        log($"  BEST CONFIG #{best.Index}");
        log($"{"=",-60}");
        log($"  Trail R:       {best.Config.TrailR}");
        log($"  Giveback:      {best.Config.GivebackPct:P0}");
        log($"  TP1:           {best.Config.Tp1R}R");
        log($"  TP2:           {best.Config.Tp2R}R");
        log($"  Hard Stop:     {best.Config.HardStopR}R");
        log($"  BE at:         {best.Config.BreakevenR}R");
        log($"  RVOL min:      {best.Config.RvolMin}");
        log($"  ADX min:       {best.Config.AdxThreshold}");
        log($"  Total PnL:     ${best.Stats.TotalPnl:F2}");
        log($"  Sharpe:        {best.Stats.Sharpe:F2}");
        log($"  Win Rate:      {best.Stats.WinRate:P1}");
        log($"  Profit Factor: {best.Stats.ProfitFactor:F2}");
        log($"  Max DD:        ${best.Stats.MaxDrawdown:F2}");

        log("\n  Per-symbol PnL:");
        foreach (var (sym, pnl) in best.PerSymbolPnl)
        {
            var tag = pnl > 0 ? "+" : "";
            log($"    {sym}: {tag}${pnl:F2}");
        }
    }

    /// <summary>
    /// Save full sweep results to a text file.
    /// </summary>
    public static void SaveResults(List<SweepResult> results, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("RANKED BY SHARPE:");
        for (int rank = 0; rank < results.Count; rank++)
        {
            var r = results[rank];
            var cfg = r.Config;
            var s = r.Stats;
            writer.WriteLine($"#{rank + 1} T={cfg.TrailR} G={cfg.GivebackPct:P0} TP1={cfg.Tp1R} S={cfg.HardStopR} | " +
                $"{s.TotalTrades}tr WR={s.WinRate:P0} PF={s.ProfitFactor:F2} " +
                $"E={s.ExpectancyR:F2}R PnL=${s.TotalPnl:F0} DD=${s.MaxDrawdown:F0} Sharpe={s.Sharpe:F2}");
            foreach (var (sym, pnl) in r.PerSymbolPnl)
            {
                writer.WriteLine($"  {sym}: ${pnl:F2}");
            }
            writer.WriteLine();
        }
    }

    private static string ConfigKey(StrategyConfig cfg) =>
        $"{cfg.TrailR}|{cfg.GivebackPct}|{cfg.Tp1R}|{cfg.Tp2R}|{cfg.HardStopR}|{cfg.BreakevenR}|{cfg.RvolMin}|{cfg.AdxThreshold}";
}
