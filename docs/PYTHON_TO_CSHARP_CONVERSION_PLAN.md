# Python → C# Conversion Plan

## Objective
Convert ALL Python backtest files into C# code integrated into the existing `Harvester.App` project so the application builds and all services work.

---

## Table of Contents
1. [Architecture Decision](#1-architecture-decision)
2. [File Mapping](#2-file-mapping)
3. [New NuGet Dependencies](#3-new-nuget-dependencies)
4. [Step-by-Step Implementation](#4-step-by-step-implementation)
5. [Integration Points](#5-integration-points)
6. [Build & Verification](#6-build--verification)

---

## 1. Architecture Decision

### Approach: Add new folders/files INSIDE `Harvester.App`

**Rationale**:
- The existing app is monolithic with no DI container — adding a separate project would require DI setup that doesn't exist
- All strategy infrastructure (`IStrategyRuntime`, `StrategyDataSlice`, `ReplayOrderIntent`, etc.) is in `Harvester.App`
- The Python backtest system maps directly to the existing replay pipeline
- Strategy configs already use env-var overrides — same pattern applies

### New folder structure in `src/Harvester.App/`:
```
src/Harvester.App/
├── Backtest/                           ← NEW top-level folder
│   ├── Indicators/
│   │   └── TechnicalIndicators.cs      ← indicators.py (all 24 functions)
│   │   └── IndicatorModels.cs          ← result records (BollingerResult, MacdResult, etc.)
│   ├── DataFetcher/
│   │   └── BacktestDataFetcher.cs      ← data_fetcher.py (IBKR fetch + CSV cache)
│   │   └── CsvBarStorage.cs            ← CSV read/write for OHLCV data
│   ├── Engine/
│   │   └── BacktestEngine.cs           ← engine.py (run_backtest, compute_statistics)
│   │   └── BacktestModels.cs           ← BacktestResult, TradeResult, Signal, StrategyConfig records
│   │   └── EquityCurveBuilder.cs       ← build_equity_curve
│   ├── Strategies/
│   │   ├── StrategyBase.cs             ← Shared base: IBacktestStrategy, Side, ExitReason, HtfBias, exit chain
│   │   ├── ConductStrategyV2.cs        ← strategy.py (V2.0 Conduct Multi-TF Trend)
│   │   ├── StrategyV3.cs               ← strategy_v3.py (VWAP+BB+Keltner Squeeze)
│   │   ├── StrategyV4.cs               ← strategy_v4.py (Image Pattern, 6 families)
│   │   ├── StrategyV5.cs               ← strategy_v5.py (Smart Mean-Reversion)
│   │   ├── StrategyV6.cs               ← strategy_v6.py (Opening Range Breakout)
│   │   └── StrategyV7.cs               ← strategy_v7.py (9 EMA Momentum Scalp)
│   ├── Scanner/
│   │   └── BacktestScanner.cs          ← quick_scanner.py (multi-strategy signal scanner)
│   ├── Live/
│   │   └── LivePaperBot.cs             ← live_paper.py (live paper trading bot)
│   │   └── LivePositionManager.cs      ← Position tracking + stop management
│   └── Sweep/
│       └── ParameterSweepRunner.cs     ← Unified sweep engine (replaces v5_sweep, v6v7_sweep, optimize)
│       └── SweepModels.cs              ← SweepConfig, SweepResult records
```

---

## 2. File Mapping — Python Source → C# Target

| Python Source | Lines | C# Target | Strategy |
|---------------|-------|-----------|----------|
| `indicators.py` | 328 | `Backtest/Indicators/TechnicalIndicators.cs` + `IndicatorModels.cs` | Replace pandas with double[] arrays |
| `data_fetcher.py` | 136 | `Backtest/DataFetcher/BacktestDataFetcher.cs` + `CsvBarStorage.cs` | Use existing `IBrokerAdapter` + CSV I/O |
| `engine.py` | 192 | `Backtest/Engine/BacktestEngine.cs` + `BacktestModels.cs` + `EquityCurveBuilder.cs` | |
| `strategy.py` | 395 | `Backtest/Strategies/ConductStrategyV2.cs` | |
| `strategy_cycle.py` | 429 | `Backtest/Strategies/StrategyCycle.cs` | |
| `strategy_v3.py` | 438 | `Backtest/Strategies/StrategyV3.cs` | |
| `strategy_v4.py` | 988 | `Backtest/Strategies/StrategyV4.cs` | |
| `strategy_v5.py` | 452 | `Backtest/Strategies/StrategyV5.cs` | |
| `strategy_v6.py` | 486 | `Backtest/Strategies/StrategyV6.cs` | |
| `strategy_v7.py` | 468 | `Backtest/Strategies/StrategyV7.cs` | |
| `live_paper.py` | 764 | `Backtest/Live/LivePaperBot.cs` + `LivePositionManager.cs` | Use existing `IBrokerAdapter` |
| `quick_scanner.py` | 242 | `Backtest/Scanner/BacktestScanner.cs` | |
| `run_backtest.py` | 164 | CLI mode in `Program.cs` / `SnapshotRuntime.cs` | New RunMode |
| `v5_sweep.py` | 233 | `Backtest/Sweep/ParameterSweepRunner.cs` | Unified |
| `v6v7_sweep.py` | 317 | `Backtest/Sweep/ParameterSweepRunner.cs` | Unified |
| `optimize.py` | 199 | `Backtest/Sweep/ParameterSweepRunner.cs` | Unified |
| Runner scripts | ~1,600 | Not converted — replaced by CLI modes | |

---

## 3. New NuGet Dependencies

**NONE required.** The Python system's dependencies map to existing C# capabilities:

| Python Library | C# Replacement |
|---------------|----------------|
| `pandas` (DataFrame) | `double[]` arrays + custom bar records |
| `numpy` | `System.Math` + inline array ops |
| `ib_insync` | Already have `IBApi` + `IBrokerAdapter` |
| `tabulate` | `Console.WriteLine` + `string.Format` |
| `zoneinfo` | `TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")` |

The existing C# codebase already implements everything from scratch (no ML libraries, no indicator libraries).

---

## 4. Step-by-Step Implementation

### Phase 1: Foundation (Models + Indicators)
*Estimated: ~1,200 lines of C#*

#### Step 1.1 — Shared Models & Interfaces
Create `Backtest/Engine/BacktestModels.cs`:
```csharp
namespace Harvester.App.Backtest.Engine;

// Shared enums (defined once, used by all strategies)
public enum TradeSide { Long, Short }
public enum ExitReason { HardStop, BreakEven, Trailing, Tp1, Tp2, Tp3, TimeStop, Eod, SignalReversal, MicroTrail, ReversalFlatten, Giveback, EmaTrail }
public enum HtfBias { Bull, Bear, Neutral }

// Universal signal record
public sealed record BacktestSignal(
    int BarIndex, DateTime Timestamp, TradeSide Side,
    double EntryPrice, double StopPrice, double RiskPerShare,
    int PositionSize, double AtrValue, HtfBias HtfTrend,
    string MtfMomentum, string SubStrategy = "");

// Universal trade result record
public sealed record BacktestTradeResult(
    int EntryBar, DateTime EntryTime, int ExitBar, DateTime ExitTime,
    TradeSide Side, double EntryPrice, double ExitPrice, double StopPrice,
    int PositionSize, double Pnl, double PnlR, ExitReason ExitReason,
    double PeakR, int BarsHeld);

// Backtest result container
public sealed record BacktestResult(
    string Symbol, string TriggerTf, object Config,
    IReadOnlyList<BacktestTradeResult> Trades,
    IReadOnlyList<(DateTime Time, double Equity)> EquityCurve,
    BacktestStatistics Stats);

// Statistics record
public sealed record BacktestStatistics(
    int TotalTrades, int Winners, int Losers, double WinRate,
    double AvgWin, double AvgLoss, double ProfitFactor,
    double ExpectancyR, double TotalPnl, double MaxDrawdown,
    double MaxDrawdownPct, double Sharpe, double AvgBarsHeld,
    int LongTrades, int ShortTrades, double LongWinRate, double ShortWinRate,
    IReadOnlyDictionary<ExitReason, int> ExitReasons);
```

Create `Backtest/Strategies/StrategyBase.cs`:
```csharp
namespace Harvester.App.Backtest.Strategies;

// Common interface all Python strategies implement
public interface IBacktestStrategy
{
    IReadOnlyList<BacktestSignal> GenerateSignals(
        BacktestBar[] triggerBars,
        BacktestBar[]? bars5m = null,
        BacktestBar[]? bars15m = null,
        BacktestBar[]? bars1h = null,
        BacktestBar[]? bars1d = null);

    BacktestTradeResult? SimulateTrade(BacktestSignal signal, BacktestBar[] triggerBars);
}

// OHLCV bar (replaces pandas DataFrame rows)
public sealed record BacktestBar(
    DateTime Timestamp, double Open, double High, double Low,
    double Close, double Volume);

// Enriched bar with all indicator values
public sealed class EnrichedBar
{
    public BacktestBar Bar { get; init; }
    public double Ema9 { get; set; }
    public double Ema21 { get; set; }
    public double Ema50 { get; set; }
    public double Sma20 { get; set; }
    public double Sma200 { get; set; }
    public double Atr14 { get; set; }
    public double Rsi14 { get; set; }
    public double Macd { get; set; }
    public double MacdSignal { get; set; }
    public double MacdHist { get; set; }
    public double BbMid { get; set; }
    public double BbUpper { get; set; }
    public double BbLower { get; set; }
    public double BbPctB { get; set; }
    public double BbBandwidth { get; set; }
    public double Adx { get; set; }
    public double PlusDi { get; set; }
    public double MinusDi { get; set; }
    public double Supertrend { get; set; }
    public int StDirection { get; set; }
    public double Rvol { get; set; }
    public double Vwap { get; set; }
    public double StochK { get; set; }
    public double StochD { get; set; }
    public double KcMid { get; set; }
    public double KcUpper { get; set; }
    public double KcLower { get; set; }
    public double Mfi14 { get; set; }
    public double OfiRaw { get; set; }
    public double OfiCum { get; set; }
    public double OfiSignal { get; set; }
    public double SpreadRatio { get; set; }
    public double SpreadZ { get; set; }
    public double VolAccel { get; set; }
    public double L2Liquidity { get; set; }
    public double WillR14 { get; set; }
    public double DcUpper { get; set; }
    public double DcLower { get; set; }
    public double DcMid { get; set; }
    public double DcPct { get; set; }
    public double Dpo20 { get; set; }
}
```

#### Step 1.2 — Technical Indicators
Create `Backtest/Indicators/TechnicalIndicators.cs`:
- Convert all 24 Python functions from `indicators.py`
- Replace `pd.Series` → `double[]`, `pd.DataFrame` → `BacktestBar[]`
- Implement `EnrichWithIndicators(BacktestBar[] bars) → EnrichedBar[]` (master enrichment)

Key conversions:
| Python (pandas) | C# (arrays) |
|-----------------|-------------|
| `series.ewm(span=period).mean()` | Manual EMA loop: `ema[i] = alpha * val + (1-alpha) * ema[i-1]` |
| `series.rolling(period).mean()` | Sliding window sum / period |
| `df['Close'].shift(1)` | `bars[i-1].Close` |
| `series.cumsum()` | Running sum loop |
| `NaN` handling | `double.NaN` checks with fallback |

#### Step 1.3 — Indicator Result Models
Create `Backtest/Indicators/IndicatorModels.cs` for multi-column indicator results:
```csharp
public sealed record BollingerResult(double Mid, double Upper, double Lower, double PctB, double Bandwidth);
public sealed record MacdResult(double Macd, double Signal, double Histogram);
public sealed record AdxResult(double Adx, double PlusDi, double MinusDi);
public sealed record SupertrendResult(double Value, int Direction);
public sealed record StochasticResult(double K, double D);
public sealed record KeltnerResult(double Mid, double Upper, double Lower);
public sealed record DonchianResult(double Upper, double Lower, double Mid, double Pct);
public sealed record OrderFlowResult(double Raw, double Cumulative, double Signal);
public sealed record SpreadResult(double Ratio, double ZScore);
```

---

### Phase 2: Data Layer (CSV + IBKR Fetch)
*Estimated: ~300 lines of C#*

#### Step 2.1 — CSV Bar Storage
Create `Backtest/DataFetcher/CsvBarStorage.cs`:
- `SaveBars(string symbol, string timeframe, BacktestBar[] bars)` → CSV at `backtest/data/<SYMBOL>/<tf>.csv`
- `LoadBars(string symbol, string timeframe) → BacktestBar[]` → read CSV, parse timestamps (UTC)
- `Exists(string symbol, string timeframe) → bool`
- Generate same CSV format as Python (`Timestamp,Open,High,Low,Close,Volume`)

#### Step 2.2 — IBKR Data Fetcher
Create `Backtest/DataFetcher/BacktestDataFetcher.cs`:
- Reuse existing `IBrokerAdapter.RequestHistoricalData()` from the C# app
- Implement `FetchAndSave(string[] symbols, string[] timeframes)`:
  1. Connect via `IbkrSession` (reuse existing)
  2. For each symbol+timeframe → `RequestHistoricalData` with IBKR duration/barSize
  3. Convert `HistoricalBarRow` → `BacktestBar`
  4. Save via `CsvBarStorage`
- Timeframe config map (same as Python):
  ```
  "30s" → barSize="30 secs", duration="2 D"
  "1m"  → barSize="1 min", duration="5 D"
  "5m"  → barSize="5 mins", duration="20 D"
  "15m" → barSize="15 mins", duration="40 D"
  "1h"  → barSize="1 hour", duration="90 D"
  "1D"  → barSize="1 day", duration="365 D"
  ```

---

### Phase 3: Backtest Engine
*Estimated: ~400 lines of C#*

#### Step 3.1 — Equity Curve Builder
Create `Backtest/Engine/EquityCurveBuilder.cs`:
- `Build(IReadOnlyList<BacktestTradeResult> trades, double initialCapital) → (DateTime, double)[]`
- Time-indexed equity curve from sequential trade PnL

#### Step 3.2 — Statistics Calculator
Add to `Backtest/Engine/BacktestEngine.cs`:
- `ComputeStatistics(IReadOnlyList<BacktestTradeResult> trades, double initialCapital) → BacktestStatistics`
- All metrics: win rate, PF, expectancy, Sharpe (√252 annualized), max drawdown, exit reasons, long/short splits

#### Step 3.3 — Run Backtest
Add to `Backtest/Engine/BacktestEngine.cs`:
- `RunBacktest(string symbol, BacktestBar[] triggerBars, string triggerTf, BacktestBar[]? bars5m, ..., IBacktestStrategy strategy) → BacktestResult`
- Generate signals → simulate trades (no overlapping) → compute stats → return result

---

### Phase 4: Strategy V2.0 (Conduct Multi-TF Trend)
*Estimated: ~500 lines of C#*

#### Step 4.1 — ConductStrategyV2
Create `Backtest/Strategies/ConductStrategyV2.cs`:
- `ConductV2Config` record with all 29 parameters (defaults matching Python)
- Implement `IBacktestStrategy`
- **HTF Bias computation**: 1h/1D EMA slope + ADX+DI + MACD histogram → Bull/Bear/Neutral
- **Entry conditions (LONG)**:
  1. HTF ≠ Bear
  2. Supertrend flip up OR EMA_9 pullback cross, Close > EMA_21
  3. RVOL ≥ 1.3
  4. RSI in (35, 70)
  5. 20MA exhaustion: `(Close - SMA_20) / ATR ≤ 0.5`
  6. MTF momentum: 5m/15m MACD + RSI aligned
- **Position sizing**: `max(1, (int)(risk_dollars / (hard_stop_r * ATR)))`
- **Exit chain**: Hard stop → TP2 → TP1 → BE → Trail → Giveback → Time stop
- Apply slippage + commission on entry/exit

---

### Phase 5: Strategies V3-V7
*Estimated: ~2,500 lines of C#*

#### Step 5.1 — StrategyV3 (VWAP+BB+Keltner Squeeze)
Create `Backtest/Strategies/StrategyV3.cs`:
- `V3Config` record
- Three sub-strategies: V3a (VWAP Mean Reversion), V3b (BB Bounce), V3c (Keltner Squeeze Breakout)
- L2 proxy filters: liquidity, spread Z, OFI confirm

#### Step 5.2 — StrategyV4 (Image Pattern)
Create `Backtest/Strategies/StrategyV4.cs`:
- `V4Config` record
- Six pattern families: BuySetup, SellSetup, 123Pattern, Breakout, Breakdown, Exhaustion
- Enhanced scoring (7 sub-criteria per pattern, min score = 3)
- ~988 lines Python → ~800 lines C#

#### Step 5.3 — StrategyCycle (Buy-Low/Sell-High)
Create `Backtest/Strategies/StrategyCycle.cs`:
- Stochastic + WilliamsR + BB/KC oscillator confluence
- 2+ of 3 confirmation requirement

#### Step 5.4 — StrategyV5 (Smart Mean-Reversion)
Create `Backtest/Strategies/StrategyV5.cs`:
- `V5Config` record (36 parameters)
- Three sub-strategies: Pullback, VWAP Tag, Exhaustion Fade
- Key innovations: 20MA exhaustion as primary filter, micro-trailing ($0.03), reversal flatten
- BE at +0.5R (earlier than V2)

#### Step 5.5 — StrategyV6 (Opening Range Breakout)
Create `Backtest/Strategies/StrategyV6.cs`:
- `V6Config` record (27 parameters)
- Per-day Opening Range computation (9:30-9:45 ET High/Low)
- UTC→Eastern Time conversion using `TimeZoneInfo`
- Entry windows [(585,690), (840,930)] as minute-of-day
- One entry per direction per day
- Stop at opposite OR side (or midpoint option)

#### Step 5.6 — StrategyV7 (9 EMA Momentum Scalp)
Create `Backtest/Strategies/StrategyV7.cs`:
- `V7Config` record (28 parameters)
- EMA_9/EMA_20 alignment + slope check
- Pullback to 9 EMA (within 0.2 ATR proximity)
- Unique: 9 EMA trailing stop (dynamic stop = EMA_9 - 0.1×ATR buffer)
- Skip first 10 minutes of session
- Short max hold (45 bars)

---

### Phase 6: Shared Exit Engine Refactor
*Estimated: ~250 lines of C#*

#### Step 6.1 — Extract Common Exit Logic
All Python strategies use the same exit chain pattern with minor variations. Create `Backtest/Strategies/ExitEngine.cs`:
```csharp
public sealed class ExitEngine
{
    // Evaluates all exit conditions in priority order
    // Returns (exitPrice, exitReason) or null if trade continues
    public static (double Price, ExitReason Reason)?
        EvaluateExits(ExitEngineConfig cfg, ExitEngineState state, BacktestBar bar, int barIndex);
}

public sealed record ExitEngineConfig(
    double HardStopR, double BreakevenR, double TrailR, double GivebackPct,
    double Tp1R, double Tp1ScalePct, double Tp2R, int MaxHoldBars,
    int EodBarMinute, double SlippageCents, double CommissionPerShare,
    bool UseMicroTrail, double MicroTrailCents, double MicroTrailActivateCents,
    bool UseReversalFlatten, bool UseEmaTrail, double EmaTrailBufferAtr);

public sealed class ExitEngineState
{
    public double EntryPrice { get; set; }
    public double StopPrice { get; set; }
    public double RiskPerShare { get; set; }
    public TradeSide Side { get; set; }
    public double PeakR { get; set; }
    public double PeakPrice { get; set; }
    public bool BreakevenActivated { get; set; }
    public bool Tp1Hit { get; set; }
    public bool MicroTrailActivated { get; set; }
    public double MicroTrailStop { get; set; }
    public int PositionSize { get; set; }
    public int EntryBar { get; set; }
}
```

This eliminates ~150 lines of duplicated exit code per strategy.

---

### Phase 7: Scanner
*Estimated: ~300 lines of C#*

#### Step 7.1 — BacktestScanner
Create `Backtest/Scanner/BacktestScanner.cs`:
- `ScanAllSymbols(string[] symbols, IBrokerAdapter broker) → ScannerResult`
- For each symbol:
  1. Fetch multi-TF data via `BacktestDataFetcher`
  2. Enrich with indicators
  3. Compute 20MA distance (global exhaustion filter)
  4. Run ALL 7 strategies, collect recent signals (within 20 bars)
  5. Reject signals in exhaustion zone
- Sort by freshness → score → return best signal
- Optionally enter trade via broker

**Integration with existing `ScannerSelectionEngineV2`**: The Python scanner is simpler (indicator-based only). Can optionally bridge to the existing V2 engine by generating `ScannerV2CandidateFileRow` from Python-style signals.

---

### Phase 8: Live Paper Trading Bot
*Estimated: ~800 lines of C#*

#### Step 8.1 — LivePositionManager
Create `Backtest/Live/LivePositionManager.cs`:
- `LivePosition` record: symbol, side, entry_price, stop_price, position_size, strategy config, state tracking (peak_r, be_activated, micro_trail_stop, etc.)
- `ManagePosition(position, currentBar) → LiveAction` (update stop / flatten / hold)
- Micro-trail stop: compute new stop → return `UpdateStop` action
- Reversal flatten: detect engulfing/wick patterns → return `Flatten` action

#### Step 8.2 — LivePaperBot
Create `Backtest/Live/LivePaperBot.cs`:
- **Reuse existing `IBrokerAdapter`** instead of `ib_insync`
- Per-symbol strategy assignment (configurable map):
  ```csharp
  var assignments = new Dictionary<string, IBacktestStrategy>
  {
      ["AAPL"] = new StrategyV5(V5Config.PullbackVwap),
      ["TSLA"] = new StrategyV5(V5Config.Tight),
      ["NVDA"] = new ConductStrategyV2(ConductV2Config.TrendDefault),
      ["AMD"]  = new ConductStrategyV2(ConductV2Config.TrendDefault),
      ["META"] = new StrategyV5(V5Config.Tight),
  };
  ```
- Main loop:
  1. Connect via `IbkrSession` (reuse existing session management)
  2. Sync positions via `IBrokerAdapter.RequestPositions()`
  3. While market open (9:30-16:00 ET):
     - For each open position → `LivePositionManager.ManagePosition()`
     - If action = UpdateStop → `IBrokerAdapter.PlaceOrder()` (modify stop)
     - If action = Flatten → `IBrokerAdapter.PlaceOrder()` (market close)
     - For each symbol without position → `strategy.GenerateSignals()` on live bars
     - If signal → `IBrokerAdapter.PlaceOrder()` (market entry + stop order)
  4. EOD (15:55 ET) → flatten all → summary

#### Step 8.3 — Integration with SnapshotRuntime
The live bot should be accessible via a new RunMode:
```csharp
case RunMode.BacktestLivePaper:
    await RunBacktestLivePaperModeAsync(cancellationToken);
    break;
```

---

### Phase 9: Parameter Sweep Engine
*Estimated: ~400 lines of C#*

#### Step 9.1 — Unified Sweep Runner
Create `Backtest/Sweep/ParameterSweepRunner.cs`:
- `SweepConfig` record: strategy type, parameter grid, symbols, timeframes
- `RunSweep(SweepConfig config) → SweepResult[]`
- Generates all config permutations → runs backtest for each → ranks by PnL/Sharpe
- Replaces 5+ Python sweep scripts with one engine

Create `Backtest/Sweep/SweepModels.cs`:
```csharp
public sealed record SweepConfig(
    string StrategyType,
    string[] Symbols,
    string TriggerTimeframe,
    Dictionary<string, object[]> ParameterGrid);

public sealed record SweepResult(
    string Symbol, string ConfigLabel, object Config,
    BacktestStatistics Stats, double HybridScore);

public sealed record HybridSelection(
    string Symbol, string BestStrategy, string BestConfigLabel,
    BacktestStatistics Stats);
```

---

### Phase 10: CLI Integration & New RunModes
*Estimated: ~200 lines of C# modifications*

#### Step 10.1 — Add RunModes to AppOptions
Modify `SnapshotRuntime.cs` RunMode enum:
```csharp
// Add to RunMode enum:
BacktestRun,          // Run single strategy backtest
BacktestCompare,      // Compare all strategies on a symbol
BacktestSweep,        // Parameter sweep
BacktestScan,         // One-shot scanner
BacktestLivePaper,    // Live paper trading bot
```

#### Step 10.2 — Add CLI Arg Parsing
Add to `AppOptions.Parse()`:
```
--backtest-symbols AAPL,TSLA,NVDA     (comma-separated)
--backtest-strategy v2|v3|v4|v5|v6|v7 (strategy selection)
--backtest-trigger-tf 30s|1m|5m        (trigger timeframe)
--backtest-sweep-strategy v5|v6|v7     (sweep target)
--backtest-live-port 7497              (IBKR port for live mode)
```

#### Step 10.3 — Add Mode Handlers
Add methods to `SnapshotRuntime.cs`:
```csharp
private async Task<int> RunBacktestRunModeAsync(CancellationToken ct)
{
    var bars = CsvBarStorage.LoadBars(options.Symbol, options.BacktestTriggerTf);
    var enriched = TechnicalIndicators.EnrichWithIndicators(bars);
    var strategy = CreateBacktestStrategy(options.BacktestStrategy);
    var result = BacktestEngine.RunBacktest(options.Symbol, enriched, options.BacktestTriggerTf, ..., strategy);
    Console.WriteLine(result.SummaryTable());
    return 0;
}
```

---

### Phase 11: Data Migration
*Estimated: ~50 lines of C# + file copy*

#### Step 11.1 — Reuse Existing CSV Data
The Python system stores data at `backtest/data/<SYMBOL>/<tf>.csv`. The C# `CsvBarStorage` reads from the same location, so all cached data is immediately available.

#### Step 11.2 — BacktestBar ↔ HistoricalBarRow Bridge
Create conversion methods for interop with existing replay system:
```csharp
public static BacktestBar FromHistoricalBarRow(HistoricalBarRow row) =>
    new(row.TimestampUtc, row.Open, row.High, row.Low, row.Close, (double)row.Volume);

public static HistoricalBarRow ToHistoricalBarRow(BacktestBar bar, int requestId) =>
    new(bar.Timestamp, requestId, bar.Timestamp.ToString("yyyyMMdd HH:mm:ss"),
        bar.Open, bar.High, bar.Low, bar.Close, (decimal)bar.Volume, bar.Close, 1);
```

---

## 5. Integration Points with Existing C# Code

### 5.1 Reusing Existing Components

| Existing C# Component | Used By New Code | How |
|----------------------|-----------------|-----|
| `IBrokerAdapter` | DataFetcher, LiveBot, Scanner | Market data + order placement |
| `IbkrSession` | DataFetcher, LiveBot | TWS connection management |
| `SnapshotEWrapper` | DataFetcher, LiveBot | TWS callback data |
| `UsEquitiesExchangeCalendarService` | V6 (ORB), LiveBot | Market hours + holidays |
| `OrderFactory` | LiveBot | Stop/Market/Limit order creation |
| `ContractFactory` | DataFetcher, LiveBot | Stock contract building |
| `PreTradeCostRiskEstimator` | LiveBot | Slippage/commission estimation |
| `HistoricalBarRow` | DataFetcher, bridge | Data type interop |
| `ReplayOrderIntent` | Scanner → replay pipeline bridge | Optional integration |
| `ExchangeSessionWindow` | V6 (Opening Range), LiveBot | Session timing |

### 5.2 Bridge to Existing Replay Pipeline

The new backtest strategies can optionally feed into the existing replay system by:
1. Implementing `IReplayOrderSignalSource` as an adapter
2. Generating `ReplayOrderIntent` from `BacktestSignal`
3. Using `ReplayExecutionSimulator` instead of the simpler Python-style simulation

```csharp
public class BacktestReplayBridge : IReplayOrderSignalSource
{
    private readonly IBacktestStrategy _strategy;

    public IReadOnlyList<ReplayOrderIntent> GetReplayOrderIntents(
        StrategyDataSlice dataSlice, StrategyRuntimeContext context)
    {
        var bars = dataSlice.HistoricalBars.Select(BacktestBar.FromHistoricalBarRow).ToArray();
        var signals = _strategy.GenerateSignals(bars);
        return signals.Select(s => ToReplayOrderIntent(s, dataSlice.TimestampUtc)).ToList();
    }
}
```

### 5.3 Namespace Organization
```
Harvester.App.Backtest.Indicators    — TechnicalIndicators, IndicatorModels
Harvester.App.Backtest.DataFetcher   — BacktestDataFetcher, CsvBarStorage
Harvester.App.Backtest.Engine        — BacktestEngine, BacktestModels, EquityCurveBuilder
Harvester.App.Backtest.Strategies    — StrategyBase, ConductStrategyV2, V3-V7, StrategyCycle, ExitEngine
Harvester.App.Backtest.Scanner       — BacktestScanner
Harvester.App.Backtest.Live          — LivePaperBot, LivePositionManager
Harvester.App.Backtest.Sweep         — ParameterSweepRunner, SweepModels
```

---

## 6. Build & Verification

### 6.1 Build Commands
```powershell
# Full build
dotnet build src/Harvester.App/Harvester.App.csproj

# Clean + rebuild
dotnet build src/Harvester.App/Harvester.App.csproj --no-incremental

# Publish
dotnet publish src/Harvester.App/Harvester.App.csproj -c Release
```

### 6.2 Verification Checklist

| Step | Command | Expected |
|------|---------|----------|
| 1. Build succeeds | `dotnet build` | 0 errors, 0 warnings |
| 2. Existing modes work | `--mode Connect --port 7497` | Connection successful |
| 3. Backtest runs | `--mode BacktestRun --symbol AAPL --backtest-strategy v2 --backtest-trigger-tf 30s` | Stats printed |
| 4. All strategies run | `--mode BacktestCompare --backtest-symbols AAPL` | Comparison table |
| 5. Sweep works | `--mode BacktestSweep --backtest-sweep-strategy v5 --backtest-symbols AAPL,TSLA` | Best configs |
| 6. Scanner works | `--mode BacktestScan --backtest-symbols AAPL,TSLA,NVDA,AMD,META --port 7497` | Best signal |
| 7. Live bot works | `--mode BacktestLivePaper --port 7497` | Connects, monitors, trades |
| 8. Existing replay works | `--mode StrategyReplay --replay-input data.json` | No regression |

### 6.3 Numerical Verification
Run the same data through Python and C# strategies, compare:
- Signal count should match exactly
- Trade PnL should match within $0.01 (floating point rounding)
- Statistics should match within 0.1%

### 6.4 Implementation Order Priority
```
Phase 1  → Foundation (models + indicators)     — MUST DO FIRST
Phase 2  → Data layer (CSV + fetch)              — MUST DO SECOND
Phase 3  → Backtest engine                       — MUST DO THIRD
Phase 4  → Strategy V2.0                         — First strategy
Phase 6  → Exit engine refactor                  — Before remaining strategies
Phase 5  → Strategies V3-V7                      — All strategies
Phase 10 → CLI integration                       — Wire into app
Phase 8  → Live bot                              — After strategies work
Phase 7  → Scanner                               — After strategies work
Phase 9  → Sweep engine                          — After strategies work
Phase 11 → Data migration + bridge               — Final integration
```

---

## Summary

| Metric | Value |
|--------|-------|
| New C# files | ~20 |
| Estimated new C# lines | ~6,500 |
| New NuGet packages | 0 |
| New project files | 0 (all in Harvester.App) |
| Breaking changes to existing code | 0 (additive only, except RunMode enum + AppOptions) |
| Python files replaced | 28 (all in backtest/) |
| Integration touch points | Program.cs, SnapshotRuntime.cs (RunMode + AppOptions) |
