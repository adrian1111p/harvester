# Harvester.App — Comprehensive Code Audit Report

**Date:** 2026-03-04 (updated)  
**Commit:** `54c3fc9` (main)  
**Auditor:** Engineering Team  
**Target:** .NET 9 console application — `src/Harvester.App/`  
**Total LOC:** 40,513 across 95 `.cs` files  
**Build Status:** ✅ 0 errors, 0 warnings  
**Test Coverage:** 0% — no test projects exist

---

## 1. Project Structure Overview

```
src/Harvester.App/
├── Program.cs                         (107 lines)  — Entry point
├── Harvester.App.csproj               (18 lines)   — net9.0 + IBApi + ClosedXML + ASP.NET Core
├── IBKR/
│   ├── Broker/                        (5 files, ~1,190 lines)
│   │   ├── IBrokerAdapter.cs          (172)  — Interface for all broker operations
│   │   ├── IbBrokerAdapter.cs         (402)  — Concrete EClientSocket wrapper
│   │   ├── IbContractNormalizationService.cs (326) — Contract spec normalizer
│   │   ├── IbOrderTranslationService.cs     (209) — BrokerOrderIntent → IBApi.Order
│   │   └── IbHistoricalIngestionAdapters.cs (81)  — Historical ETL normalizers
│   ├── Connection/                    (2 files, ~192 lines)
│   │   ├── IbkrSession.cs            (173)  — EClientSocket lifecycle manager
│   │   └── IbConnectionState.cs      (19)   — Connection state enum
│   ├── Contracts/                     (1 file, 115 lines)
│   │   └── ContractFactory.cs        (115)  — Static factory for all contract types
│   ├── Orders/                        (1 file, 309 lines)
│   │   └── OrderFactory.cs           (309)  — Static factory for all order types
│   ├── Risk/                          (3 files, ~314 lines)
│   │   ├── PreTradeControlDsl.cs     (151)  — DSL evaluator for pre-trade guards
│   │   ├── PreTradeCostRiskEstimator.cs (95) — Commission+slippage estimator
│   │   └── FaRoutingValidator.cs     (68)   — FA routing validation
│   ├── Runtime/                       (9 files, ~12,058 lines)
│   │   ├── SnapshotRuntime.cs        (9,773)  — ⚠️ GOD CLASS
│   │   ├── SnapshotEWrapper.cs       (1,134)  — EWrapper + 35 record types
│   │   ├── OrderLifecycleModel.cs    (297)    — Order state machine
│   │   ├── OrderReconciliation.cs    (253)    — Order reconciliation
│   │   ├── L2CandlestickBuilder.cs   (227)    — L2 OHLC builder
│   │   ├── RuntimeStateStore.cs      (210)    — Checkpoint persistence
│   │   ├── L2MtfSignalStrategy.cs    (133)    — MTF signal generation
│   │   ├── IbErrorPolicy.cs          (115)    — Error classification
│   │   └── RequestRegistry.cs        (87)     — Request correlation
│   └── Wrapper/                       (1 file, 95 lines)
│       └── HarvesterEWrapper.cs      (95)   — Base EWrapper class
├── Historical/                        (1 file, 88 lines)
│   └── HistoricalIngestionContracts.cs — Generic ETL pipeline interfaces
├── Monitor/                           (3 .cs files + 1 html, ~315 lines)
│   ├── PositionsWebServer.cs         (155)  — Kestrel + WebSocket server
│   ├── IbkrPositionPoller.cs         (84)   — IBKR account update poller
│   ├── PositionMonitorStore.cs       (76)   — Thread-safe position store
│   └── wwwroot/index.html            —        SPA dashboard
├── Strategy/                          (34 files, ~18,380 lines)
│   ├── ReplayStrategySystemLayout.cs (9,431) — ⚠️ GOD CLASS
│   ├── ReplayExecutionSimulator.cs   (2,142) — Order fill simulator
│   ├── ScannerCandidateReplayRuntime.cs (1,139) — Scanner replay
│   ├── V3LiveRuntime.cs              (749)   — V3 live strategy
│   ├── ReplaySelfLearningEngine.cs   (722)   — Walk-forward learning
│   ├── ScannerSelectionEngineV2.cs   (613)   — Candidate ranking
│   ├── ReplayRamSessionState.cs      (575)   — Session state tracking
│   └── ... (27 additional files)     (~2,900) — Interfaces, configs, models
└── Backtest/                          (26 files, ~7,414 lines)
    ├── Runner/
    │   ├── StrategyComparisonRunner.cs (1,036) — Multi-strategy comparison
    │   └── LivePaperBot.cs           (517)   — Paper trading simulator
    ├── Indicators/
    │   └── TechnicalIndicators.cs    (655)   — SMA/EMA/RSI/VWAP/ATR/etc.
    ├── Strategies/
    │   ├── StrategyV4.cs             (608)   — Strategy V4
    │   └── ... (V1–V10)             (~3,600) — Strategy versions + exit engine
    └── ... (engine, models, fetchers) (~1,000)
```

---

## 2. Critical Findings

### 2.1 God Classes

| # | File | Lines | Responsibility Count | Severity |
|---|------|-------|---------------------|----------|
| 1 | `SnapshotRuntime.cs` | **9,773** | CLI parsing + 63 mode dispatch + heartbeat + reconnect + data export + risk eval + 71 record types + 3 enums | **CRITICAL** |
| 2 | `ReplayStrategySystemLayout.cs` | **9,431** | Full replay orchestration, data loading, phase management, reporting | **CRITICAL** |
| 3 | `ReplayExecutionSimulator.cs` | **2,142** | Fill simulation, margin, settlement, fee computation | HIGH |
| 4 | `SnapshotEWrapper.cs` | **1,134** | 36 ConcurrentQueues + 30 TCS fields + 35 record types | HIGH |

### 2.2 AppOptions Monolith

The `AppOptions` record has **~145 positional parameters** — the largest record in the codebase. It is defined inline within `SnapshotRuntime.cs`. Its `Parse(string[] args)` method is ~800 lines of manual `switch`/`case` CLI parsing.

**Problems:**
- Adding a new CLI flag requires editing 4 locations in the same file (record field, default variable, switch case, constructor call)
- No validation of required arguments
- Unknown flags are silently ignored
- Hardcoded defaults include a real IBKR account number (`U22462030`)

### 2.3 Zero Test Coverage

No test projects, test files, or test framework references exist anywhere in the repository. The following modules are pure logic and highly testable but have no tests:

| Module | Testability |
|--------|-------------|
| `IbErrorPolicy` | 100% — pure classification logic |
| `PreTradeControlDsl` | 100% — pure DSL parser/evaluator |
| `FaRoutingValidator` | 100% — pure validation |
| `OrderLifecycleModel` | 100% — pure state machine |
| `OrderReconciliation` | 100% — pure reconciliation |
| `ContractFactory` | 100% — pure factory |
| `OrderFactory` | 100% — pure factory |
| `L2CandlestickBuilder` | 100% — pure aggregation |
| `TechnicalIndicators` | 100% — pure math |
| `PreTradeCostRiskEstimator` | 100% — pure estimation |

### 2.4 No Structured Logging

There are **335+ raw `Console.WriteLine` calls** in `SnapshotRuntime.cs` alone. No `ILogger`, no log levels, no structured output, no log sinks.

### 2.5 Thread Safety Issues

| # | Location | Issue |
|---|----------|-------|
| 1 | `SnapshotEWrapper._accountDownloadEndTcs` | Non-readonly field replaced via plain assignment — race if read/write overlap |
| 2 | `V3LiveRuntime._evaluations`, `_signals`, `_exitEvents`, `_riskEvents` | Plain `List<T>` accessed from async callbacks — not thread-safe |
| 3 | `SnapshotRuntime._dailyTransmittedOrderCount` | Plain `int` accessed from heartbeat monitor thread without `Interlocked` |

### 2.6 Hardcoded Values

| Value | Location | Risk |
|-------|----------|------|
| Account `U22462030` | `AppOptions.Parse` default | **Security** — real account in source |
| Symbols `SIRI, SOFI, F, PLTR` | `AppOptions.Parse` default | Misleading defaults |
| V3 symbols `NVDA, META, AMD, AAPL` | `V3LiveConfig.cs` | Should use env/config |
| `"temp"` directory paths | `ScannerCandidateReplayRuntime.cs` | Platform-dependent |

---

## 3. Architecture Assessment

### 3.1 What's Working Well

| Area | Assessment |
|------|-----------|
| **IBKR/Broker layer** | Clean `IBrokerAdapter` interface with proper separation |
| **IBKR/Connection** | `IbkrSession` is focused and well-scoped |
| **IBKR/Risk** | Small, focused classes with single responsibilities |
| **Monitor module** | Clean 3-class design (Store/Poller/WebServer) |
| **Strategy interfaces** | `IStrategyRuntime`, `ILiveOrderSignalSource` are well-designed |
| **Historical ETL** | Proper generic pipeline with `IHistoricalExtractor/Normalizer/Writer` |
| **Dependency flow** | No circular namespace dependencies detected |

### 3.2 What Needs Refactoring

| Area | Problem | Impact |
|------|---------|--------|
| **SnapshotRuntime** | Does everything — CLI, routing, heartbeat, reconnect, modes, export, risk | Untestable, unmaintainable |
| **AppOptions** | 145 params in one record, inline in SnapshotRuntime | Change amplification |
| **Record types** | 71 records in SnapshotRuntime + 35 in SnapshotEWrapper | Poor discoverability |
| **ReplayStrategySystemLayout** | 9,431-line replay orchestrator | Same as SnapshotRuntime |
| **Logging** | Raw Console.WriteLine everywhere | No filtering, no persistence |
| **Configuration** | 145 CLI flags, no appsettings.json support | Poor DX |

### 3.3 Dependency Graph

```
Program.cs
  ├── Backtest/ (standalone backtest modes)
  └── IBKR/Runtime/SnapshotRuntime
        ├── IBKR/Broker/IBrokerAdapter ← IbBrokerAdapter
        ├── IBKR/Connection/IbkrSession
        ├── IBKR/Contracts/ContractFactory
        ├── IBKR/Orders/OrderFactory
        ├── IBKR/Risk/ (PreTrade*, FaRouting)
        ├── IBKR/Wrapper/HarvesterEWrapper ← SnapshotEWrapper
        ├── Monitor/ (PositionMonitorStore, Poller, WebServer)
        ├── Strategy/ (IStrategyRuntime, V3LiveRuntime, Replay*)
        └── Historical/ (ETL pipeline)
```

**Concern:** `Strategy/` references `IBKR/Runtime/` for record types (PortfolioUpdateRow, etc.) — DTOs should live in a shared contracts layer.

---

## 4. Refactoring Recommendations

### Priority 1 — Critical (address within next sprint)

| # | Task | Files Affected | Estimated Effort |
|---|------|---------------|-----------------|
| **R1** | **Split SnapshotRuntime.cs** — Extract each RunMode group into its own class (e.g., `ConnectMode.cs`, `OrdersMode.cs`, `HistoricalMode.cs`, `ScannerMode.cs`, `CryptoMode.cs`, `FaMode.cs`, `OptionsMode.cs`). Keep thin `RuntimeOrchestrator` with dispatch. | 9,773 → ~500 per file + orchestrator | 3–5 days |
| **R2** | **Extract AppOptions** into `Options/AppOptions.cs` + `Options/AppOptionsParser.cs`. Decompose into sub-records: `ConnectionOptions`, `LiveTradingOptions`, `HistoricalOptions`, `ScannerOptions`, `ReplayOptions`, `CryptoOptions`, `FaOptions`, `MonitorOptions` | 1 → 10 files | 2–3 days |
| **R3** | **Extract record types** — Move 71 records from SnapshotRuntime and 35 from SnapshotEWrapper into domain-specific model files under `Models/` | 2 → 8–10 files | 1–2 days |
| **R4** | **Add test project** — Create `Harvester.App.Tests` with unit tests for the 10 pure-logic modules listed in §2.3 | New project | 3–4 days |
| **R5** | **Remove hardcoded account** — Replace `U22462030` default with empty string + required validation | 1 file | 0.5 day |

### Priority 2 — High (next 2 sprints)

| # | Task | Estimated Effort |
|---|------|-----------------|
| **R6** | **Split ReplayStrategySystemLayout.cs** (9,431 lines) using same pattern as R1 | 3–5 days |
| **R7** | **Introduce `ILogger`** via `Microsoft.Extensions.Logging` — replace 335+ Console.WriteLine calls | 2–3 days |
| **R8** | **Fix thread safety** — atomic TCS replacement, ConcurrentQueue for V3Live lists, Interlocked for counters | 1 day |
| **R9** | **Extract finalization logic** — Deduplicate 4 catch blocks in `RunAsync` into `FinalizeRun(exitCode)` | 0.5 day |
| **R10** | **Add `appsettings.json` support** alongside CLI flags using `IConfiguration` | 2 days |

### Priority 3 — Medium (backlog)

| # | Task | Estimated Effort |
|---|------|-----------------|
| **R11** | Add XML doc comments to all public interfaces and key methods | 2 days |
| **R12** | Introduce `Harvester.Contracts` shared project for cross-layer DTOs | 1–2 days |
| **R13** | Implement `IRunMode` interface + command pattern for mode dispatch | 2–3 days |
| **R14** | Create integration tests with mock `IBrokerAdapter` | 3–5 days |
| **R15** | Extract `StrategyBase` from V1–V10 backtest strategies to reduce duplication | 1–2 days |

### Execution Status Update (2026-03-04)

Completed items from this audit roadmap:

- ✅ **R4** test project established and expanded (`tests/Harvester.App.Tests`, now 17 passing tests)
- ✅ **R6** replay layout split started (`ReplayStrategyContracts.cs` extracted from `ReplayStrategySystemLayout.cs`)
- ✅ **R7** structured logging introduced and wired in runtime paths (`SnapshotRuntime`, `V3LiveRuntime`)
- ✅ **R8** thread-safety hardening applied (atomic TCS replacement, synchronized event collections, atomic counters)
- ✅ **R9** runtime finalization deduplicated into centralized helper path
- ✅ **R10** configuration layering added (`appsettings.json` + env + CLI precedence)
- ✅ **R11** key XML docs added for public strategy/runtime contracts
- ✅ **R12** shared contracts project introduced (`src/Harvester.Contracts`) and consumed by app/tests
- ✅ **R13** run-mode command pattern introduced (`IRunModeCommand` + command implementations)
- ✅ **R14** integration-style wrapper tests added with mock `IBrokerAdapter`
- ✅ **R15** backtest strategy base extraction applied (`BacktestStrategyBase` and `ConductStrategyAdapterBase`)

Remaining high-priority roadmap work from this document is concentrated in **R1/R2** (full runtime/app-options decomposition) and broader **R3** model extraction scope.

---

## 5. Module-by-Module Scores

| Module | Size | Quality | Testability | Documentation | Overall |
|--------|------|---------|-------------|---------------|---------|
| IBKR/Broker | ✅ Good | ✅ Good | ✅ High | ⚠️ No XML docs | **B+** |
| IBKR/Connection | ✅ Good | ✅ Good | ✅ High | ⚠️ No XML docs | **B+** |
| IBKR/Contracts | ✅ Good | ✅ Good | ✅ High | ⚠️ No XML docs | **B** |
| IBKR/Orders | ✅ Good | ✅ Good | ✅ High | ⚠️ No XML docs | **B** |
| IBKR/Risk | ✅ Good | ✅ Good | ✅ High | ⚠️ No XML docs | **B+** |
| IBKR/Runtime | ❌ Critical | ⚠️ Mixed | ❌ Low | ❌ None | **D** |
| IBKR/Wrapper | ✅ Good | ✅ Good | ⚠️ Medium | ⚠️ No XML docs | **B** |
| Monitor | ✅ Good | ✅ Good | ✅ High | ✅ Has XML docs | **A-** |
| Historical | ✅ Good | ✅ Good | ✅ High | ⚠️ Minimal | **B** |
| Strategy | ❌ Critical | ⚠️ Mixed | ⚠️ Medium | ⚠️ Minimal | **C-** |
| Backtest | ⚠️ Large | ⚠️ Mixed | ⚠️ Medium | ❌ None | **C** |

---

## 6. Recommended Refactoring Order

```
Phase 1 (Week 1-2):  R5 → R3 → R2 → R1
Phase 2 (Week 3-4):  R4 → R7 → R8 → R9
Phase 3 (Week 5-6):  R6 → R10 → R11
Phase 4 (Backlog):   ✅ R12 → ✅ R13 → ✅ R14 → ✅ R15
```

**Rationale:** Start with quick wins (R5 hardcoded removal, R3 record extraction) before the larger structural splits (R1, R2). Add tests (R4) immediately after the split so refactored code has coverage. Logging (R7) and thread safety (R8) are independent and can run in parallel.

---

## 7. Summary

The codebase has a solid foundation in its broker abstraction, connection management, risk evaluation, and monitoring layers. However, two critical god classes (`SnapshotRuntime.cs` at 9,773 lines and `ReplayStrategySystemLayout.cs` at 9,431 lines) represent **47% of total LOC** and create severe maintainability, testability, and onboarding risk. The complete absence of unit tests compounds this risk.

The recommended refactoring plan prioritizes decomposing these god classes, establishing test coverage, and introducing structured logging — all without changing external behavior.
