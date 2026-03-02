# Harvester C# Application — Complete Technical Documentation

## 1. Overview

**Harvester.App** is a monolithic .NET 9.0 console application that provides a comprehensive day-trading infrastructure connecting to Interactive Brokers TWS via the IBApi socket protocol. It encompasses live market data, order management, strategy replay/backtesting, self-learning ML, scanner-based candidate selection, and full execution simulation.

| Property | Value |
|----------|-------|
| **Framework** | .NET 9.0 |
| **Output Type** | Console Exe |
| **Solution** | `Harvester.sln` (repo root) |
| **Project** | `src/Harvester.App/Harvester.App.csproj` |
| **NuGet Dependencies** | IBApi 1.0.0-preview-975, ClosedXML 0.104.2 |
| **Source Files** | 48 files, ~30,192 lines |
| **Architecture** | Monolithic - no DI container, no NATS, no REST/gRPC |

---

## 2. Entry Point & Execution Flow

### `Program.cs` (20 lines)
```
CLI args → AppOptions.Parse(args)
    → if StrategyReplay mode + scanner path → ScannerCandidateReplayRuntime
    → SnapshotRuntime(options, strategyRuntime?)
    → runtime.RunAsync()
    → Environment.Exit(exitCode)
```

**AppOptions** (defined inside `SnapshotRuntime.cs`): Parses all CLI arguments including symbol, mode, account, replay input path, scanner parameters, and env-var overrides for strategy configuration.

**RunMode Enum**: 50+ modes covering every IBKR API capability:
- Connection & account: `Connect`, `ManagedAccounts`, `AccountUpdates`, `AccountSummaryOnly`
- Market data: `TopData`, `MarketDepth`, `RealtimeBars`, `MarketDataAll`
- Historical: `HistoricalBars`, `HistoricalBarsKeepUpToDate`, `HistoricalTicks`, `HeadTimestamp`, `Histogram`
- Orders: `Orders*`, `OrdersDryRun`, `OrdersPlaceSim`, `OrdersCancelSim`, `OrdersWhatIf`
- Positions: `Positions`, `PositionsMonitor*`, `PositionsAutoReplaceScanLoop`
- Options: `OptionChains`, `OptionExercise`, `OptionGreeks`
- Crypto: `CryptoPermissions`, `CryptoContract`, `CryptoStreaming`, `CryptoHistorical`, `CryptoOrder`
- FA: `FaAllocationGroups`, `FaGroupsProfiles`, `FaUnification`, `FaModelPortfolios`, `FaOrder`
- Scanner: `ScannerExamples`, `ScannerComplex`, `ScannerParameters`, `ScannerWorkbench`, `ScannerPreview`
- Strategy: `StrategyReplay`
- Other: `FundamentalData`, `DisplayGroups*`, `ErrorCodes`

---

## 3. Directory Structure

```
src/Harvester.App/
├── Program.cs                          # Entry point (20L)
├── Harvester.App.csproj                # Project file
├── Historical/
│   └── HistoricalIngestionContracts.cs  # Pipeline pattern for historical data (88L)
├── IBKR/
│   ├── Connection/
│   │   ├── IbConnectionState.cs        # Enum: Disconnected/Connecting/Connected/Failed (7L)
│   │   └── IbkrSession.cs             # TWS socket lifecycle, reconnect with backoff (144L)
│   ├── Wrapper/
│   │   └── HarvesterEWrapper.cs        # Base TWS callback handler (38L)
│   ├── Contracts/
│   │   └── ContractFactory.cs          # Static builders: Stock/Forex/Future/Option/etc (104L)
│   ├── Orders/
│   │   └── OrderFactory.cs             # Static builders: Market/Limit/Stop/Bracket/Algo (282L)
│   ├── Broker/
│   │   ├── IBrokerAdapter.cs           # Interface + 5 record types (162L)
│   │   ├── IbBrokerAdapter.cs          # Concrete IBApi impl (344L)
│   │   ├── IbContractNormalizationService.cs  # Symbol/OCC normalization (269L)
│   │   ├── IbOrderTranslationService.cs       # Order intent → IBApi Order (179L)
│   │   └── IbHistoricalIngestionAdapters.cs   # Bar extractors (73L)
│   ├── Risk/
│   │   ├── PreTradeCostRiskEstimator.cs # Commission+slippage estimation (86L)
│   │   ├── PreTradeControlDsl.cs        # Rule engine: notional/qty/time gates (130L)
│   │   └── FaRoutingValidator.cs        # FA model routing checks (56L)
│   └── Runtime/
│       ├── SnapshotRuntime.cs           # GOD CLASS — 50+ run modes (9,616L)
│       ├── SnapshotEWrapper.cs          # TWS callback impl, 35+ TCS, 30+ queues (1,126L)
│       ├── IbErrorPolicy.cs             # API error classification (95L)
│       ├── RequestRegistry.cs           # Correlation-based request tracking (72L)
│       ├── OrderReconciliation.cs       # open/completed/exec/comm reconciliation (225L)
│       ├── OrderLifecycleModel.cs       # Order state machine (264L)
│       ├── L2CandlestickBuilder.cs      # Mid-price candles from L2 depth (198L)
│       ├── L2MtfSignalStrategy.cs       # Multi-timeframe L2 signals (114L)
│       └── RuntimeStateStore.cs         # SHA256-checksummed state persistence (183L)
└── Strategy/
    ├── IStrategyRuntime.cs              # Pluggable strategy interface (8L)
    ├── IStrategyEventScheduler.cs       # Scheduled event interface (5L)
    ├── IReplayOrderSignalSource.cs      # Replay signal source interface (5L)
    ├── IExchangeCalendarService.cs      # Calendar interface + ExchangeSessionWindow (12L)
    ├── FrameworkModels.cs               # Alpha/Portfolio/Risk/Execution pipeline (57L)
    ├── StrategyRuntimeContracts.cs      # Context + DataSlice records (26L)
    ├── NullStrategyRuntime.cs           # No-op implementation (20L)
    ├── DeterministicReplayClock.cs      # Monotonic replay clock (18L)
    ├── DeterministicStrategyEventScheduler.cs  # Event scheduler (113L)
    ├── UsEquitiesExchangeCalendarService.cs    # US market calendar (123L)
    ├── StrategyReplayDriver.cs          # JSON dataset → DataSlice loader (105L)
    ├── ReplayStrategySystemLayout.cs    # 49 Tmg strategies + configs (9,431L)
    ├── ReplayExecutionSimulator.cs      # Full execution simulation (2,427L)
    ├── ReplayPerformanceAnalyzer.cs     # Performance metrics (170L)
    ├── ReplaySelfLearningAnalyzer.cs    # V1: 6-feature logistic regression (231L)
    ├── ReplaySelfLearningEngine.cs      # V2: 16-feature GLM, AdaGrad, Kelly (722L)
    ├── ScannerSelectionEngineV2.cs      # 8-factor composite scoring (613L)
    ├── ScannerCandidateReplayRuntime.cs # Primary IStrategyRuntime for replay (1,139L)
    ├── ReplayRamSessionState.cs         # Per-symbol session state + microstructure (670L)
    ├── ReplayMtfCandleSignals.cs        # MTF candlestick signal engine (199L)
    ├── ReplayCorporateActionsEngine.cs  # Split/dividend normalization (118L)
    ├── ReplayFinancingEngine.cs         # Borrow/locate cost engine (93L)
    ├── ReplaySymbolEventsEngine.cs      # Ticker rename/delist handling (135L)
    └── ReplayHistoricalCandlestickCharts.cs  # MTF chart builder (244L)
```

---

## 4. Core Architectural Concepts

### 4.1 Data Model — Sealed Records Everywhere
The entire domain model uses C# `sealed record` types (80+ types). This provides:
- Immutability (value semantics)
- Built-in `Equals`, `GetHashCode`, `ToString`
- Non-destructive mutation via `with` expressions
- Pattern matching compatibility

Key record families:
- **Market data**: `TopTickRow`, `DepthRow`, `RealtimeBarRow`, `HistoricalBarRow`
- **Orders**: `ReplayOrderIntent`, `ReplayFillRow`, `CanonicalOrderEvent`
- **Portfolio**: `ReplayPortfolioRow`, `PositionRow`
- **Performance**: `ReplayPerformancePacketRow`, `ReplayPerformanceSummaryRow`
- **Strategy**: `AlphaInsight`, `PortfolioTarget`, `StrategyRuntimeContext`, `StrategyDataSlice`

### 4.2 Strategy Pipeline (Lean/QuantConnect-inspired)
```
IAlphaModel          → generates AlphaInsight (direction + confidence)
IPortfolioConstructionModel → converts insights → PortfolioTarget (quantity)
IRiskManagementModel → adjusts targets for risk
IExecutionModel      → converts targets → ReplayOrderIntent (actual orders)
```
Currently null-implemented, with the active system using the replay pipeline:
```
Overlay (Ovl001)  → global safety (flatten caps)
Entry (SingleShot) → scanner-based signal generation
Management (Tmg001-049) → per-trade trailing/stops/targets
EOD (Eod001)      → force-flat before market close
```

### 4.3 Replay/Backtest Architecture
```
StrategyReplayDriver.LoadSlices(JSON) → StrategyDataSlice[]
    ↓
For each slice:
    ScannerCandidateReplayRuntime.GetReplayOrderIntents(slice)
        ↓
    ReplayDayTradingPipeline:
        1. Overlay safety check
        2. Entry signal generation
        3. Trade management (Tmg strategies)
        4. EOD flatten
        ↓ returns ReplayOrderIntent[]
    ReplayExecutionSimulator.Simulate(intents, portfolio, slice)
        ↓ returns fills, portfolio, trailing updates, etc.
    ReplayPerformanceAnalyzer.Analyze(slices, fills, portfolio)
```

### 4.4 Configuration
- **CLI args** parsed by `AppOptions.Parse(args)` in `SnapshotRuntime.cs`
- **Environment variables** for strategy Tmg configs (e.g., `REPLAY_TMG_001_PROFIT_TARGET_BPS`)
- **No config files** (appsettings.json or similar)
- **No DI container** — all dependencies manually wired

### 4.5 IOBrokerAdapter Abstraction
The `IBrokerAdapter` interface (40+ methods) abstracts ALL TWS interactions:
- Contract building (`BuildContract`)
- Order translation (`BuildOrder`, `TranslateOrderStatus`)
- Market data (`RequestMarketData`, `RequestMarketDepth`)
- Historical data (`RequestHistoricalData`)
- Account (`RequestAccountSummary`, `RequestPositions`)
- Placement (`PlaceOrder`, `CancelOrder`)

This enables testing and potential broker substitution.

---

## 5. Key Components Detail

### 5.1 SnapshotRuntime.cs (9,616 lines) — The God Class

Central orchestrator that owns:
- **IBKR connection** via `IbkrSession` + `SnapshotEWrapper`
- **Error policy** via `IbErrorPolicy`
- **Request tracking** via `RequestRegistry`
- **Pre-trade controls** via `PreTradeControlDsl` + `PreTradeCostRiskEstimator`
- **Strategy runtime** via `IStrategyRuntime` (pluggable)
- **State persistence** via `RuntimeStateStore`

`RunAsync()` lifecycle:
1. Restore crash-recovery checkpoint
2. Connect to TWS (host/port/clientId)
3. Verify connection + clock skew check
4. Enter SteadyState lifecycle
5. Initialize strategy runtime
6. Dispatch to mode-specific handler
7. Persist state + export artifacts (JSON/XLSX)

### 5.2 ReplayStrategySystemLayout.cs (9,431 lines) — 49 Strategies

Contains the complete trade management strategy library:

| Strategy Range | Category | Key Feature |
|---------------|----------|-------------|
| Tmg001-005 | Bracket/Trail/BE | Basic stop+target+trail+breakeven |
| Tmg006-010 | Momentum | ATR scaling, R-multiple exits |
| Tmg011-015 | Mean-reversion | VWAP reversion, Bollinger, Keltner |
| Tmg016-020 | Composite | Multi-indicator scoring, OFI |
| Tmg021-025 | Scalp/Micro-trail | Tight cents-based trailing |
| Tmg026-030 | Swing/Multi-session | Multi-day holds, overnight |
| Tmg031-035 | Options-aware | Delta/gamma hedging concepts |
| Tmg036-040 | Adaptive/ML-driven | Self-learning weight adjustment |
| Tmg041-045 | Volume/L2 depth | Order flow, depth imbalance |
| Tmg046-049 | MTF regime/ATR | Multi-timeframe regime detection |

Each Tmg strategy:
1. Has a `TmgXXXConfig` record (configurable parameters)
2. Implements `IReplayTradeManagementStrategy`
3. Returns `ReplayOrderIntent[]` for stop/target adjustments
4. Is instantiated in `ScannerCandidateReplayRuntime` constructor

### 5.3 ReplayExecutionSimulator.cs (2,427 lines)

Full market simulation engine:
- **Order types**: Market, Limit, Stop, StopLimit, TrailingStop
- **Fill model**: Configurable slippage + commission (per-share or BPS)
- **Partial fills**: Volume-proportional progression
- **OCO groups**: Auto-cancel linked legs on fill
- **Bracket orders**: Parent → child activation chains
- **Combo legs**: Multi-leg instrument simulation
- **Trailing stops**: Dynamic re-anchor on favorable moves
- **Margin**: Initial/maintenance margin checks with margin calls
- **Settlement**: T+n cash settlement with rejection on insufficient funds
- **Corporate actions**: Split ratio adjustment + cash dividends
- **Delist**: Forced close at terminal price
- **Financing**: Short borrow cost accrual

### 5.4 Self-Learning Engines

**V1** (`ReplaySelfLearningAnalyzer.cs`, 231 lines):
- 6 features: intercept, side_sign, commission_bps, slippage_bps, period_return_bps, drawdown_bps
- Logistic regression with SGD
- Platt scaling calibration

**V2** (`ReplaySelfLearningEngine.cs`, 722 lines):
- 16 features: Z-score normalized
- AdaGrad optimizer with L2 regularization
- Walk-forward temporal split validation
- Feature importance ranking + per-prediction contribution
- Confidence-weighted symbol bias store with exponential decay
- Kelly criterion position sizing
- Time-of-day cyclical encoding (sin/cos)
- Metrics: Brier score, LogLoss

### 5.5 ScannerSelectionEngineV2.cs (613 lines)

8-factor composite scoring for scanner candidate ranking:
1. Volume score
2. Spread gate
3. L2 depth score
4. Momentum score
5. Symbol bias (from self-learning)
6. Time-of-day factor
7. Sector diversification
8. Historical win-rate adjustment

Input: `ScannerV2CandidateFileRow` + L1/L2 snapshots → Output: `ScannerV2RankedCandidate[]`

### 5.6 ReplayRamSessionState.cs (670 lines)

Per-symbol in-memory state tracking:
- 1-minute bar history (rolling window)
- Order book state (bid/ask levels)
- Microstructure buckets: spread, L2 imbalance, tape volume (buy/sell), volatility
- Trade episode recording with full lifecycle (entry, fills, exit, MFE/MAE, R-multiple, decision trace)

---

## 6. Data Flow Diagrams

### Live Trading Mode
```
IBKR TWS ←→ IbkrSession ←→ SnapshotEWrapper (callbacks)
                                    ↓
                            SnapshotRuntime.RunXxxMode()
                                    ↓
                            IBrokerAdapter.PlaceOrder() → TWS
```

### Strategy Replay Mode
```
JSON File → StrategyReplayDriver.LoadSlices()
    → ReplayCorporateActionsEngine.NormalizeRows()
    → StrategyDataSlice[] (synthetic TopTick + HistoricalBar)
    ↓
ScannerCandidateReplayRuntime.OnDataAsync(slice)
    → ScannerSelectionEngineV2.Evaluate() (rank candidates)
    → ReplayDayTradingPipeline:
        → Ovl001 overlay safety
        → Entry signal
        → Tmg001-049 management
        → Eod001 EOD flatten
    → ReplayOrderIntent[]
    ↓
ReplayExecutionSimulator.Simulate(intents, portfolio)
    → ReplayFillRow[], ReplayPortfolioRow, trailing updates, etc.
    ↓
ReplayPerformanceAnalyzer.Analyze()
    → Sharpe, win rate, drawdown, alpha, turnover
    ↓
ReplaySelfLearningEngine.Train()
    → Kelly sizing, bias updates, feature importance
```

---

## 7. External Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| IBApi | 1.0.0-preview-975 | Interactive Brokers TWS socket API |
| ClosedXML | 0.104.2 | Excel XLSX export for artifacts |

No other external libraries. All indicators, ML, simulation, and analysis are implemented from scratch.

---

## 8. Build & Run

```bash
# Build
dotnet build src/Harvester.App/Harvester.App.csproj

# Run examples
dotnet run --project src/Harvester.App -- --mode Connect --host 127.0.0.1 --port 7497
dotnet run --project src/Harvester.App -- --mode StrategyReplay --replay-input data/replay.json --symbol AAPL
dotnet run --project src/Harvester.App -- --mode TopData --symbol TSLA
```
