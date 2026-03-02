# Harvester C# Codebase — Comprehensive Analysis

> **Generated**: 2026-03-01  
> **Scope**: `src/Harvester.App/` — single .NET 9.0 monolithic console application  
> **Total**: 48 source files · 30,192 lines of C#

---

## 1. Project Structure & Build

### 1.1 Solution

| Item | Value |
|------|-------|
| Solution | `Harvester.sln` |
| Projects | 1 — `src/Harvester.App/Harvester.App.csproj` |
| Target Framework | `net9.0` |
| Output Type | Console (`Exe`) |
| ImplicitUsings | Enabled |
| Nullable | Enabled |

### 1.2 NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `ClosedXML` | 0.104.2 | Excel file generation for artifact exports |
| `IBApi` | 1.0.0-preview-975 | Interactive Brokers TWS API client |

### 1.3 Directory Layout

```
src/Harvester.App/
├── Program.cs                          # Entry point (20 lines)
├── IBKR/                              # Interactive Brokers integration
│   ├── Broker/                        # Adapter & translation layer
│   │   ├── IBrokerAdapter.cs          # Abstraction interface (162 lines)
│   │   ├── IbBrokerAdapter.cs         # Concrete EClientSocket wrapper (344 lines)
│   │   ├── IbContractNormalizationService.cs  # Contract builder & normalizer (269 lines)
│   │   ├── IbHistoricalIngestionAdapters.cs   # Bar normalization (73 lines)
│   │   └── IbOrderTranslationService.cs       # Intent→Order translation (179 lines)
│   ├── Connection/                    # Session lifecycle
│   │   ├── IbConnectionState.cs       # State enum (16 lines)
│   │   └── IbkrSession.cs            # Connection state machine (144 lines)
│   ├── Contracts/                     # Contract factory
│   │   └── ContractFactory.cs         # Static factory for all asset types (104 lines)
│   ├── Orders/                        # Order construction
│   │   └── OrderFactory.cs            # 20+ order type factory methods (282 lines)
│   ├── Risk/                          # Pre-trade risk controls
│   │   ├── FaRoutingValidator.cs      # FA order routing validation (56 lines)
│   │   ├── PreTradeCostRiskEstimator.cs  # Cost estimation profiles (86 lines)
│   │   └── PreTradeControlDsl.cs      # DSL-based risk policy engine (130 lines)
│   ├── Runtime/                       # Core runtime engine
│   │   ├── IbErrorPolicy.cs           # API error classification (95 lines)
│   │   ├── L2CandlestickBuilder.cs    # OHLC from L2 depth (198 lines)
│   │   ├── L2MtfSignalStrategy.cs     # MTF signal generation (114 lines)
│   │   ├── OrderLifecycleModel.cs     # Order state machine (264 lines)
│   │   ├── OrderReconciliation.cs     # Order/execution reconciliation (225 lines)
│   │   ├── RequestRegistry.cs         # API request tracking (72 lines)
│   │   ├── RuntimeStateStore.cs       # Checkpoint persistence (183 lines)
│   │   ├── SnapshotEWrapper.cs        # EWrapper callback handler (1,126 lines)
│   │   └── SnapshotRuntime.cs         # ★ Main runtime orchestrator (9,616 lines)
│   └── Wrapper/
│       └── HarvesterEWrapper.cs       # Base EWrapper (80 lines)
├── Historical/                        # Data ingestion pipeline
│   └── HistoricalIngestionContracts.cs  # ETL pipeline interfaces (74 lines)
└── Strategy/                          # Replay & backtesting engine
    ├── DeterministicReplayClock.cs     # Forward-only replay clock (18 lines)
    ├── DeterministicStrategyEventScheduler.cs  # Scheduled event emitter (113 lines)
    ├── FrameworkModels.cs             # Alpha/Portfolio/Risk/Execution model interfaces (57 lines)
    ├── IExchangeCalendarService.cs    # Calendar interface (12 lines)
    ├── IReplayOrderSignalSource.cs    # Order intent source interface (5 lines)
    ├── IStrategyEventScheduler.cs     # Scheduler interface (5 lines)
    ├── IStrategyRuntime.cs            # Strategy lifecycle interface (8 lines)
    ├── NullStrategyRuntime.cs         # No-op strategy (20 lines)
    ├── ReplayCorporateActionsEngine.cs  # Split/action normalization (118 lines)
    ├── ReplayExecutionSimulator.cs    # ★ Full trade simulation (2,142 lines)
    ├── ReplayFinancingEngine.cs       # Borrow/locate financing (93 lines)
    ├── ReplayHistoricalCandlestickCharts.cs  # Chart builder (244 lines)
    ├── ReplayMtfCandleSignals.cs      # MTF candle signal engine (199 lines)
    ├── ReplayPerformanceAnalyzer.cs   # Performance analytics (146 lines)
    ├── ReplayRamSessionState.cs       # In-memory microstructure state (575 lines)
    ├── ReplaySelfLearningAnalyzer.cs  # V1 logistic regression ML (231 lines)
    ├── ReplaySelfLearningEngine.cs    # V2 GLM with AdaGrad ML (722 lines)
    ├── ReplayStrategySystemLayout.cs  # ★ All strategy config records (9,431 lines)
    ├── ReplaySymbolEventsEngine.cs    # Symbol mapping/delist events (135 lines)
    ├── ScannerCandidateReplayRuntime.cs  # ★ Main strategy runtime (1,139 lines)
    ├── ScannerSelectionEngineV2.cs    # Multi-factor scanner scoring (613 lines)
    ├── StrategyReplayDriver.cs        # JSON dataset loader (105 lines)
    ├── StrategyRuntimeContracts.cs    # Core data contracts (26 lines)
    └── UsEquitiesExchangeCalendarService.cs  # US market calendar (123 lines)
```

---

## 2. Entry Point & CLI

**File**: `Program.cs` (20 lines)

```
args → AppOptions.Parse(args) → Mode dispatch
  ├── StrategyReplay mode?  → ScannerCandidateReplayRuntime
  └── All other modes       → SnapshotRuntime.RunAsync()
                              → Environment.Exit(returnCode)
```

- **No `appsettings.json`** — all configuration via CLI arguments parsed by `AppOptions.Parse(args)`
- **No dependency injection container** — direct instantiation
- Exit codes: `0` = success, `1` = blocking errors/gate failures, `2` = timeout/exception

---

## 3. Architecture Overview

### 3.1 High-Level Data Flow

```
                        ┌─────────────────────────────────────────┐
                        │            Program.cs                    │
                        │         AppOptions.Parse(args)           │
                        └───────────┬──────────┬──────────────────┘
                                    │          │
                    ┌───────────────▼──┐   ┌───▼──────────────────┐
                    │ SnapshotRuntime   │   │ ScannerCandidate     │
                    │ (9,616 lines)    │   │ ReplayRuntime        │
                    │ 40+ RunModes     │   │ (Backtesting)        │
                    └──────┬───────────┘   └──────┬───────────────┘
                           │                      │
        ┌──────────────────▼──────────┐   ┌───────▼──────────────┐
        │        IBKR Layer           │   │   Strategy Layer      │
        │ ┌────────────┐ ┌──────────┐ │   │ ┌──────────────────┐ │
        │ │IbkrSession │ │EWrapper  │ │   │ │ Execution Sim    │ │
        │ │ConnectionSM│ │Callbacks │ │   │ │ Self-Learning    │ │
        │ └──────┬─────┘ └────┬─────┘ │   │ │ Scanner Engine   │ │
        │        │             │       │   │ │ Performance Anal │ │
        │ ┌──────▼─────────────▼─────┐ │   │ │ 49 Trade Mgmt   │ │
        │ │  IbBrokerAdapter          │ │   │ │ MTF Candles     │ │
        │ │  EClientSocket wrapper    │ │   │ └──────────────────┘ │
        │ └───────────────────────────┘ │   └──────────────────────┘
        │ ┌───────────────────────────┐ │
        │ │ Risk: PreTradeControls,   │ │
        │ │ CostEstimator, FA Valid.  │ │
        │ └───────────────────────────┘ │
        └─────────────────────────────────┘
```

### 3.2 Communication Pattern

| Mechanism | Used? | Details |
|-----------|-------|---------|
| IBKR TWS Socket | ✅ | Direct TCP via `EClientSocket` → TWS/Gateway |
| NATS | ❌ | Not present |
| REST/gRPC | ❌ | Not present |
| Message Bus | ❌ | Not present |
| File I/O | ✅ | JSON artifacts, Excel exports via ClosedXML |

The application communicates **exclusively** via:
1. **IBKR TWS API** — socket connection to Trader Workstation or IB Gateway
2. **File system** — JSON checkpoint state, Excel exports, replay data files

---

## 4. Module Deep Dive

### 4.1 IBKR/Runtime — Core Runtime Engine

#### `SnapshotRuntime.cs` ★ (9,616 lines)

The heart of the application — a massive runtime orchestrator managing the entire IBKR session lifecycle.

**Lifecycle State Machine**:
```
Startup → SteadyState → Shutdown | Halted
```

**40+ RunModes** organized by category:

| Category | Modes |
|----------|-------|
| **Connection** | `Connect` |
| **Orders** | `Orders`, `OrdersAllOpen`, `OrdersDryRun`, `OrdersPlaceSim`, `OrdersCancelSim`, `OrdersWhatIf` |
| **Positions** | `Positions`, `PositionsMonitor1Pct` |
| **Market Data** | `SnapshotAll`, `TopData`, `MarketDepth`, `RealtimeBars`, `MarketDataAll` |
| **Historical** | `HistoricalBars`, `HistoricalBarsKeepUpToDate`, `Histogram`, `HistoricalTicks`, `HeadTimestamp` |
| **Account** | `ManagedAccounts`, `FamilyCodes`, `AccountUpdates`, `AccountSummaryOnly`, `PnlAccount`, `PnlSingle` |
| **Options** | `OptionChains`, `OptionExercise`, `OptionGreeks` |
| **Crypto** | `CryptoPermissions`, `CryptoContract`, `CryptoStreaming`, `CryptoHistorical`, `CryptoOrder` |
| **FA** | `FaAllocationGroups`, `FaGroupsProfiles`, `FaUnification`, `FaModelPortfolios`, `FaOrder` |
| **Fundamental** | `FundamentalData`, `WshFilters` |
| **Scanner** | `ScannerExamples`, `ScannerComplex`, `ScannerParameters`, `ScannerWorkbench`, `ScannerPreview` |
| **Display** | `DisplayGroups*` |
| **Strategy** | `StrategyReplay` |
| **Contracts** | `ContractsValidate` |
| **Diagnostics** | `ErrorCodes` |

**Key Capabilities**:
- Heartbeat monitoring with clock skew detection
- Automatic reconnection with backoff ladder
- Runtime state checkpointing with SHA256 integrity verification
- Order reconciliation engine
- Pre-trade control gates
- Adapter tracing for all IBKR API calls
- Strategy scheduler event dispatch

#### `SnapshotEWrapper.cs` (1,126 lines)

Implements the IBKR `EWrapper` interface — receives all asynchronous callbacks from the TWS API.

**Data Collection Architecture**: Uses `TaskCompletionSource<bool>` (30+ instances) for async coordination and `ConcurrentQueue<T>` (25+ instances) for thread-safe data buffering:

| Queue | Data Type |
|-------|-----------|
| `AccountSummaryRows` | Account summary data |
| `OpenOrderRows` | Open orders |
| `CompletedOrderRows` | Completed orders |
| `ExecutionRows` | Trade executions |
| `CommissionRows` | Commission reports |
| `PositionRows` | Portfolio positions |
| `TopTickRows` | L1 market data |
| `DepthRows` | L2 market depth |
| `RealtimeBarRows` | 5s real-time bars |
| `HistoricalBarRows` | Historical OHLCV |
| `HistogramRows` | Price histograms |
| `HistoricalTickRows` | Tick-level data |
| `FamilyCodeRows` | Family codes |
| `PnlRows` | P&L data |
| `OptionChainRows` | Option chains |
| `OptionGreekRows` | Option greeks |
| `FaDataRows` | FA allocation data |
| `ScannerDataRows` | Scanner results |

**Data Sanitization**: Applies business rules to incoming market data:
- Filters invalid prices (≤ 0, NaN, infinity)
- Filters negative sizes
- Detects orphan size updates without corresponding price
- Normalizes delayed tick types to real-time field IDs
- Translates IBKR order events to canonical format

#### `RuntimeStateStore.cs` (183 lines)

Persists runtime checkpoint state to JSON with integrity verification.

```
RuntimeStateCheckpoint
├── Timestamp, Host, ProcessId
├── Stage (enum: Initializing/Starting/Running/ShuttingDown/Halted)
├── RuntimeLifecycleTransition history
└── SHA256 checksum verification
```

Features:
- Schema version tracking
- Quarantine for corrupt files (renamed to `.quarantine`)
- Version history within the checkpoint file

#### `RequestRegistry.cs` (72 lines)

Tracks all outstanding IBKR API requests with correlation IDs, deadlines, and status lifecycle: `Started → Completed | TimedOut | Failed | Cancelled`.

#### `OrderReconciliation.cs` (225 lines)

Reconciles four data streams into a canonical order ledger:
```
OpenOrders + CompletedOrders + Executions + Commissions → Reconciled Ledger
```

**Diagnostic Gap Types**:
- `execution_without_order` — execution with no matching order metadata
- `completed_without_open` — completed order never seen as open
- `execution_without_commission` — execution missing commission data
- `open_without_execution` — open order with no executions

**Coverage Metrics**: `ExecutionCommissionCoveragePct`, `ExecutionOrderMetadataCoveragePct`

#### `OrderLifecycleModel.cs` (264 lines)

State machine for order lifecycle with strict transition validation:
```
Accepted → Working → PartiallyFilled → Filled
                  → Canceled
                  → Rejected
                  → Inactive
```

Also classifies IBKR API errors into severity levels:
- `Ignored` — informational, no action
- `Warning` — logged but not blocking
- `Retryable` — can attempt recovery
- `NonBlocking` — functionality impacted but can continue
- `Blocking` — must halt

#### `L2CandlestickBuilder.cs` (198 lines)

Builds OHLC candlesticks from Level 2 market depth updates across configurable timeframes. Maintains full order book state, computes mid price and spread BPS at each update.

#### `L2MtfSignalStrategy.cs` (114 lines)

Multi-timeframe signal generation from L2 candlesticks:
- **Entry**: Requires bullish/bearish alignment across **6 timeframes** (1D, 1h, 15m, 5m, 1m, 30s)
- **Spread Gate**: Maximum 12 BPS spread for signal validity
- Uses 30s and 1m as trigger timeframes

#### `IbErrorPolicy.cs` (95 lines)

Classifies every IBKR API error code into an action:

| Action | Example Codes |
|--------|---------------|
| `Ignore` | 2100, 2103–2158, 10089 (informational messages) |
| `Warn` | Various non-critical warnings |
| `Retry` | 1100, 1101, 1102, 1300, 2110 (connectivity issues) |
| `HardFail` | Unrecognized or critical errors |

Mode-specific handling for OptionGreeks, FA, Scanner, and DisplayGroups modes.

---

### 4.2 IBKR/Broker — Broker Abstraction Layer

#### `IBrokerAdapter.cs` (162 lines)

Abstraction interface with **40+ methods** covering:
- Connection management (`Connect`, `Disconnect`, `IsConnected`)
- Market data (`ReqMktData`, `ReqMktDepthExchanges`, `ReqRealTimeBars`)
- Historical data (`ReqHistoricalData`, `ReqHistoricalTicks`, `ReqHeadTimestamp`)
- Orders (`PlaceOrder`, `CancelOrder`, `ReqAllOpenOrders`, `ReqCompletedOrders`)
- Account (`ReqAccountSummary`, `ReqPositions`, `ReqPnL`)
- Options (`ReqSecDefOptParams`, `ExerciseOptions`, `CalculateImpliedVolatility`)
- Scanner (`ReqScannerSubscription`, `ReqScannerParameters`)
- FA (`RequestFA`, `ReplaceFA`)

**Supporting Records**:
- `BrokerAdapterTrace` — audit trail for every API call (direction, method, timestamp, payload)
- `BrokerContractSpec` — normalized contract specification
- `BrokerOrderIntent` — abstract order intent with side, quantity, type, TIF, limits
- `CanonicalOrderEvent` — normalized order state change event
- `BrokerAssetType` enum — STK, OPT, FUT, CASH, CFD, IND, CRYPTO, BAG

#### `IbBrokerAdapter.cs` (344 lines)

Concrete implementation wrapping `EClientSocket` with:
- **Trace emission** for every API call (timestamped audit log)
- **Contract translation** via `IbContractNormalizationService`
- **Order translation** via `IbOrderTranslationService`
- Delegates all calls to underlying IBKR `EClientSocket`

#### `IbContractNormalizationService.cs` (269 lines)

Builds `IBApi.Contract` objects from `BrokerContractSpec`:
- Handles all asset types (STK, OPT, FUT, CASH, CFD, IND, CRYPTO, BAG)
- **OCC option symbol parsing**: e.g., `AAPL240621C00195000` → symbol, expiry, right, strike
- **Future expiry recovery**: extends partial date formats
- **Combo/bag legs**: multi-leg spread construction

#### `IbOrderTranslationService.cs` (179 lines)

Translates `BrokerOrderIntent` to `IBApi.Order`:
- Maps action (BUY/SELL/SSHORT), order type, TIF
- Handles combo leg limit price adjustments
- Normalizes partial fill handling
- Applies OCA (One-Cancels-All) group settings

#### `IbHistoricalIngestionAdapters.cs` (73 lines)

Normalizes raw IBKR historical bar data into `CanonicalHistoricalBar` format.

---

### 4.3 IBKR/Connection — Session Management

#### `IbkrSession.cs` (144 lines)

Connection state machine:
```
Disconnected → Connecting → Connected → Reconnecting → Connected
                         ↘ Degraded → Halting
```

- **Reconnect Ladder**: Progressive backoff on connection failures
- **EReader Thread**: Manages background message processing thread
- **Graceful Shutdown**: State-tracked disconnect sequence

#### `IbConnectionState.cs` (16 lines)

```csharp
enum IbConnectionState { Disconnected, Connecting, Connected, Reconnecting, Degraded, Halting }
record IbConnectionTransition(IbConnectionState From, IbConnectionState To, DateTime Timestamp, string? Reason);
```

---

### 4.4 IBKR/Contracts — Contract Factory

#### `ContractFactory.cs` (104 lines)

Static factory methods for `IBApi.Contract`:
- `Stock(symbol, exchange?, currency?)`
- `Forex(pair, exchange?)`
- `Future(symbol, expiry, exchange?, currency?)`
- `Option(symbol, expiry, strike, right, exchange?, currency?)`
- `CFD(symbol, exchange?, currency?)`
- `Index(symbol, exchange?, currency?)`
- `Crypto(symbol, exchange?, currency?)`
- `Bag(symbol, legs[], exchange?, currency?)` — combo/spread orders

---

### 4.5 IBKR/Orders — Order Construction

#### `OrderFactory.cs` (282 lines)

20+ static factory methods for every IBKR order type:

| Category | Methods |
|----------|---------|
| **Basic** | `Market`, `Limit`, `Stop`, `StopLimit` |
| **At Close** | `MarketOnClose`, `LimitOnClose` |
| **Trailing** | `TrailingStop`, `TrailingStopLimit` |
| **Bracket** | `Bracket` (parent + take profit + stop loss) |
| **Conditional** | `MarketIfTouched` |
| **Pegged** | `PeggedToMarket`, `PeggedToMidpoint` |
| **Relative** | `Relative` |
| **Scale** | `ScaleLimit` |
| **OCA** | `OcaGroup` (One-Cancels-All) |
| **Algo** | `Twap`, `Vwap`, `Adaptive` |

---

### 4.6 IBKR/Risk — Pre-Trade Risk Controls

#### `PreTradeControlDsl.cs` (130 lines)

DSL-based policy engine with configurable rules:

| Rule | Description |
|------|-------------|
| `max-notional` | Maximum order notional value |
| `max-qty` | Maximum share quantity |
| `max-daily-orders` | Daily order count limit |
| `session-window` | Restrict to trading hours |

**Actions**: `Warn` (log and proceed), `Reject` (block order), `Halt` (stop runtime)

#### `PreTradeCostRiskEstimator.cs` (86 lines)

Pre-trade cost estimation with three profiles:

| Profile | Commission | Slippage | Impact |
|---------|-----------|----------|--------|
| `MicroEquity` | Flat per-share | Fixed BPS | Volume share model |
| `Conservative` | Higher per-share | Higher BPS | Conservative impact |
| `VolumeShareImpact` | Standard | Spread-based | Full volume participation |

#### `FaRoutingValidator.cs` (56 lines)

Validates Financial Advisor order routing:
- Group/profile mutual exclusivity
- Allocation method validation
- Percentage total constraints

---

### 4.7 IBKR/Wrapper — Base Wrapper

#### `HarvesterEWrapper.cs` (80 lines)

Base EWrapper implementation handling:
- `nextValidId` — initial order ID
- `managedAccounts` — available accounts
- `contractDetails` / `contractDetailsEnd`
- `error` with severity classification
- `IbApiError` record: `(DateTime Timestamp, int Id, int Code, string Message, string RawLine)`

---

### 4.8 Historical — Data Ingestion Pipeline

#### `HistoricalIngestionContracts.cs` (74 lines)

Generic ETL pipeline with pluggable stages:

```
IHistoricalExtractor<TRaw>        → Extract raw data
IHistoricalNormalizer<TRaw, TCan> → Normalize to canonical format
IHistoricalWriter<TCan>           → Write to storage

HistoricalIngestionPipeline<TRaw, TCanonical> — orchestrates the pipeline
```

**`CanonicalHistoricalBar`**: Timestamp, Open, High, Low, Close, Volume, WAP, Count

---

### 4.9 Strategy — Replay & Backtesting Engine

#### `ScannerCandidateReplayRuntime.cs` ★ (1,139 lines)

The main strategy runtime for scanner-driven backtesting. Implements:
- `IStrategyRuntime`
- `IReplayOrderSignalSource`
- `IReplaySimulationFeedbackSink`
- `IReplayScannerSelectionSource`

**49 Trade Management Strategies** (Tmg001–Tmg049):

| ID | Name | Description |
|----|------|-------------|
| Tmg001 | Bracket Exit | Fixed bracket take-profit and stop-loss |
| Tmg002 | Break-even Escalation | Move stop to break-even after threshold |
| Tmg003 | Trailing Progression | Multi-stage trailing stop |
| Tmg004 | Partial Take-Profit Runner | Scale out at targets, run remainder |
| Tmg005 | Time Stop | Exit after maximum hold duration |
| Tmg006 | Volatility Adaptive Exit | ATR-based dynamic stops |
| Tmg007 | Drawdown De-Risk | Reduce on cumulative drawdown |
| Tmg008 | Session VWAP Reversion | Exit on VWAP cross |
| Tmg009 | Liquidity Spread Exit | Exit on spread widening |
| Tmg010 | Event Risk Cooldown | Reduce exposure around events |
| Tmg011 | Stall Exit | Exit on price stagnation |
| Tmg012 | PnL Cap | Hard daily P&L limit |
| Tmg013 | Spread Persistence | Exit on sustained wide spreads |
| Tmg014 | Gap Risk | Gap detection and management |
| Tmg015 | Adverse Drift | Exit on slow adverse price movement |
| Tmg016 | Peak Pullback | Exit when pullback from peak exceeds threshold |
| Tmg017 | Microstructure Stress | L2 imbalance/tape pressure detection |
| Tmg018 | Stale Favorable Move | Exit if favorable move goes stale |
| Tmg019 | Rolling Adverse Window | Rolling window adverse return check |
| Tmg020 | Underperformance Timeout | Exit on sustained underperformance |
| Tmg021 | Quote Pressure | Bid/ask imbalance pressure |
| Tmg022 | Volatility Shock Window | Exit on volatility spike |
| Tmg023 | Profit Reversion Failsafe | Protect profits from reversion |
| Tmg024 | Range Compression | Exit on volatility compression |
| Tmg025 | Rolling Volatility Floor | Minimum volatility threshold |
| Tmg026 | Chop Adverse | Exit in choppy adverse conditions |
| Tmg027 | Trend Exhaustion | Detect trend exhaustion patterns |
| Tmg028 | Reversal Acceleration | Fast reversal detection |
| Tmg029 | Sustained Reversion | Prolonged adverse reversion |
| Tmg030 | Recovery Failure | Failed recovery attempt |
| Tmg031 | Rebound Stall | Rebound that stalls |
| Tmg032 | Weak Bounce Failure | Weak bounce followed by failure |
| Tmg033 | Rebound Rollunder | Rebound rolls back under entry |
| Tmg034 | Post-Rebound Fade | Fade after rebound |
| Tmg035 | Rebound Rejection Accel | Accelerating rejection after rebound |
| Tmg036 | Rejection Stall Break | Stall then break after rejection |
| Tmg037 | Rejection Rebound Fail | Rebound fail after rejection |
| Tmg038 | Rejection Continuation Confirm | Continuation confirmed after rejection |
| Tmg039 | Double Rejection Weak Rebound | Weak rebound after double rejection |
| Tmg040 | Double Rebound Failure | Double rebound attempt failure |
| Tmg041 | Triple Step Break | Three-step breakdown pattern |
| Tmg042 | Rebound Pullback Fail | Pullback failure after rebound |
| Tmg043 | Rebound Pullback Rejection | Pullback rejection after rebound |
| Tmg044–047 | (Reserved/Additional) | Additional specialized patterns |
| Tmg048 | MTF Candle Reversal | Multi-timeframe candlestick reversal signals |
| Tmg049 | MTF Regime ATR | ATR-based regime detection across timeframes |

**Additional Overlays**:
- `Ovl001` — Flatten/reversal/giveback cap overlay
- `Eod001` — Force flat end-of-day (mandatory position closure)

**All strategies are configurable** via environment variables (e.g., `TMG_001_TAKE_PROFIT_BPS`, `TMG_005_MAX_HOLD_SECONDS`).

#### `ReplayStrategySystemLayout.cs` ★ (9,431 lines)

Contains all trade management strategy configuration records and interfaces:

**Interfaces**:
- `IReplayGlobalSafetyOverlayStrategy` — system-wide safety overlay
- `IReplayEntryStrategy` — entry signal generation
- `IReplayTradeManagementStrategy` — per-trade management

Contains `ReplayDayTradingPipeline` orchestrating the strategy chain, plus configuration record classes (with defaults) for all 49 Tmg strategies.

#### `ReplayExecutionSimulator.cs` ★ (2,142 lines)

Full execution simulation engine for backtesting:

**Simulation Capabilities**:
- Fill simulation with configurable commission and slippage models
- Corporate action application (splits, reverse splits)
- Delisting event handling
- Borrow/locate financing simulation (short selling)
- Locate rejection simulation
- Margin requirement checking
- Cash settlement processing
- Order activation simulation (stop orders, trailing stops)
- Trailing stop price updates
- OCA (One-Cancels-Other) group management
- Combo/multi-leg order lifecycle
- Order cancellation processing

**Key Records**:
```csharp
ReplayOrderIntent        // Order instruction with side, qty, type, limits
ReplayFillRow             // Fill event with price, qty, commission, slippage
ReplayPortfolioRow        // Portfolio snapshot after each event
ReplaySliceSimulationResult  // Complete simulation output per time slice
    ├── Fills
    ├── PortfolioSnapshots
    ├── CorporateActionEvents
    ├── DelistingEvents
    ├── FinancingCharges
    ├── LocateRejections
    ├── MarginRejections
    ├── CashSettlements
    ├── OrderActivations
    ├── TrailingStopUpdates
    ├── OcaCancellations
    ├── ComboLifecycleEvents
    ├── OrderCancellations
    ├── ... (16 sub-collections total)
```

#### `ScannerSelectionEngineV2.cs` (613 lines)

Multi-factor scanner selection engine (V2, replacing V1 weighted-score-only ranking):

**8-Factor Composite Scoring**:

| Factor | Weight | Description |
|--------|--------|-------------|
| FileScore | Configurable | Base scanner file quality score |
| Spread | Configurable | L1 bid-ask spread (tighter = better) |
| Volume | Configurable | Trading volume |
| L2 Depth | Configurable | Level 2 book depth |
| Momentum | Configurable | Price momentum (avoids adverse momentum) |
| SelfLearning Bias | Configurable | ML model bias (from self-learning engine) |
| TimeOfDay | Configurable | Session timing factor |
| Diversification | Configurable | Sector/exchange diversification |

**Hard Gates** (reject if failed):
- Price range gate (min/max price)
- L1 spread gate (max 3%)
- Volume minimum gate
- L2 depth gate
- Momentum gate (adverse momentum BPS threshold)

**Session Phase Adjustments**:
- Open phase (first 15 min): 0.9× score multiplier
- Close phase (last 30 min): 0.85× score multiplier
- Exchange concentration guard

#### `ReplaySelfLearningEngine.cs` (722 lines)

V2 self-learning engine using online Generalized Linear Model (GLM) with AdaGrad optimizer:

**16 Features**:

| # | Feature | Description |
|---|---------|-------------|
| 1 | intercept | Bias term |
| 2 | side_sign | Long (+1) vs Short (-1) |
| 3 | commission_bps | Transaction cost in BPS |
| 4 | slippage_bps | Execution slippage in BPS |
| 5 | period_return_bps | Trade return in BPS |
| 6 | drawdown_bps | Maximum adverse excursion |
| 7 | hold_duration_sec | Time held in seconds |
| 8 | r_multiple | Return / risk ratio |
| 9 | mfe_over_mae | Max favorable / max adverse excursion |
| 10 | atr_relative_return | Return normalized by ATR |
| 11 | time_of_day_sin | Cyclical time encoding (sin) |
| 12 | time_of_day_cos | Cyclical time encoding (cos) |
| 13 | recent_win_rate_5 | Rolling 5-trade win rate |
| 14 | streak_length | Consecutive win/loss count |
| 15 | volatility_regime | Current volatility regime proxy |
| 16 | volume_participation | Volume participation fraction |

**ML Capabilities**:
- Z-score feature normalization with running mean/variance
- Walk-forward temporal validation (no future data leakage)
- Platt calibration for probability outputs
- Feature importance ranking
- Symbol bias store with exponential decay
- Kelly criterion position sizing recommendations

#### `ReplaySelfLearningAnalyzer.cs` (231 lines)

V1 self-learning with simpler 6-feature logistic regression:
- Features: intercept, side, commission, slippage, period return, drawdown
- Brier score and log loss evaluation
- Platt calibration
- Used as fallback/comparison baseline for V2 engine

#### `ReplayMtfCandleSignals.cs` (199 lines)

Multi-timeframe candlestick signal engine:

**Timeframes**: 30s, 1m, 5m, 15m, 1h, 1D

**Signal Logic**:
- **Bullish Entry**: All 6 timeframes show bullish candle (close > open)
- **Bearish Entry**: All 6 timeframes show bearish candle (close < open)
- **Exit Signal**: 30s + 1m + 5m show reversal against position direction

#### `ReplayHistoricalCandlestickCharts.cs` (244 lines)

Builds historical candlestick charts from replay data slices. Scanner historical evaluation assesses MTF alignment quality for candidate selection.

#### `ReplayPerformanceAnalyzer.cs` (146 lines)

Performance analytics engine calculating:
- Benchmark comparison (vs. SPY or custom benchmark)
- Period returns (daily, weekly, monthly)
- Maximum drawdown and drawdown duration
- Sharpe-like ratio
- Win rate and profit factor
- Portfolio turnover
- Alpha vs. benchmark

#### `ReplayRamSessionState.cs` (575 lines)

In-memory session state tracking microstructure data per symbol:

**`ReplayMicrostructureBucketRow`**:
- Mark price, bid/ask spread, L2 bid/ask imbalance
- Tape buy/sell volume
- Volatility proxy
- Gate codes (which pre-trade gates are active)

**`ReplayTradeEpisodeRow`**:
- Entry/exit timestamps and prices
- Fill history
- Feature vectors for ML
- Label (win/loss)
- Decision trace (which strategies triggered)

#### `ReplayFinancingEngine.cs` (93 lines)

Simulates borrow/locate costs for short selling with timeline-based profile updates. Supports variable borrow rates across the day.

#### `ReplayCorporateActionsEngine.cs` (118 lines)

Handles corporate actions (stock splits) with three price normalization modes:
- `Raw` — unadjusted prices
- `SplitAdjusted` — backward-adjusted for splits
- `TotalReturn` — adjusted for splits + dividends

#### `ReplaySymbolEventsEngine.cs` (135 lines)

Processes symbol lifecycle events:
- **Renames**: Symbol→NewSymbol mapping at point in time
- **Delistings**: Symbol removal with final price

#### `DeterministicStrategyEventScheduler.cs` (113 lines)

Emits scheduled events using the exchange calendar:

| Event Type | Timing |
|------------|--------|
| `interval` | Configurable periodic (e.g., every 60s) |
| `market_open` | At session open |
| `before_open` | Before session open |
| `market_close_warning` | Before session close |
| `after_close` | After session close |
| `early_close` | On early close days |

#### `DeterministicReplayClock.cs` (18 lines)

Forward-only clock for deterministic replay — ensures no backward time travel during simulation.

#### `UsEquitiesExchangeCalendarService.cs` (123 lines)

US equities market calendar with:
- All NYSE holidays (including Easter/Good Friday computation)
- Early close days (day after Thanksgiving, Christmas Eve, July 3rd)
- Session windows: Regular (9:30–16:00 ET), extended hours support

#### `StrategyReplayDriver.cs` (105 lines)

Loads JSON replay datasets and builds `StrategyDataSlice` per row with `TopTicks` and `HistoricalBars` for feeding into the strategy engine.

#### `FrameworkModels.cs` (57 lines)

Lean/Zipline-inspired framework abstractions:

```csharp
interface IAlphaModel              → AlphaInsight (direction, magnitude, confidence, period)
interface IPortfolioConstructionModel → PortfolioTarget (symbol, quantity, weight)
interface IRiskManagementModel      → Risk-adjusted targets
interface IExecutionModel          → Order execution logic
```

#### `StrategyRuntimeContracts.cs` (26 lines)

Core data contracts:
- `StrategyRuntimeContext` — runtime context with mode, clock, session window
- `StrategyDataSlice` — data payload with timestamp, symbol, tick/bar data

---

## 5. Key Data Models

### 5.1 Order Domain

```
BrokerOrderIntent
├── Symbol, Side (BUY/SELL/SSHORT), Quantity
├── OrderType (MKT/LMT/STP/STP LMT/TRAIL/etc.)
├── TIF (DAY/GTC/IOC/FOK)
├── LimitPrice?, StopPrice?, TrailingAmount?
├── OcaGroup?, ParentId?
└── FaGroup?, FaMethod?, FaProfile?

CanonicalOrderEvent
├── OrderId, PermId, Status
├── Filled, Remaining, AvgFillPrice
├── LastFillPrice, LastFillSize
└── Timestamp, WhyHeld?

OrderLifecycleRow (state machine tracking)
├── State transitions with validation
└── Error classification per event
```

### 5.2 Market Data Domain

```
TopTickRow (L1)
├── TickType, Price, Size, Timestamp
├── Sanitized (invalid prices filtered)
└── Delayed→Realtime field normalization

DepthRow (L2)
├── Position, MarketMaker, Operation
├── Side (BID/ASK), Price, Size
└── → L2CandlestickBuilder → OHLC candles

CanonicalHistoricalBar
├── Timestamp, Open, High, Low, Close
├── Volume, WAP, Count
└── Source normalization applied
```

### 5.3 Strategy/Replay Domain

```
ReplaySliceSimulationResult
├── List<ReplayFillRow>
├── List<ReplayPortfolioRow>
├── List<CorporateActionEvent>
├── List<DelistingEvent>
├── List<FinancingCharge>
├── List<LocateRejection>
├── List<MarginRejection>
├── List<CashSettlement>
├── List<OrderActivation>
├── List<TrailingStopUpdate>
├── List<OcaCancellation>
├── List<ComboLifecycleEvent>
└── ... (16 total sub-collections)

ReplayTradeEpisodeRow
├── EntryTimestamp, ExitTimestamp
├── Symbol, Side, Quantity
├── EntryPrice, ExitPrice
├── Fills[], Features[], Label
├── DecisionTrace (which Tmg triggered exit)
└── → feeds into SelfLearningEngine
```

---

## 6. Configuration Model

### 6.1 CLI Arguments

All configuration flows through `AppOptions.Parse(args)` — there is **no `appsettings.json`**:
- RunMode selection
- IBKR connection parameters (host, port, client ID)
- Symbol/contract specifications
- Strategy parameters

### 6.2 Environment Variables

Trade management strategies are configurable via environment variables:
```
TMG_001_TAKE_PROFIT_BPS=50
TMG_001_STOP_LOSS_BPS=30
TMG_005_MAX_HOLD_SECONDS=3600
TMG_006_ATR_MULTIPLIER=2.5
...
```

### 6.3 Runtime State Persistence

`RuntimeStateStore` saves/loads JSON checkpoints:
```json
{
  "SchemaVersion": 1,
  "Timestamp": "2026-03-01T10:00:00Z",
  "Host": "MACHINE01",
  "ProcessId": 12345,
  "Stage": "Running",
  "Transitions": [...],
  "Checksum": "sha256:abc123..."
}
```

---

## 7. Architecture Patterns

| Pattern | Implementation |
|---------|---------------|
| **State Machine** | `IbConnectionState`, `OrderLifecycleModel`, `RuntimeLifecycleStage` |
| **Callback → Queue** | `SnapshotEWrapper` — EWrapper callbacks → ConcurrentQueue → sync processing |
| **Async Coordination** | `TaskCompletionSource<bool>` for all IBKR async request/response pairs |
| **Factory** | `ContractFactory`, `OrderFactory` — static factory methods |
| **Adapter** | `IbBrokerAdapter` wraps `EClientSocket` behind `IBrokerAdapter` |
| **Strategy** | `IStrategyRuntime`, `IReplayTradeManagementStrategy` — pluggable strategies |
| **Pipeline** | `HistoricalIngestionPipeline<TRaw, TCan>` — Extract→Normalize→Write |
| **DSL** | `PreTradeControlDsl` — rule-based policy engine |
| **Online Learning** | `ReplaySelfLearningEngine` — incremental GLM with AdaGrad |
| **Walk-Forward Validation** | Self-learning engine avoids future data leakage |
| **Deterministic Replay** | `DeterministicReplayClock` + event scheduling for reproducible backtests |
| **Checkpoint/Recovery** | `RuntimeStateStore` with SHA256 integrity and quarantine |

---

## 8. What Does NOT Exist

The following were expected based on the initial request but **do not exist** in this workspace:

| Expected | Status |
|----------|--------|
| `services/cs/` directory | ❌ Does not exist |
| 7 named C# projects (das-ui-*, harvester-monitor-ui-cs, etc.) | ❌ Does not exist |
| Multi-project solution | ❌ Single project only |
| NATS messaging | ❌ Not used |
| REST/gRPC APIs | ❌ Not present |
| Win32 interop | ❌ Not present |
| `appsettings.json` | ❌ CLI args only |
| `contracts/csharp/` | ❌ Does not exist |
| `contracts/schema/` | ❌ Does not exist |
| `infra/` directory | ❌ Does not exist |
| Docker configuration | ❌ Not present |
| Dependency injection | ❌ Direct instantiation |

---

## 9. Codebase Metrics

| Metric | Value |
|--------|-------|
| Total Source Files | 48 |
| Total Lines of C# | 30,192 |
| Largest File | `SnapshotRuntime.cs` (9,616 lines) |
| 2nd Largest | `ReplayStrategySystemLayout.cs` (9,431 lines) |
| 3rd Largest | `ReplayExecutionSimulator.cs` (2,142 lines) |
| Files > 1,000 lines | 5 |
| Files > 100 lines | 30 |
| NuGet Dependencies | 2 |
| Interfaces | 9 |
| Trade Mgmt Strategies | 49 (Tmg001–Tmg049) |
| IBKR RunModes | 40+ |
| ML Features (V2) | 16 |

---

## 10. Summary

**Harvester.App** is a **monolithic .NET 9.0 console application** that serves as both a **live IBKR trading terminal** (40+ operational modes) and a **deterministic strategy replay/backtesting engine** with:

1. **Full IBKR TWS API coverage** — orders, market data (L1/L2/historical), accounts, options, crypto, FA, scanners
2. **Production-grade risk controls** — DSL-based pre-trade gates, cost estimation, FA routing validation
3. **49 trade management strategies** — from basic bracket exits to sophisticated microstructure stress detection
4. **Machine learning** — online GLM with 16 features, AdaGrad optimization, walk-forward validation, Kelly sizing
5. **Multi-timeframe signal generation** — 6-timeframe candlestick alignment (30s through 1D)
6. **Full execution simulation** — fills, corporate actions, delistings, financing, margin, OCA groups

The codebase is architecturally self-contained with no external service dependencies beyond the IBKR TWS API socket connection.
