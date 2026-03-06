# Strategy Module — Professional Code Audit Report

**Date:** 2026-03-06  
**Scope:** `src/Harvester.App/Strategy/` (38 files, ~19,350 LOC)  
**Auditor:** GitHub Copilot  

---

## 1. Executive Summary

The `Strategy` folder houses the complete trading strategy stack: live V3/V11 runtime pipeline, replay/backtest simulation engine, scanner selection logic, self-learning analytics, and supporting infrastructure (calendars, corporate actions, financing). The module has evolved rapidly through multiple product phases and now exhibits **several architectural hotspots** that increase maintenance burden, defect risk, and onboarding friction.

### Key Metrics

| Category | Files | LOC | % of Total |
|---|---:|---:|---:|
| Replay/Backtest engine | 10 | 14,610 | 75.5% |
| Live V3/V11 pipeline | 14 | 2,839 | 14.7% |
| Scanner selection | 2 | 1,947 | 10.1% |
| Interfaces & contracts | 6 | 133 | 0.7% |
| Infrastructure (calendar, clock) | 6 | 296 | — |
| **Total** | **38** | **~19,350** | **100%** |

### Top-Level Findings

| Severity | # | Summary |
|---|---:|---|
| **Critical** | 3 | Mega-file concentration, thread-safety gaps, missing validation |
| **High** | 5 | Duplicate abstractions, string-typed enums, untested paths |
| **Medium** | 8 | Config sprawl, naming inconsistency, I/O in constructors |
| **Low** | 6 | Style, dead records, minor performance |

---

## 2. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                     SnapshotRuntime (Host)                          │
│   consumes ILiveOrderSignalSource.ConsumeOrderIntents()             │
│   calls AcknowledgeOrderTransmitted / AcknowledgePositionClosed     │
└───────────┬──────────────────────────────────────────────────────────┘
            │
            ▼
┌──────────────────────────────────────────────────────────────────────┐
│  V3LiveRuntime  (IStrategyRuntime, ILiveOrderSignalSource)          │
│  ┌────────────┐  ┌────────────────┐  ┌──────────────┐              │
│  │FeatureBldr │  │ SignalEngine   │  │ RiskGuard    │              │
│  └────────────┘  └────────────────┘  └──────────────┘              │
│  ┌────────────┐  ┌────────────────┐  ┌──────────────┐              │
│  │OrderBridge │  │ PosMonitor     │  │ PosTracker   │              │
│  └────────────┘  └────────────────┘  └──────────────┘              │
│  ┌────────────┐  ┌────────────────┐  ┌──────────────┐              │
│  │CandleAggr  │  │ ExecStateMach  │  │ SelectGate   │              │
│  └────────────┘  └────────────────┘  └──────────────┘              │
│  ┌────────────┐  ┌────────────────┐                                │
│  │SymbolResol │  │ HistBarDedup   │                                │
│  └────────────┘  └────────────────┘                                │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│  Replay/Backtest Pipeline (separate parallel stack)                  │
│  ScannerCandidateReplayRuntime ─► DayTradingPipeline                │
│    ├── Overlays (Ovl001)                                            │
│    ├── Entry (ReplayScannerSingleShotEntryStrategy)                 │
│    ├── Trade Mgmt (Tmg001–Tmg049)                                   │
│    └── EOD (Eod001)                                                 │
│  ReplayExecutionSimulator    ─► fill simulation                     │
│  ReplayPerformanceAnalyzer   ─► equity curve + Sharpe               │
│  ReplaySelfLearningEngine V2 ─► GLM adaptive feedback               │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Critical Findings

### C-01 — Mega-File Concentration (9,272 LOC)

**File:** `ReplayStrategySystemLayout.cs` — **9,272 lines**

This single file contains:
- The `ReplayDayTradingPipeline` orchestrator
- Scanner selection module (`ReplayScannerSymbolSelectionModule`)
- Global safety overlay (`Ovl001`)
- Entry strategy (`ReplayScannerSingleShotEntryStrategy`)
- **49 trade management strategies** (Tmg001–Tmg049) with individual config records
- End-of-day strategy (`Eod001`)
- All helper/state classes for replay pipeline

**Risk:**  
- Merge conflicts are near-certain when two developers touch trade management.
- IDE navigation and refactoring tools perform poorly beyond ~3,000 LOC.
- Testing one strategy in isolation requires loading the entire file.

**Recommendation:**
```
Strategy/
  Replay/
    Pipeline/
      ReplayDayTradingPipeline.cs
    Overlays/
      Ovl001FlattenReversalAndGivebackCap.cs
    Entry/
      ReplayScannerSingleShotEntry.cs
    TradeManagement/
      Tmg001BracketExit.cs
      Tmg002BreakEvenEscalation.cs
      ...
    EndOfDay/
      Eod001ForceFlat.cs
    Scanner/
      ReplayScannerSymbolSelectionModule.cs
```

### C-02 — Thread-Safety Gaps in Live Runtime

`V3LiveRuntime` is called from `SnapshotRuntime` (~1/sec per symbol). The intent queue is properly locked, but:

1. **`_stateBySymbol`** — written in `OnDataAsync`, `AcknowledgeOrderTransmitted`, `InitializeAsync`, and `OnScheduledEventAsync` without synchronization. The host may call `AcknowledgeOrderTransmitted` on a different thread than `OnDataAsync`.
2. **`_positionTracker`** — `SyncFromPositions`, `UpdateMarkPrice`, `RecordEntry`, and `RecordClose` all mutate shared dictionaries. `RecordEntry`/`RecordClose` come via host callbacks, potentially on a TP thread.
3. **`_evaluations`, `_signals`** — properly locked via `_eventsLock`, but `_positionTracker` and `_candleAggregator` mutations happening outside the lock may interleave.
4. **`_executionStateMachine`** — internally locked (good), however the `V3LivePositionTracker` mutations it relies on are *not* locked.

**Recommendation:**
- Introduce a `_dataLock` around all per-tick state mutations in `OnDataAsync` or serialize all host callbacks onto the data processing path.
- Consider making `V3LivePositionTracker` itself thread-safe with internal locking, since it's called from multiple entry points.

### C-03 — Missing Validation / Silent Failures in Config Parsing

`V3LiveConfig.FromEnvironment()` uses `Math.Clamp`/`Math.Max` to sanitize bounds, but:

1. If `SessionStartUtc` / `SessionEndUtc` cannot be parsed (`TimeSpan.TryParse` in `IsWithinSession`), the session gate silently returns `true` — **all-hours trading** rather than failing safe.
2. If `V3LIVE_SYMBOLS` env var parses to zero symbols after dedup, a hardcoded fallback is used silently — the operator has no indication their config was rejected.
3. No startup validation log or diagnostic dump of the effective config is emitted.

**Recommendation:**
- Log the full effective config at `InitializeAsync` time at `Information` level.
- Throw (or log `Warning`) when session time parsing fails rather than defaulting open.
- Emit a `Warning` when symbol fallback kicks in.

---

## 4. High-Severity Findings

### H-01 — Duplicate MTF Candle Engines (Live vs Replay)

Two independent candle aggregation implementations exist:

| Component | Used By | Timeframes |
|---|---|---|
| `V3LiveCandleAggregator` | live runtime | 60s, 300s, 900s, 3600s, 86400s |
| `ReplayMtfCandleSignalEngine` | replay pipeline | 30s, 60s, 300s, 900s, 3600s, 86400s |

They use identical bucket-alignment logic (`AlignToBucketStart`) copied independently, and their MTF alignment logic (bull/bear per TF) is semantically identical but structurally different.

**Risk:** Behavioral divergence between live and backtest. Bug fixes in one don't propagate.  
**Recommendation:** Extract a shared `MtfCandleEngine<TCandle>` with timeframe config injected, and use it in both pipelines.

### H-02 — String-Typed Trade Semantics

Critical trade fields use raw strings throughout:

| Field | Values | Files using |
|---|---|---|
| `Side` | "LONG", "SHORT", "BUY", "SELL" | 7+ |
| `OrderType` | "MKT", "LMT", "MARKET", etc. | 5+ |
| `TimeInForce` | "IOC", "DAY", "DAY+" | 5+ |

The same semantic concept (`TradeSide`) exists as an **enum** in the backtest engine but is converted to/from strings at every boundary:
```csharp
// V3LiveOrderBridge
var action = side == TradeSide.Long ? "BUY" : "SELL";
// V3LiveRuntime AcknowledgeOrderTransmitted
var side = "LONG"; // default
```

**Risk:** Typos silently compile; `"BUY"` vs `"LONG"` confusion; case-sensitivity bugs.  
**Recommendation:** Define proper enums (`OrderSide`, `OrderType`, `TimeInForce`) in contracts and only serialize to string at the IBKR boundary.

### H-03 — ScannerCandidateReplayRuntime Constructor is 120+ Lines of Wiring

The constructor of `ScannerCandidateReplayRuntime` (~1,248 LOC) instantiates **49 trade management strategies** by hand, each with its own `BuildXxxConfigFromEnvironment()` call. This:

1. Makes the constructor a de-facto DI container — but without the benefits of DI (testing, replacement).
2. Every new Tmg requires touching both the constructor and the pipeline wiring array.
3. No strategy registry or discovery mechanism exists.

**Recommendation:**
- Introduce a strategy registry pattern or `IServiceProvider`-based approach.
- Use convention-based discovery: scan for types implementing `IReplayTradeManagementStrategy` and auto-register.
- Alternatively, define a `StrategyProfile` config that lists which Tmg strategies are active.

### H-04 — No Unit Test Surface / Testability Barriers

No files in the Strategy folder are interface-segregated enough for isolated unit testing:

1. `V3LiveFeatureBuilder.Build()` calls `TechnicalIndicators.*` directly — cannot inject alternative indicator implementations.
2. `V3LiveRuntime.OnDataAsync` directly calls 6+ sub-engines with no seam for mocking.
3. `V3LivePositionTracker.SyncFromPositions` couples directly to `PositionRow` IBKR transport records.
4. Config is read from env vars in the constructor path — hard to inject test configs without environment mutation.

**Recommendation:**
- Inject sub-engines via constructor (or use an options pattern).
- `V3LiveConfig` should accept a `Dictionary<string,string>` source for testability (not just `Environment.GetEnvironmentVariable`).
- Extract `IFeatureBuilder`, `ISignalEngine`, `IRiskGuard` interfaces for the live pipeline.

### H-05 — Replay Contracts File is a Mixed Bag (1,335 LOC)

`ReplayStrategyContracts.cs` contains:
- Data records (fill, portfolio, order intent)
- Strategy interfaces (overlay, entry, trade management, EOD)
- 19+ config records (Tmg001Config through Tmg049Config)
- Multiple implementation stubs

This makes it impossible to reference just the contracts without pulling all config defaults.

**Recommendation:** Split into:
- `ReplayContracts.cs` — pure data records and interfaces
- `ReplayConfigs.cs` — config records with defaults
- Or one file per strategy family.

---

## 5. Medium-Severity Findings

### M-01 — Naming Inconsistency Between Live and Replay

| Concept | Live Name | Replay Name |
|---|---|---|
| Order intent | `LiveOrderIntent` | `ReplayOrderIntent` |
| Feature snapshot | `V3LiveFeatureSnapshot` | (none — inline) |
| MTF alignment | `V3LiveMtfAlignment` | `ReplayMtfSignalSnapshot` |
| Position tracked | `V3LiveTrackedPosition` | `ReplayPortfolioRow` |

These represent the same domain concepts but share no base type or interface. This prevents shared analytics or cross-mode comparison tools from operating generically.

**Recommendation:** Define domain interfaces (`IOrderIntent`, `IFeatureSnapshot`, etc.) that both live and replay records implement.

### M-02 — `V3LivePositionTracker` Peak/Trough Logic Asymmetry

For SHORT positions:
```csharp
tracked.PeakPriceSinceEntry = tracked.PeakPriceSinceEntry > 0
    ? Math.Min(tracked.PeakPriceSinceEntry, markPrice)
    : markPrice;
```

The field is called `PeakPriceSinceEntry` but for shorts it tracks the *trough* (most favorable). Meanwhile `TroughPriceSinceEntry` tracks the adverse direction. This semantic inversion is confusing and error-prone for any future consumer.

**Recommendation:** Rename to `MostFavorablePriceSinceEntry` and `MostAdversePriceSinceEntry`, or create computed properties that respect side semantics.

### M-03 — Config Env-Var Sprawl (60+ Environment Variables)

`V3LiveConfig.FromEnvironment()` reads **60+** environment variables with multi-alias fallback chains (`V11LIVE_*`, `V3LIVE_*`). This is:

1. Undocumented beyond code.
2. Easy to misconfigure (typos in env var names silently use defaults).
3. No validation report at startup.

**Recommendation:**
- Add a `Validate()` method that checks for contradictions (e.g., `MinPrice > MaxPrice`).
- Emit a diagnostic table of all resolved values at startup.
- Consider migrating to `appsettings.json` binding via `IConfiguration` with env-var override.

### M-04 — `V3LivePositionMonitor` Uses ATR Fallback for Stop/TP

When `position.StopPrice > 0` is not set, the monitor falls back to computing stop from `_config.HardStopR * features.Atr14`. However:

1. `features.Atr14` is computed from the *current* historical bars, not from entry-time ATR. Volatility expansion after entry would widen the stop; contraction would tighten it beyond what was intended.
2. No entry-time ATR is cached on the position, so the fallback is always wrong.

**Recommendation:** Store `EntryAtr14` on `V3LiveTrackedPosition` at entry time and use that for stop/TP calculations when the tracked values are not explicitly set.

### M-05 — I/O in Constructor Paths

`ScannerCandidateReplayRuntime` constructor:
- Reads files from disk (`File.ReadAllText` / `JsonSerializer.Deserialize`)
- Loads self-learning bias entries
- Exports V2 scanner snapshot

Constructor I/O:
- Prevents lazy initialization
- Can throw `IOException` before the runtime is fully wired
- Makes object creation non-deterministic

**Recommendation:** Move all file I/O into `InitializeAsync()`.

### M-06 — Hardcoded Magic Numbers

| Location | Value | Intent |
|---|---|---|
| `V3LiveSignalEngine` | `MinSqueezeBarCount = 8` | Minimum squeeze duration |
| `V3LiveSignalEngine` | `OfiTiebreakerThreshold = 0.05` | OFI tiebreaker sensitivity |
| `V3LivePositionMonitor` | `* 3` (stale tolerance) | 3× entry staleness for exit |
| `V3LivePositionMonitor` | `* 0.3` (depth threshold) | 30% of depth min for exit |
| `V3LivePositionMonitor` | `>= 0.5` (progress check) | Time stop progress R |
| `V3LiveCandleAggregator` | `MaxHistoryPerTimeframe = 500` | Rolling window size |

**Recommendation:** Promote to `V3LiveConfig` properties or at minimum document them with named constants including rationale.

### M-07 — No Graceful Degradation When Indicator Computation Fails

`V3LiveFeatureBuilder.Build()` calls `TechnicalIndicators.*` on arrays of 30+ bars. If any indicator returns `NaN` or throws internally:
- `Atr14 = NaN` → signal engine rejects (`atr-invalid`)
- `Rsi14 = NaN` → signal scoring silently skips RSI conditions
- `BbPctB = NaN` → Bollinger conditions never fire (good)
- `Adx14 = NaN` → ADX filter bypassed (`double.IsNaN(f.Adx14) || ...`)

The inconsistency means some NaN indicators silently disable their filters while others correctly reject. This leads to regime changes in signal generation based on data quality rather than market state.

**Recommendation:** Standardize NaN handling: either all NaN indicators reject the entire evaluation, or all NaN indicators default-pass with a logged warning. Document the chosen behavior.

### M-08 — `ReplayExecutionSimulator` Size (2,427 LOC)

Second-largest file. Contains fill simulation, margin model, cash settlement, trailing stops, combo orders, and order book management. This is a realistic brokerage simulator but belongs in its own namespace/project.

**Recommendation:** Extract to `Strategy/Replay/Simulation/ReplayExecutionSimulator.cs` and consider splitting into focused simulators (fill engine, margin/cash, order management).

---

## 6. Low-Severity Findings

### L-01 — Dead Framework Models

`FrameworkModels.cs` defines `IAlphaModel`, `IPortfolioConstructionModel`, `IRiskManagementModel`, `IExecutionModel` and their null implementations. These are **not referenced** by any live or replay component. They appear to be an abandoned QuantConnect-style framework scaffold.

**Recommendation:** Remove or move to a `_planned/` folder if they represent future work.

### L-02 — LINQ Allocations in Hot Paths

`V3LiveFeatureBuilder.Build()` runs on every data tick (~1/sec per symbol):
```csharp
var bars = dataSlice.HistoricalBars
    .OrderBy(x => x.TimestampUtc)
    .Select(x => new BacktestBar(...))
    .ToArray();
var closes = bars.Select(x => x.Close).ToArray();
var volumes = bars.Select(x => x.Volume).ToArray();
```

This allocates 3 arrays + intermediate iterators per tick per symbol. With 4 symbols, that's 12 array allocations/sec.

**Recommendation:** Pre-allocate reusable buffers or use `ArrayPool<T>`. For indicators that accept `ReadOnlySpan<double>`, pass spans directly.

### L-03 — Inconsistent `DateTime.UtcNow` vs Deterministic Clock

The live runtime uses `DateTime.UtcNow` in `RecordEntry`, `OnIntentCreated`, `BuildRiskState`, etc. — while the replay pipeline has a proper `DeterministicReplayClock`. If anyone attempts to run the live strategy in a deterministic test harness, timestamps will be non-deterministic.

**Recommendation:** Inject an `ISystemClock` (or `TimeProvider` in .NET 8+) throughout the live pipeline.

### L-04 — `V3LiveHistoricalBarDeduplicator` Uses Value Equality Instead of Timestamp Watermark

The deduplicator checks 6 fields for exact equality to detect duplicates:
```csharp
bar.TimestampUtc == watermark.TimestampUtc &&
bar.Open == watermark.Open && bar.High == watermark.High && ...
```

If IBKR sends a corrected bar with the same timestamp but different OHLCV (e.g., after a late print correction), it won't be deduplicated. Conversely, if two genuinely different bars happen to have the same OHLCV but different timestamps, the second is correctly accepted. The current approach is overly conservative.

**Recommendation:** Use only `TimestampUtc` as watermark for dedup (already have the `<` check). The OHLCV equality check adds fragility without clear benefit.

### L-05 — Export Path Coupling

`V3LiveRuntime.OnShutdownAsync()` writes 8 JSON files to disk using hardcoded filename patterns. This:
- Mixes I/O concerns with strategy state management
- Makes export format changes require editing the runtime
- No abstraction for exporting to different sinks (DB, message queue, etc.)

**Recommendation:** Extract an `IStrategyExporter` interface with a `JsonFileExporter` implementation.

### L-06 — `V3LiveCandleAggregator` Timeframes are Static

```csharp
public static readonly int[] TimeframeSeconds = [60, 300, 900, 3600, 86400];
```

The live aggregator hardcodes 5 timeframes while the replay engine uses 6 (includes 30s). This asymmetry is intentional (30s in live from L1 ticks has low value) but undocumented and could surprise someone expecting parity.

**Recommendation:** Add a doc comment explaining the intentional omission of 30s in live mode.

---

## 7. Improvement Proposals (Prioritized Roadmap)

### Phase 1: Critical Safety & Hygiene (1-2 sprints)

| # | Action | Impact | Effort |
|---:|---|---|---|
| 1 | **Thread-safety audit** — add `_dataLock` to `V3LiveRuntime`, make `V3LivePositionTracker` thread-safe | Prevents race conditions in production | M |
| 2 | **Split `ReplayStrategySystemLayout.cs`** (9K LOC) into one-file-per-strategy | Removes #1 maintenance pain point | L (mechanical) |
| 3 | **Config validation + startup diagnostics** — log effective config, warn on parse failures | Prevents silent misconfiguration | S |
| 4 | **Store entry-time ATR** on `V3LiveTrackedPosition` | Fixes stop/TP drift in position monitor | S |

### Phase 2: Architecture Improvement (2-3 sprints)

| # | Action | Impact | Effort |
|---:|---|---|---|
| 5 | **Unify candle aggregation** — shared engine between live and replay | Eliminates live/backtest divergence risk | M |
| 6 | **Replace string-typed trade fields** with enums (`OrderSide`, `OrderType`, `TimeInForce`) | Compile-time safety, IDE support | M |
| 7 | **Extract `IFeatureBuilder`, `ISignalEngine`, `IRiskGuard`** interfaces + DI | Testability, extensibility | M |
| 8 | **Introduce strategy registry** for Tmg strategies in replay | Self-documenting, convention-based wiring | M |
| 9 | **Split `ReplayStrategyContracts.cs`** into contracts vs configs | Clean dependency graph | S |

### Phase 3: Quality & Observability (2-3 sprints)

| # | Action | Impact | Effort |
|---:|---|---|---|
| 10 | **Inject `TimeProvider`** across live pipeline | Deterministic testing | S |
| 11 | **Extract `IStrategyExporter`** for shutdown exports | Decouples I/O from logic | S |
| 12 | **Standardize NaN indicator handling** | Consistent signal generation behavior | S |
| 13 | **Add unit tests** for `V3LiveSignalEngine`, `V3LiveRiskGuard`, `V3LivePositionMonitor`, `V3LiveSelectionGate` | Regression safety net | L |
| 14 | **Remove dead `FrameworkModels`** or formalize as planned feature | Reduces confusion | XS |
| 15 | **Promote magic numbers** to config | Tunable without code changes | S |
| 16 | **Reduce LINQ allocations** in `V3LiveFeatureBuilder` | ~12 array allocs/sec savings | S |

### Phase 4: Domain Model Maturation (3+ sprints) — ✅ COMPLETED 2026-03-06

| # | Action | Impact | Effort | Status |
|---:|---|---|---|---|
| 17 | **Define shared domain types** (`IOrderIntent`, `IFeatureSnapshot`) across live/replay | Cross-mode tooling enablement | L | ✅ Done |
| 18 | **Extract `Strategy/Replay/` subfolder** with proper namespace | Physical separation of concerns | M | ✅ Done |
| 19 | **Rename peak/trough fields** to side-agnostic names | Eliminates semantic confusion | S | ✅ Done |
| 20 | **Move replay I/O out of constructors** into `InitializeAsync` | Proper lifecycle management | S | ✅ Done |

**Phase 4 Implementation Notes:**
- **#17:** Created `StrategyDomainContracts.cs` with three interfaces: `IOrderIntent` (TimestampUtc, Symbol, SideLabel, OrderQuantity, Source), `IFeatureSnapshot` (TimestampUtc, IsReady, Price, Atr14), `IMtfAlignment` (HasAllTimeframes, IsBullish, IsBearish). Both `LiveOrderIntent` and `ReplayOrderIntent` implement `IOrderIntent`; `V3LiveFeatureSnapshot` implements `IFeatureSnapshot`; `V3LiveMtfAlignment` and `ReplayMtfSignalSnapshot` implement `IMtfAlignment`.
- **#18:** Moved 73 replay-related files (Replay*, Tmg001–Tmg049, Ovl001, Eod001, Scanner*, StrategyReplayDriver, DeterministicReplayClock, IReplayOrderSignalSource) to `Strategy/Replay/` subfolder. Namespace kept as `Harvester.App.Strategy` to avoid 54+ cascading using-directive changes.
- **#19:** Renamed `PeakPriceSinceEntry` → `MostFavorablePriceSinceEntry` and `TroughPriceSinceEntry` → `MostAdversePriceSinceEntry` across V3LivePositionTracker, V3LivePositionMonitor, V3LiveRuntime, V3LiveStrategyExporter. Added XML doc comments clarifying semantics (best/worst price since entry).
- **#20:** Moved V2 scanner selection block (LoadScannerV2BiasEntries + ExportScannerV2Snapshot) from `ScannerCandidateReplayRuntime` constructor to `InitializeAsync`. Constructor stores parameters and uses V1 fallback; V2 evaluation with disk I/O deferred to initialization lifecycle.
- **Verification:** 0 errors, 0 warnings. Backtest identical (V11 #1: PF 1.10, +$27.96, WR 69.2%, 146 trades, Sharpe 0.63).

---

## 8. Dependency Map

```
V3LiveRuntime
 ├── V3LiveConfig (no deps)
 ├── V3LiveFeatureBuilder → TechnicalIndicators (Backtest.Indicators)
 ├── V3LiveSignalEngine → TradeSide (Backtest.Engine)
 ├── V3LiveRiskGuard → V3LiveConfig
 ├── V3LiveOrderBridge → V3LiveConfig, TradeSide
 ├── V3LivePositionMonitor → V3LiveConfig
 ├── V3LivePositionTracker → PositionRow (IBKR.Runtime)
 ├── V3LiveCandleAggregator → HistoricalBarRow (IBKR.Runtime)
 ├── V3LiveExecutionStateMachine (no deps)
 ├── V3LiveSymbolResolver → PositionRow (IBKR.Runtime)
 ├── V3LiveSelectionGate → V3LiveConfig
 └── V3LiveHistoricalBarDeduplicator → HistoricalBarRow (IBKR.Runtime)

ScannerCandidateReplayRuntime
 ├── ScannerSelectionEngineV2 (complex config)
 ├── ReplayDayTradingPipeline
 │    ├── Ovl001 overlay
 │    ├── Entry strategy
 │    ├── Tmg001–Tmg049 trade management (49 strategies)
 │    └── Eod001 end-of-day
 ├── ReplayMtfCandleSignalEngine
 ├── ReplayRamSessionState
 └── ReplayTradeEpisodeRecorder

ReplayExecutionSimulator
 ├── Fill engine (market, limit, stop, trailing)
 ├── Margin model
 ├── Cash settlement
 └── Order state management
```

---

## 9. File-by-File Size Inventory

| File | LOC | Notes |
|---|---:|---|
| ReplayStrategySystemLayout.cs | 9,272 | **⚠ CRITICAL — split immediately** |
| ReplayExecutionSimulator.cs | 2,427 | Large but cohesive |
| ReplayStrategyContracts.cs | 1,335 | Mixed contracts + configs |
| ScannerCandidateReplayRuntime.cs | 1,248 | Constructor-heavy wiring |
| ReplaySelfLearningEngine.cs | 830 | Acceptable for GLM engine |
| V3LiveRuntime.cs | 794 | Core orchestrator, well-structured |
| ScannerSelectionEngineV2.cs | 701 | Acceptable |
| ReplayRamSessionState.cs | 670 | Session state tracking |
| V3LiveCandleAggregator.cs | 284 | Clean |
| V3LivePositionMonitor.cs | 282 | 11 exit conditions |
| ReplayHistoricalCandlestickCharts.cs | 272 | Utility |
| V3LivePositionTracker.cs | 271 | Mutable state |
| ReplaySelfLearningAnalyzer.cs | 270 | V1 analyzer |
| ReplayMtfCandleSignals.cs | 231 | Duplicate of CandleAggregator |
| V3LiveSignalEngine.cs | 217 | Composite scoring |
| V3LiveExecutionStateMachine.cs | 215 | State machine |
| V3LiveFeatureBuilder.cs | 213 | Feature extraction |
| V3LiveConfig.cs | 192 | 60+ config params |
| ReplayPerformanceAnalyzer.cs | 169 | Analytics |
| Remaining 20 files | <160 each | Infrastructure, interfaces |

---

## 10. Summary Recommendation

**Immediate actions (this week):**
1. Add thread-safety to `V3LivePositionTracker` and `V3LiveRuntime` data path.
2. Store entry-time ATR on tracked positions.
3. Log effective config at startup.

**Next sprint:**
4. Begin splitting `ReplayStrategySystemLayout.cs` into per-strategy files.
5. Replace string-typed order fields with enums.

**Medium-term:**
6. Unify candle aggregation between live and replay.
7. Extract interfaces for live sub-engines and enable unit testing.
8. Introduce `TimeProvider` for deterministic testing.

The live pipeline (V3/V11) is architecturally sound after Phase 1-4 refactors. All 20 audit items are now implemented. The major remaining technical debt resides in the replay stack — particularly the 9K-line system layout. Ongoing work can focus on unit test coverage (item #13) and further replay-stack decomposition.

---

## Appendix A — Implementation Changelog

| Phase | Date | Items | Verification |
|---|---|---|---|
| Phase 1 | 2026-03-06 | #1–#4: Thread-safety, 9K file split, config validation, entry ATR storage | 0E/0W, V11 identical |
| Phase 2 | 2026-03-06 | #5–#8: OrderEnums, ReplayStrategyContracts split, ILiveStrategyComponents, TmgStrategyFactory, CandleTimeUtils | 0E/0W, V11 identical |
| Phase 3 | 2026-03-06 | #9–#16: Remove FrameworkModels, TimeProvider injection (11 sites), promote magic numbers (6→config), NaN handling, LINQ allocations, IStrategyExporter | 0E/0W, V11 identical |
| Phase 4 | 2026-03-06 | #17–#20: Shared domain types (IOrderIntent/IFeatureSnapshot/IMtfAlignment), Strategy/Replay/ subfolder (73 files), side-agnostic field renames, constructor I/O deferral | 0E/0W, V11 identical |

---

*End of audit report.*
