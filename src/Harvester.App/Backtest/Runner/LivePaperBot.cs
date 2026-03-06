// LivePaperBot.cs — Live Paper Trading Bot V2.
// Port of backtest/live_paper.py
//
// Runs a persistent polling loop during market hours:
//   - Connects to IBKR TWS paper trading (port 7497)
//   - Assigns best strategy per symbol (from sweep optimization)
//   - Manages entries and exits with full exit cascade
//   - Flattens all positions at EOD (15:55 ET)
//
// IMPORTANT: This class is designed to work with the existing SnapshotRuntime
// IBKR infrastructure. For live execution, it needs an active EClientSocket.
// This implementation provides the complete trading logic; the IBKR connection
// layer is handled by the existing broker adapter.

using Harvester.App.Backtest.DataFetcher;
using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;
using Harvester.App.Backtest.Strategies;
using Harvester.App.Strategy;

namespace Harvester.App.Backtest.Runner;

/// <summary>
/// Strategy assignment type for per-symbol strategy routing.
/// </summary>
public enum LiveStrategyType
{
    TrendV13,
    V3Balanced,
    V4ExhRunner,
    V4ExhBase,
    V5PullbackVwap,
    V5Tight,
}

/// <summary>
/// Tracks a live open position with all state needed for exit management.
/// </summary>
public sealed class LivePosition
{
    public required string Symbol { get; init; }
    public required TradeSide Side { get; init; }
    public required double EntryPrice { get; init; }
    public double StopPrice { get; set; }
    public required int Shares { get; init; }
    public required DateTime EntryTimeEt { get; init; }
    public required string Strategy { get; init; }
    public required double AtrAtEntry { get; init; }
    public required double RiskPerShare { get; init; }
    public bool BreakevenActivated { get; set; }
    public double PeakPrice { get; set; }
    public double TrailingStop { get; set; }
    public bool Tp1Hit { get; set; }
}

/// <summary>
/// Entry in the daily trade log.
/// </summary>
public sealed record TradeLogEntry
{
    public required string Time { get; init; }
    public required string Symbol { get; init; }
    public required TradeSide Side { get; init; }
    public required double Entry { get; init; }
    public required double Stop { get; init; }
    public required string Strategy { get; init; }
    public string Status { get; set; } = "OPEN";
    public double ExitPrice { get; set; }
    public double Pnl { get; set; }
    public double PnlR { get; set; }
    public string ExitReason { get; set; } = "";
}

/// <summary>
/// Live paper trading bot — manages position lifecycle with IBKR TWS.
/// This class implements the complete trading logic (signal detection,
/// entry/exit management, risk controls) as a portable engine that can
/// be driven by the existing SnapshotRuntime infrastructure.
/// </summary>
public sealed class LivePaperBot
{
    // ── Constants ────────────────────────────────────────────────────────
    public const int PaperPort = 7497;
    public const int ClientId = 90;
    public const int PositionSize = 2;
    public const int MaxDailyTrades = 10;
    public const int EodFlattenMinute = 955; // 15:55 ET
    public const int CheckIntervalSec = 60;

    // ── Per-symbol strategy assignment (from V5 sweep optimization) ──
    public static readonly Dictionary<string, LiveStrategyType> SymbolStrategy = new()
    {
        ["AAPL"] = LiveStrategyType.V5PullbackVwap,  // 24tr, 75% WR, +$210
        ["TSLA"] = LiveStrategyType.V5Tight,          // 55tr, 55% WR, +$1832
        ["NVDA"] = LiveStrategyType.TrendV13,         // 18tr, 39% WR, +$55
        ["AMD"]  = LiveStrategyType.TrendV13,         // 17tr, 82% WR, +$401
        ["META"] = LiveStrategyType.V5Tight,          // 54tr, 54% WR, +$276
    };

    // ── Strategy configs ────────────────────────────────────────────────
    private static readonly StrategyConfig CfgTrend = new()
    {
        RiskPerTradeDollars = 50.0,
        TrailR = 1.5, GivebackPct = 0.70, Tp1R = 2.0, Tp2R = 4.0,
        HardStopR = 1.5, BreakevenR = 1.2, RvolMin = 1.3, AdxThreshold = 20.0,
    };

    private static readonly V3Config CfgV3 = new()
    {
        RiskPerTradeDollars = 50.0,
        MinPrice = 8.0, MaxPrice = 500.0,
        HardStopR = 2.0, TrailR = 1.5, Tp1R = 1.5, Tp2R = 3.0, BreakevenR = 1.0,
    };

    private static readonly V4Config CfgV4ExhRunner = new()
    {
        RiskPerTradeDollars = 50.0, EnhancedMinScore = 2,
        EnableBuySetup = false, EnableSellSetup = false,
        Enable123Pattern = false, EnableBreakout = false, EnableBreakdown = false,
        ExhaustionLookback = 15, ExhaustionMinMoveAtr = 3.0,
        ExhaustionReversalBars = 3,
        HardStopR = 2.5, TrailR = 2.0, Tp1R = 2.0, Tp2R = 5.0,
        BreakevenR = 1.5, GivebackPct = 0.80, MaxHoldBars = 180,
    };

    private static readonly V4Config CfgV4ExhBase = new()
    {
        RiskPerTradeDollars = 50.0, EnhancedMinScore = 2,
        EnableBuySetup = false, EnableSellSetup = false,
        Enable123Pattern = false, EnableBreakout = false, EnableBreakdown = false,
        ExhaustionLookback = 15, ExhaustionMinMoveAtr = 3.0,
        ExhaustionReversalBars = 3,
        HardStopR = 2.0, TrailR = 1.5, Tp1R = 1.5, Tp2R = 3.0,
        BreakevenR = 1.0, GivebackPct = 0.70, MaxHoldBars = 120,
    };

    private static readonly V5Config CfgV5PullbackVwap = new()
    {
        RiskPerTradeDollars = 50.0,
        ExhaustionFadeEnabled = false,
    };

    private static readonly V5Config CfgV5Tight = new()
    {
        RiskPerTradeDollars = 50.0,
        MaxMaDistAtr = 0.3,
        MicroTrailCents = 2.0, MicroTrailActivateCents = 3.0,
        HardStopR = 1.0, BreakevenR = 0.3, GivebackPct = 0.40,
        Tp1R = 0.8, Tp2R = 1.5, MaxHoldBars = 30,
    };

    // ── State ───────────────────────────────────────────────────────────
    public Dictionary<string, LivePosition> Positions { get; } = new();
    public int DailyTrades { get; private set; }
    public double DailyPnl { get; private set; }
    public List<TradeLogEntry> TradeLog { get; } = [];

    private readonly Action<string> _log;

    public LivePaperBot(Action<string>? log = null)
    {
        _log = log ?? Console.WriteLine;
    }

    /// <summary>
    /// Check if the assigned strategy generates a signal for a symbol using cached data.
    /// In live mode, data would come from IBKR reqHistoricalData.
    /// </summary>
    public BacktestSignal? CheckForSignal(string symbol, EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null, EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null, EnrichedBar[]? bars1d = null)
    {
        if (!SymbolStrategy.TryGetValue(symbol, out var stratType))
            return null;

        if (bars1m.Length < 60)
            return null;

        var strategy = CreateStrategy(stratType);
        var signals = strategy.GenerateSignals(bars1m, bars5m, bars15m, bars1h, bars1d);

        if (signals.Count == 0)
            return null;

        // Only use the most recent signal (within last 2 bars)
        var lastSig = signals[^1];
        if (lastSig.BarIndex < bars1m.Length - 2)
            return null;

        return lastSig;
    }

    /// <summary>
    /// Record a trade entry (would be paired with IBKR order execution in live mode).
    /// </summary>
    public void RecordEntry(string symbol, BacktestSignal signal, double fillPrice, DateTime entryTimeEt)
    {
        var side = signal.Side;
        Positions[symbol] = new LivePosition
        {
            Symbol = symbol,
            Side = side,
            EntryPrice = fillPrice,
            StopPrice = signal.StopPrice,
            Shares = PositionSize,
            EntryTimeEt = entryTimeEt,
            Strategy = GetStrategyName(symbol),
            AtrAtEntry = signal.AtrValue,
            RiskPerShare = signal.RiskPerShare,
            PeakPrice = fillPrice,
            TrailingStop = signal.StopPrice,
        };

        DailyTrades++;
        TradeLog.Add(new TradeLogEntry
        {
            Time = entryTimeEt.ToString("HH:mm:ss"),
            Symbol = symbol,
            Side = side,
            Entry = fillPrice,
            Stop = signal.StopPrice,
            Strategy = GetStrategyName(symbol),
        });

        _log($">>> ENTERING {side} {symbol} {PositionSize} shares @ ${fillPrice:F2} " +
             $"(stop: ${signal.StopPrice:F2}) [{GetStrategyName(symbol)}]");
    }

    /// <summary>
    /// Evaluate exit rules for an open position given the current price.
    /// Delegates to the shared <see cref="ExitCascadeEngine"/> for core R-based exits,
    /// then evaluates V5-specific reversal-flatten logic on top.
    /// Returns the exit reason if the position should be closed, null otherwise.
    /// </summary>
    public string? ManagePosition(string symbol, double currentPrice, DateTime nowEt,
        BacktestBar? lastBar = null, BacktestBar? prevBar = null)
    {
        if (!Positions.TryGetValue(symbol, out var pos))
            return null;

        if (!SymbolStrategy.TryGetValue(symbol, out var stratType))
            return null;

        var rps = pos.RiskPerShare;
        if (rps <= 0) return null;

        var isLong = pos.Side == TradeSide.Long;

        // Build shared cascade input
        var cascadeParams = GetCascadeParams(stratType);
        var holdSeconds = (nowEt - pos.EntryTimeEt).TotalSeconds;
        var unrealizedPnlPeak = isLong
            ? (pos.PeakPrice - pos.EntryPrice) * pos.Shares
            : (pos.EntryPrice - pos.PeakPrice) * pos.Shares;

        var input = new ExitCascadeInput
        {
            IsLong = isLong,
            EntryPrice = pos.EntryPrice,
            CurrentPrice = currentPrice,
            StopPrice = pos.StopPrice,
            AtrAtEntry = pos.AtrAtEntry,
            RiskPerShare = rps,
            PeakFavorablePrice = pos.PeakPrice,
            TrailingStopPrice = pos.TrailingStop,
            BreakevenActivated = pos.BreakevenActivated,
            Tp1Activated = pos.Tp1Hit,
            UnrealizedPnlPeak = Math.Max(0, unrealizedPnlPeak),
            Quantity = pos.Shares,
            HoldSeconds = holdSeconds,
            Params = cascadeParams,
        };

        var result = ExitCascadeEngine.Evaluate(input);

        // Apply state updates from the cascade engine
        pos.PeakPrice = result.UpdatedPeakPrice;
        pos.TrailingStop = result.UpdatedTrailingStop;
        pos.StopPrice = result.UpdatedStopPrice;
        pos.BreakevenActivated = result.UpdatedBreakevenActivated;
        pos.Tp1Hit = result.UpdatedTp1Activated;

        // Log position state
        double unrealizedR = isLong
            ? (currentPrice - pos.EntryPrice) / rps
            : (pos.EntryPrice - currentPrice) / rps;
        double peakR = isLong
            ? (pos.PeakPrice - pos.EntryPrice) / rps
            : (pos.EntryPrice - pos.PeakPrice) / rps;
        _log($"  [{symbol}] Price=${currentPrice:F2} UnR={unrealizedR:F2}R PeakR={peakR:F2}R");

        if (result.ShouldExit && !result.IsPartialExit)
        {
            _log($"    Exit: {result.ExitReason} — {result.Detail}");
            return result.ExitReason;
        }

        // ═══ V5 REVERSAL FLATTEN (backtest-specific, not in shared engine) ═══
        var v5Cfg = GetV5Config(stratType);
        if (v5Cfg is { ReversalFlatten: true } && unrealizedR > 0)
        {
            if (lastBar != null && prevBar != null)
            {
                var barRange = lastBar.High - lastBar.Low;
                if (barRange > 0)
                {
                    if (isLong)
                    {
                        var upperWick = (lastBar.High - Math.Max(lastBar.Open, lastBar.Close)) / barRange;
                        var isEngulfing = lastBar.Close < lastBar.Open && lastBar.Close < prevBar.Open;
                        if (isEngulfing || upperWick > 0.6)
                        {
                            _log("    Reversal candle detected! Flattening LONG.");
                            return "REVERSAL_FLATTEN";
                        }
                    }
                    else
                    {
                        var lowerWick = (Math.Min(lastBar.Open, lastBar.Close) - lastBar.Low) / barRange;
                        var isEngulfing = lastBar.Close > lastBar.Open && lastBar.Close > prevBar.Open;
                        if (isEngulfing || lowerWick > 0.6)
                        {
                            _log("    Reversal candle detected! Flattening SHORT.");
                            return "REVERSAL_FLATTEN";
                        }
                    }
                }
            }
        }

        if (result.IsPartialExit)
        {
            _log($"    Partial: {result.ExitReason} — {result.Detail}");
            return result.ExitReason;
        }

        return null;
    }

    /// <summary>
    /// Record a trade exit and update PnL.
    /// </summary>
    public double RecordExit(string symbol, double exitPrice, string reason)
    {
        if (!Positions.TryGetValue(symbol, out var pos))
            return 0;

        double pnl = pos.Side == TradeSide.Long
            ? (exitPrice - pos.EntryPrice) * pos.Shares
            : (pos.EntryPrice - exitPrice) * pos.Shares;

        double pnlR = pos.RiskPerShare > 0
            ? pnl / (pos.RiskPerShare * pos.Shares)
            : 0;

        DailyPnl += pnl;

        _log($"<<< EXITED {pos.Side} {symbol} @ ${exitPrice:F2} ({reason}) " +
             $"PnL=${pnl:F2} ({pnlR:F2}R) [{pos.Strategy}]");

        // Update trade log
        for (int i = TradeLog.Count - 1; i >= 0; i--)
        {
            if (TradeLog[i].Symbol == symbol && TradeLog[i].Status == "OPEN")
            {
                TradeLog[i].ExitPrice = exitPrice;
                TradeLog[i].Pnl = pnl;
                TradeLog[i].PnlR = pnlR;
                TradeLog[i].ExitReason = reason;
                TradeLog[i].Status = "CLOSED";
                break;
            }
        }

        Positions.Remove(symbol);
        return pnl;
    }

    /// <summary>
    /// Flatten all open positions.
    /// </summary>
    public void FlattenAll(Func<string, double> getCurrentPrice, string reason = "EOD")
    {
        foreach (var symbol in Positions.Keys.ToArray())
        {
            _log($"Flattening {symbol} ({reason})");
            var price = getCurrentPrice(symbol);
            RecordExit(symbol, price, reason);
        }
    }

    /// <summary>
    /// Print end-of-day summary.
    /// </summary>
    public void PrintSummary()
    {
        _log($"\n{new string('=', 60)}");
        _log("  END-OF-DAY SUMMARY");
        _log(new string('=', 60));
        _log($"  Total trades: {DailyTrades}");
        _log($"  Daily PnL: ${DailyPnl:F2}");

        if (TradeLog.Count > 0)
        {
            var closed = TradeLog.Where(t => t.Status == "CLOSED").ToList();
            var wins = closed.Count(t => t.Pnl > 0);
            if (closed.Count > 0)
            {
                _log($"  Win rate: {100.0 * wins / closed.Count:F0}% ({wins}/{closed.Count})");
            }

            foreach (var t in TradeLog)
            {
                var pnlStr = t.Status == "CLOSED" ? $"${t.Pnl:F2}" : "OPEN";
                _log($"    {t.Time} {t.Side} {t.Symbol} Entry=${t.Entry:F2} {pnlStr} [{t.Strategy}]");
            }
        }
        _log(new string('=', 60));
    }

    /// <summary>
    /// Simulate a full trading day using cached data (offline backtesting of the live bot logic).
    /// </summary>
    public void SimulateFromCached(string[]? symbols = null)
    {
        var syms = symbols ?? SymbolStrategy.Keys.ToArray();
        _log(new string('=', 60));
        _log("  LIVE PAPER BOT SIMULATION (cached data)");
        _log($"  Symbols: {string.Join(", ", syms)}");
        _log($"  Position size: {PositionSize} shares");
        _log("  Strategy assignments:");
        foreach (var (sym, stype) in SymbolStrategy)
        {
            if (syms.Contains(sym))
                _log($"    {sym} -> {stype}");
        }
        _log(new string('=', 60));

        // Load all data
        var allData = new Dictionary<string, (EnrichedBar[] M1, EnrichedBar[]? M5, EnrichedBar[]? M15, EnrichedBar[]? H1, EnrichedBar[]? D1)>();
        foreach (var sym in syms)
        {
            if (!CsvBarStorage.Exists(sym, "1m")) continue;
            var bars1m = TechnicalIndicators.EnrichWithIndicators(CsvBarStorage.LoadBars(sym, "1m"));

            EnrichedBar[]? ctx5m = null, ctx15m = null, ctx1h = null, ctx1d = null;
            foreach (var tf in new[] { "5m", "15m", "1h", "1D" })
            {
                if (!CsvBarStorage.Exists(sym, tf)) continue;
                var b = CsvBarStorage.LoadBars(sym, tf);
                if (b.Length == 0) continue;
                var e = TechnicalIndicators.EnrichWithIndicators(b);
                switch (tf)
                {
                    case "5m": ctx5m = e; break;
                    case "15m": ctx15m = e; break;
                    case "1h": ctx1h = e; break;
                    case "1D": ctx1d = e; break;
                }
            }

            allData[sym] = (bars1m, ctx5m, ctx15m, ctx1h, ctx1d);
        }

        // Walk through bars for each symbol checking signals
        foreach (var (sym, (bars1m, ctx5m, ctx15m, ctx1h, ctx1d)) in allData)
        {
            if (!SymbolStrategy.ContainsKey(sym)) continue;
            if (DailyTrades >= MaxDailyTrades) break;

            var signal = CheckForSignal(sym, bars1m, ctx5m, ctx15m, ctx1h, ctx1d);
            if (signal != null && !Positions.ContainsKey(sym))
            {
                var now = bars1m[^1].Bar.Timestamp;
                RecordEntry(sym, signal, signal.EntryPrice, now);

                // Simulate exit using the last bar price
                var lastPrice = bars1m[^1].Bar.Close;
                BacktestBar? lastBarData = bars1m.Length >= 1 ? bars1m[^1].Bar : null;
                BacktestBar? prevBarData = bars1m.Length >= 2 ? bars1m[^2].Bar : null;
                var exitReason = ManagePosition(sym, lastPrice, now, lastBarData, prevBarData);
                if (exitReason != null)
                {
                    RecordExit(sym, lastPrice, exitReason);
                }
            }
        }

        PrintSummary();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetStrategyName(string symbol) =>
        SymbolStrategy.TryGetValue(symbol, out var st) ? st.ToString() : "Unknown";

    private static IBacktestStrategy CreateStrategy(LiveStrategyType stratType) => stratType switch
    {
        LiveStrategyType.TrendV13 => new ConductStrategyV3(CfgTrend),
        LiveStrategyType.V3Balanced => new StrategyV3(CfgV3),
        LiveStrategyType.V4ExhRunner => new StrategyV4(CfgV4ExhRunner),
        LiveStrategyType.V4ExhBase => new StrategyV4(CfgV4ExhBase),
        LiveStrategyType.V5PullbackVwap => new StrategyV5(CfgV5PullbackVwap),
        LiveStrategyType.V5Tight => new StrategyV5(CfgV5Tight),
        _ => new ConductStrategyV3(CfgTrend),
    };

    private static V5Config? GetV5Config(LiveStrategyType stratType) => stratType switch
    {
        LiveStrategyType.V5PullbackVwap => CfgV5PullbackVwap,
        LiveStrategyType.V5Tight => CfgV5Tight,
        _ => null,
    };

    private static ExitCascadeParams GetCascadeParams(LiveStrategyType stratType) => stratType switch
    {
        LiveStrategyType.TrendV13 => new ExitCascadeParams
        {
            HardStopR = CfgTrend.HardStopR, TrailR = CfgTrend.TrailR,
            Tp1R = CfgTrend.Tp1R, Tp2R = CfgTrend.Tp2R,
            BreakevenR = CfgTrend.BreakevenR, GivebackPct = CfgTrend.GivebackPct,
            MaxHoldSeconds = 90 * 60,
        },
        LiveStrategyType.V3Balanced => new ExitCascadeParams
        {
            HardStopR = CfgV3.HardStopR, TrailR = CfgV3.TrailR,
            Tp1R = CfgV3.Tp1R, Tp2R = CfgV3.Tp2R,
            BreakevenR = CfgV3.BreakevenR, GivebackPct = CfgV3.GivebackPct,
            MaxHoldSeconds = 90 * 60,
        },
        LiveStrategyType.V4ExhRunner => new ExitCascadeParams
        {
            HardStopR = CfgV4ExhRunner.HardStopR, TrailR = CfgV4ExhRunner.TrailR,
            Tp1R = CfgV4ExhRunner.Tp1R, Tp2R = CfgV4ExhRunner.Tp2R,
            BreakevenR = CfgV4ExhRunner.BreakevenR, GivebackPct = CfgV4ExhRunner.GivebackPct,
            MaxHoldSeconds = 120 * 60,
        },
        LiveStrategyType.V4ExhBase => new ExitCascadeParams
        {
            HardStopR = CfgV4ExhBase.HardStopR, TrailR = CfgV4ExhBase.TrailR,
            Tp1R = CfgV4ExhBase.Tp1R, Tp2R = CfgV4ExhBase.Tp2R,
            BreakevenR = CfgV4ExhBase.BreakevenR, GivebackPct = CfgV4ExhBase.GivebackPct,
            MaxHoldSeconds = 120 * 60,
        },
        LiveStrategyType.V5PullbackVwap => new ExitCascadeParams
        {
            HardStopR = CfgV5PullbackVwap.HardStopR, TrailR = CfgV5PullbackVwap.TrailR,
            Tp1R = CfgV5PullbackVwap.Tp1R, Tp2R = CfgV5PullbackVwap.Tp2R,
            BreakevenR = CfgV5PullbackVwap.BreakevenR, GivebackPct = CfgV5PullbackVwap.GivebackPct,
            MaxHoldSeconds = (int)CfgV5PullbackVwap.MaxHoldBars * 60,
        },
        LiveStrategyType.V5Tight => new ExitCascadeParams
        {
            HardStopR = CfgV5Tight.HardStopR, TrailR = CfgV5Tight.TrailR,
            Tp1R = CfgV5Tight.Tp1R, Tp2R = CfgV5Tight.Tp2R,
            BreakevenR = CfgV5Tight.BreakevenR, GivebackPct = CfgV5Tight.GivebackPct,
            MicroTrailCents = CfgV5Tight.MicroTrailCents,
            MicroTrailActivateCents = CfgV5Tight.MicroTrailActivateCents,
            MaxHoldSeconds = (int)CfgV5Tight.MaxHoldBars * 60,
        },
        _ => new ExitCascadeParams
        {
            HardStopR = CfgV4ExhBase.HardStopR, TrailR = CfgV4ExhBase.TrailR,
            Tp1R = CfgV4ExhBase.Tp1R, Tp2R = CfgV4ExhBase.Tp2R,
            BreakevenR = CfgV4ExhBase.BreakevenR, GivebackPct = CfgV4ExhBase.GivebackPct,
            MaxHoldSeconds = 90 * 60,
        },
    };
}
