// QuickScanner.cs — One-shot signal scanner across all strategies and symbols.
// Port of backtest/quick_scanner.py
//
// In backtest/offline mode: scans cached CSV data for the most recent signals.
// The IBKR live integration reuses existing SnapshotRuntime infrastructure.

using Harvester.App.Backtest.DataFetcher;
using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Backtest.Runner;

/// <summary>
/// A detected signal from the scanner.
/// </summary>
public sealed record ScannerSignal(
    string Symbol,
    string Strategy,
    TradeSide Side,
    double EntryPrice,
    double StopPrice,
    double RiskPerShare,
    double Atr,
    int Freshness,       // bars ago from last bar
    int Score,           // enhanced score (0 for strategies without scoring)
    string SubStrategy); // pattern/entry type info

/// <summary>
/// One-shot signal scanner: checks ALL strategies on ALL symbols.
/// Picks the best available signal based on freshness and score.
/// </summary>
public static class QuickScanner
{
    /// <summary>
    /// 20MA Exhaustion filter: reject signals in overextended zones.
    /// LONG rejected when price > threshold ATR above 20MA.
    /// SHORT rejected when price > threshold ATR below 20MA.
    /// </summary>
    public const double MaExhaustionMaxAtr = 0.5;

    /// <summary>
    /// How many recent bars to consider for signals (20 bars ≈ 20 minutes on 1m).
    /// </summary>
    public const int RecencyWindow = 20;

    /// <summary>
    /// Named strategy configurations matching the Python scanner presets.
    /// </summary>
    public static readonly (string Name, Func<IBacktestStrategy> Factory)[] Strategies =
    [
        ("Trend-V1.3", () => new ConductStrategyV2(new StrategyConfig
        {
            RiskPerTradeDollars = 50.0,
            TrailR = 1.5, GivebackPct = 0.70, Tp1R = 2.0, Tp2R = 4.0,
            HardStopR = 1.5, BreakevenR = 1.2, RvolMin = 1.3, AdxThreshold = 20.0,
        })),
        ("V3-Balanced", () => new StrategyV3(new V3Config
        {
            RiskPerTradeDollars = 50.0,
            MinPrice = 8.0, MaxPrice = 500.0,
            HardStopR = 2.0, TrailR = 1.5, Tp1R = 1.5, Tp2R = 3.0, BreakevenR = 1.0,
        })),
        ("V4-Exh-Runner", () => new StrategyV4(new V4Config
        {
            RiskPerTradeDollars = 50.0, EnhancedMinScore = 2,
            EnableBuySetup = false, EnableSellSetup = false,
            Enable123Pattern = false, EnableBreakout = false, EnableBreakdown = false,
            ExhaustionLookback = 15, ExhaustionMinMoveAtr = 3.0,
            ExhaustionReversalBars = 3,
            HardStopR = 2.5, TrailR = 2.0, Tp1R = 2.0, Tp2R = 5.0,
            BreakevenR = 1.5, GivebackPct = 0.80, MaxHoldBars = 180,
        })),
        ("V4-Exh-Base", () => new StrategyV4(new V4Config
        {
            RiskPerTradeDollars = 50.0, EnhancedMinScore = 2,
            EnableBuySetup = false, EnableSellSetup = false,
            Enable123Pattern = false, EnableBreakout = false, EnableBreakdown = false,
            ExhaustionLookback = 15, ExhaustionMinMoveAtr = 3.0,
            ExhaustionReversalBars = 3,
            HardStopR = 2.0, TrailR = 1.5, Tp1R = 1.5, Tp2R = 3.0,
            BreakevenR = 1.0, GivebackPct = 0.70, MaxHoldBars = 120,
        })),
        ("V4-Exh-Loose", () => new StrategyV4(new V4Config
        {
            RiskPerTradeDollars = 50.0, EnhancedMinScore = 1,
            EnableBuySetup = false, EnableSellSetup = false,
            Enable123Pattern = false, EnableBreakout = false, EnableBreakdown = false,
            ExhaustionLookback = 10, ExhaustionMinMoveAtr = 2.5,
            ExhaustionReversalBars = 2,
            HardStopR = 2.0, TrailR = 1.5, Tp1R = 1.5, Tp2R = 3.0,
            BreakevenR = 1.0, GivebackPct = 0.70, MaxHoldBars = 120,
        })),
        ("V5-PullbackVWAP", () => new StrategyV5(new V5Config
        {
            RiskPerTradeDollars = 50.0,
            ExhaustionFadeEnabled = false,
        })),
        ("V5-Tight", () => new StrategyV5(new V5Config
        {
            RiskPerTradeDollars = 50.0,
            MaxMaDistAtr = 0.3,
            MicroTrailCents = 2.0, MicroTrailActivateCents = 3.0,
            HardStopR = 1.0, BreakevenR = 0.3, GivebackPct = 0.40,
            Tp1R = 0.8, Tp2R = 1.5, MaxHoldBars = 30,
        })),
    ];

    /// <summary>
    /// Scan cached data for all symbols and return ranked signals.
    /// </summary>
    public static List<ScannerSignal> ScanCached(
        string[]? symbols = null,
        Action<string>? log = null)
    {
        var syms = symbols ?? BacktestRunner.DefaultSymbols;
        log ??= Console.WriteLine;
        var allSignals = new List<ScannerSignal>();

        log(new string('=', 60));
        log("  QUICK SCANNER — Looking for signals in cached data");
        log(new string('=', 60));

        foreach (var symbol in syms)
        {
            log($"\n--- Scanning {symbol} ---");

            if (!CsvBarStorage.Exists(symbol, "1m"))
            {
                log($"  No 1m data for {symbol}");
                continue;
            }

            var bars1m = CsvBarStorage.LoadBars(symbol, "1m");
            if (bars1m.Length < 60)
            {
                log($"  Not enough data ({bars1m.Length} bars)");
                continue;
            }

            var enriched1m = TechnicalIndicators.EnrichWithIndicators(bars1m);
            var lastClose = bars1m[^1].Close;
            log($"  Last price: ${lastClose:F2} | Bars: {bars1m.Length}");

            // 20MA exhaustion check (global filter)
            var lastBar = enriched1m[^1];
            var sma20 = lastBar.Sma20;
            var atr14 = lastBar.Atr14;
            double maDistAtr = double.NaN;

            if (!double.IsNaN(sma20) && !double.IsNaN(atr14) && atr14 > 0 && sma20 > 0)
            {
                maDistAtr = (lastClose - sma20) / atr14;
                log($"  20MA=${sma20:F2}  dist={maDistAtr:+0.00;-0.00} ATR");
            }
            else
            {
                log("  20MA/ATR unavailable — skipping exhaustion filter");
            }

            // Load context timeframes
            EnrichedBar[]? ctx5m = null, ctx15m = null, ctx1h = null, ctx1d = null;
            foreach (var tf in new[] { "5m", "15m", "1h", "1D" })
            {
                if (!CsvBarStorage.Exists(symbol, tf)) continue;
                var ctxBars = CsvBarStorage.LoadBars(symbol, tf);
                if (ctxBars.Length == 0) continue;
                var ctxEnriched = TechnicalIndicators.EnrichWithIndicators(ctxBars);
                switch (tf)
                {
                    case "5m": ctx5m = ctxEnriched; break;
                    case "15m": ctx15m = ctxEnriched; break;
                    case "1h": ctx1h = ctxEnriched; break;
                    case "1D": ctx1d = ctxEnriched; break;
                }
            }

            foreach (var (stratName, factory) in Strategies)
            {
                try
                {
                    var strategy = factory();
                    var signals = strategy.GenerateSignals(enriched1m, ctx5m, ctx15m, ctx1h, ctx1d);

                    if (signals.Count == 0)
                    {
                        log($"  {stratName}: No signals");
                        continue;
                    }

                    // Check recent signals (last N bars)
                    var recent = signals.Where(s => s.BarIndex >= enriched1m.Length - RecencyWindow).ToList();
                    if (recent.Count == 0)
                    {
                        var last = signals[^1];
                        var barsAgo = enriched1m.Length - 1 - last.BarIndex;
                        log($"  {stratName}: {signals.Count} signals total, last was {barsAgo} bars ago");
                        continue;
                    }

                    var sig = recent[^1]; // most recent
                    var freshness = enriched1m.Length - 1 - sig.BarIndex;

                    // 20MA exhaustion gate
                    if (!double.IsNaN(maDistAtr))
                    {
                        if (sig.Side == TradeSide.Long && maDistAtr > MaExhaustionMaxAtr)
                        {
                            log($"  !!! REJECTED {stratName} LONG {symbol}: " +
                                $"price {maDistAtr:+0.0;-0.0} ATR above 20MA (exhaustion zone)");
                            continue;
                        }
                        if (sig.Side == TradeSide.Short && maDistAtr < -MaExhaustionMaxAtr)
                        {
                            log($"  !!! REJECTED {stratName} SHORT {symbol}: " +
                                $"price {maDistAtr:+0.0;-0.0} ATR below 20MA (exhaustion zone)");
                            continue;
                        }
                    }

                    var subInfo = sig.SubStrategy ?? "";
                    log($"  >>> SIGNAL: {stratName} {sig.Side} @ ${sig.EntryPrice:F2}" +
                        $" Stop=${sig.StopPrice:F2} Risk=${sig.RiskPerShare:F2}" +
                        $" (bars ago: {freshness})" +
                        (string.IsNullOrEmpty(subInfo) ? "" : $" [{subInfo}]"));

                    allSignals.Add(new ScannerSignal(
                        Symbol: symbol,
                        Strategy: stratName,
                        Side: sig.Side,
                        EntryPrice: sig.EntryPrice,
                        StopPrice: sig.StopPrice,
                        RiskPerShare: sig.RiskPerShare,
                        Atr: sig.AtrValue,
                        Freshness: freshness,
                        Score: 0, // score tracking can be added per strategy
                        SubStrategy: subInfo));
                }
                catch (Exception ex)
                {
                    log($"  {stratName}: ERROR - {ex.Message}");
                }
            }
        }

        // Results
        log($"\n{new string('=', 60)}");
        if (allSignals.Count == 0)
        {
            log("  NO RECENT SIGNALS FOUND across any strategy/symbol.");
            log("  Strategies are selective by design (high win-rate = fewer but better entries).");
        }
        else
        {
            // Sort by freshness (ascending = most recent first), then by score descending
            allSignals.Sort((a, b) =>
            {
                int c = a.Freshness.CompareTo(b.Freshness);
                return c != 0 ? c : b.Score.CompareTo(a.Score);
            });

            log($"  FOUND {allSignals.Count} SIGNAL(S)!");
            var best = allSignals[0];
            log($"\n  BEST: {best.Symbol} {best.Strategy} {best.Side}");
            log($"    Entry: ${best.EntryPrice:F2}");
            log($"    Stop:  ${best.StopPrice:F2}");
            log($"    Risk:  ${best.RiskPerShare:F2}/share");
            log($"    Freshness: {best.Freshness} bars ago");
        }
        log(new string('=', 60));

        return allSignals;
    }
}
