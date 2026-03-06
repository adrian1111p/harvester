# Harvester Platform (IBKR) — Full Architectural Audit Report

**Date**: March 6, 2026  
**Scope**: Harvester.App (IBKR stack only — DAS stack excluded as unmaintained)  
**Repository**: `https://github.com/adrian1111p/harvester.git` | Branch: `main`

---

## 1. Executive Summary

Harvester is an **automated day-trading platform** built on **.NET 9 / C# 13** that interfaces directly with **Interactive Brokers TWS** via IBApi. The codebase spans **~50,000+ lines of C#** covering live trading, backtesting, replay simulation, self-learning ML, scanner-based symbol selection, and real-time monitoring.

The platform has a strong feature set — particularly its production-grade replay simulator with 49 TMG exit strategies and its self-learning GLM engine — but suffers from **critical backtest-to-live parity gaps**, an **unfavorable risk/reward ratio in live mode**, and a **God-class monolith** at 10,253 lines. These issues directly undermine profitability and must be addressed to achieve a consistent trading edge.

---

## 2. Architecture Overview

```
Program.cs (32 lines) → AppOptionsParser (1,316 lines, 100+ CLI flags)
    └→ SnapshotRuntime (8,876 lines, 58 mode handlers) ← GOD CLASS
        ├── IBKR/ (Broker, Connection, Contracts, Orders, Risk, Runtime, Wrapper)
        ├── Strategy/V3Live* (live trading engine)
        ├── Strategy/Replay/* (production-grade walk-forward simulation)
        ├── Backtest/* (quick hypothesis testing & parameter sweeps)
        ├── Scanner/* (symbol selection + auto-trade slot management)
        ├── SelfLearning/* (GLM-based ML engine, V2.1)
        └── Monitor/* (real-time WebSocket dashboard on port 5100)
```

- **Runtime**: .NET 9, C# 13, IBApi v1.0.0-preview-975, ClosedXML 0.104.2, ASP.NET Core (Monitor UI)
- **68 run modes** dispatched via CLI `--mode` through a single switch statement
- **No DI container**, no plugin architecture — everything routed through one partial class
- **Config layering**: `appsettings.json` + CLI overrides via `AppOptionsParser`

---

## 3. Module Inventory

| Module | Files | ~Lines | Purpose |
|--------|------:|-------:|---------|
| **SnapshotRuntime** (God class) | 4 partial files | 10,253 | Central orchestrator for all 58 non-backtest modes |
| **AppOptionsParser** | 1 | 1,316 | CLI parser with 100+ options across 15 categories |
| **V3Live Strategy Engine** | 8 | ~2,200 | Live trading: signal scoring, risk guard, position monitoring, order bridge |
| **Replay Simulator** | 10+ | ~5,000 | Production-grade walk-forward with 49 TMG exit strategies, realistic fills |
| **Backtest Engine** | 29 | ~8,244 | Quick hypothesis testing, parameter sweeps, strategy comparison |
| **Scanner** | 5+ | ~1,500 | 8-factor symbol scoring + auto-trade slot management (5-8 slots) |
| **Self-Learning V2.1** | 4+ | ~1,200 | GLM-based trade quality prediction with expanding-window walk-forward |
| **Monitor UI** | 4 | ~683 | Real-time WebSocket dashboard (Kestrel, positions + PnL) |
| **IBKR Integration** | 30+ | ~4,000 | Full TWS API wrapper (orders, positions, market data, account, risk) |
| **Contracts** | 1 | 48 | Shared record types (anemic — most types live in SnapshotRuntime.Models) |

### 3.1 V3Live Strategy Engine (Live Trading)

| Component | Purpose |
|-----------|---------|
| **V3LiveRuntime** (788 lines) | Main orchestrator: tick loop, symbol management, position lifecycle |
| **V3LiveSignalEngine** | 7-component composite signal scorer (momentum, VWAP, L2, volume, trend, mean-reversion, microstructure) |
| **V3LiveFeatureBuilder** | 10 indicators: ATR, RSI, VWAP, Bollinger Bands, Keltner Channels, Stochastic, ADX, RVOL, VolAcc, OFI |
| **V3LiveRiskGuard** | 9 pre-trade risk checks: daily loss, open risk, duplicate, slippage, L2 depth, imbalance, price, RVOL, ADX |
| **V3LivePositionMonitor** | 11 exit conditions: hard stop, trail, TP1/TP2, time stop, EOD, giveback, reversal, momentum fade, spread, stale |
| **V3LiveOrderBridge** | Order placement and management via IBKR API |
| **V3LiveConfig** | 80+ configurable parameters |
| **V3LiveSelectionGate** | Symbol eligibility filtering |

### 3.2 Replay Simulator (Walk-Forward)

| Component | Purpose |
|-----------|---------|
| **ReplayExecutionSimulator** (2,431 lines) | Full order lifecycle: fill simulation with slippage, commissions, SEC/TAF fees, queue priority |
| **ReplayDayTradingPipeline** | Multi-day walk-forward pipeline orchestrator |
| **TmgStrategyFactory** | Factory for 49 TMG exit strategies (only Tmg001 enabled by default) |
| **DeterministicReplayClock** | Explicit time advancement for deterministic results |
| **StrategyRuntimeContext** | Scheduled events, data slice notifications, margin/cash enforcement |
| **ScannerSelectionEngineV2** (701 lines) | 8-factor composite scoring for symbol selection |

### 3.3 Backtest Engine (Quick Hypothesis Testing)

| Component | Purpose |
|-----------|---------|
| **BacktestEngine** (184 lines) | Statistics: equity curve, drawdown, Sharpe (√252 annualized), profit factor, win rate, exit breakdown |
| **TechnicalIndicators** (764 lines) | 24 technical indicators (ATR, EMA, SMA, VWAP, RSI, ADX, RVOL, Bollinger, etc.) |
| **ExitEngine** (487 lines) | Full exit cascade: hard stop → breakeven → trailing → TP1/TP2/TP3 → time stop → EOD → giveback |
| **StrategyV11** | Latest backtest strategy variant |
| **LivePaperBot** (583 lines) | Paper trading bot with persistent polling loop and EOD flatten at 15:55 ET |
| **ParameterSweep** (293 lines) | Grid search over configs, ranked by Sharpe ratio |
| **StrategyComparisonRunner** (1,402 lines) | Runs all strategy variants across full symbol universe |
| **Archived Strategies** (V1–V9, 2,578 lines) | Kept for comparison runs |

### Backtest vs. Replay: Key Differences

| Aspect | **Backtest** | **Replay** |
|--------|-------------|-----------|
| Data source | Cached CSV files | Time-ordered slices from replay input |
| Execution model | Simplified `SimulateTrade()` | Full order lifecycle with slippage, commissions, fees |
| Corporate actions | Not modeled | Full support: price normalization, delists |
| Margin/cash | Not modeled | Margin requirements, settled cash, settlement lag |
| Short selling | Basic side tracking | Borrow/locate profiles with rejection tracking |
| Clock | Implicit (bar index) | `DeterministicReplayClock` with explicit time |
| Output | Statistics summary | 15+ distinct artifact types |
| Purpose | Quick hypothesis testing | Production-grade walk-forward validation |

### 3.4 Scanner Auto-Trade Module

The `positions-auto-replace-scan-loop` mode is a **fully autonomous slot-replacement trading loop**:

1. **Slot management**: Maintains 5–8 active position slots (`LiveScannerTopN`)
2. **Scanner refresh** (every 15 min): Loads candidate CSVs per market phase (open / post-open gainers / post-open losers), filters by `WeightedScore >= LiveScannerMinScore`, probes IBKR for volume/price validation
3. **Replacement execution**: When a slot frees, dequeues next candidate, creates `LiveOrderPlacementPlan`, executes via IBKR
4. **Safety controls**: Max 8 trades per window, stale file kill switch, min candidate count, budget concentration cap
5. **V2 bias integration**: Self-learning bias store adjusts candidate scoring

### 3.5 Monitor UI

A clean, lightweight real-time dashboard:
- **IbkrPositionPoller** → **PositionMonitorStore** → **PositionsWebServer** (3 well-separated classes)
- WebSocket push at 2s intervals with auto-reconnect
- Displays: Position Count, Total Unrealized P&L, Total Realized P&L, Total Market Value
- Per-position table: Symbol, Side, Qty, Avg Cost, Mkt Price, Unrealized P&L, P&L %, Realized P&L
- Dark theme, color-coded values — activated via `--mode positions-monitor-ui` on port 5100

---

## 4. Critical Weaknesses & Problems

### 4.1 🔴 CRITICAL — Backtest/Live Parity Gap

The **single most dangerous issue**. The backtest and live engines use **fundamentally different exit parameters**:

| Parameter | Backtest (V11) | Live (V3Live) | Impact |
|-----------|---------------|---------------|--------|
| Hard Stop | **1.1R** | **0.90R** | Live cuts losers 22% sooner |
| Trail Start | **0.9R** | **0.35R** | Live trail activates 2.6× earlier |
| TP2 Target | **1.8R** | **1.45R** | Live takes profit 19% lower |
| TP1 Action | **Tighten to breakeven** | **Advisory log only** | TP1 protection absent in live |
| Partial Exits | ✅ Supported | ❌ Full quantity only | Can't scale out of winners |

**Consequence**: Any backtest showing a profitable edge **cannot be trusted** for live deployment. You are effectively flying blind.

### 4.2 🔴 CRITICAL — Unfavorable Risk/Reward Ratio

Live configuration: **0.90R stop** vs **1.45R TP2** = **1.61:1 R:R**.

With `$30 GivebackUsdCap` on `$22 MaxRiskPerTrade`, profits are clipped at **~1.36R** before reaching TP2. This requires a **>42% win rate** just to break even (before commissions). With realistic commissions and slippage, you need **>48-50%** — a very thin edge.

### 4.3 🔴 CRITICAL — TP1 Advisory-Only in Live

When price reaches TP1 (0.55R), the backtest tightens the stop to breakeven, protecting the trade. In live mode, TP1 merely logs a message. **Profitable trades can reverse to a full loss** that the backtest would have prevented.

### 4.4 🔴 CRITICAL — No Partial Exits in Live

V3Live executes **full-quantity entries and exits only**. No scale-in, scale-out, or partial profit-taking. This means:
- No locking in partial profits at TP1
- No adding to winning positions
- No reducing risk as price moves favorably

### 4.5 🔴 CRITICAL — God Class: SnapshotRuntime (10,253 lines)

`SnapshotRuntime.cs` at 8,876 lines (10,253 with partials) contains **58 mode handlers** in a single switch statement. No DI, no interface segregation, no testability. Any change to one mode risks breaking others. This is the biggest maintainability bottleneck.

### 4.6 🟡 HIGH — 48 of 49 TMG Strategies Disabled

The replay simulator has **49 sophisticated TMG exit strategies**, but `TmgStrategyFactory` defaults to only `Tmg001`. The remaining 48 — including trailing variants, partial exit strategies, and time-adaptive exits — represent **enormous untapped edge** sitting in the codebase unused.

### 4.7 🟡 HIGH — Feature Recomputation Every Tick

`V3LiveFeatureBuilder` recalculates all 10 indicators from scratch on every tick for every symbol. With 5-8 active symbols, this is **~25 indicator computations per tick per symbol** with zero caching. Wastes CPU and increases tick-to-decision latency.

### 4.8 🟡 HIGH — LivePaperBot Duplication

`LivePaperBot.cs` (583 lines) implements its own trading loop with position management, while the live scanner loop in SnapshotRuntime implements a parallel version. Zero shared code between them.

### 4.9 🟡 MEDIUM — Anemic Contracts Library

`Harvester.Contracts` contains only **48 lines** and **5 records**. Critical types like `MonitorPositionRow`, `PortfolioUpdateRow`, `LiveScannerCandidateRow` are buried in `SnapshotRuntime.Models.cs` (1,177 lines), preventing proper separation of concerns.

### 4.10 🟡 MEDIUM — No Regime Detection

The strategy uses static parameters regardless of whether the market is trending, ranging, or volatile. The same stop/trail/target values apply to a 2% gap-up momentum day and a flat, choppy session. This leads to unnecessary stops in ranging markets and premature exits in trending ones.

### 4.11 🟡 MEDIUM — No Time-of-Day Filtering

Most retail day-trading edge concentrates in the first 30-90 minutes. The system currently treats all market hours equally, taking afternoon trades with statistically weaker expected returns.

### 4.12 🟡 MEDIUM — Static Signal Weights

The 7-component signal scorer uses fixed weights. The self-learning V2.1 engine exists but doesn't feed back into signal component weighting. Signal components that have stopped working continue receiving the same weight.

### 4.13 🟡 MEDIUM — StrategyComparisonRunner Bloat

At 1,402 lines for a single runner class, this should be decomposed into smaller units with shared infrastructure.

### 4.14 🟢 LOW — Archive Bloat

8 archived strategy files (2,578 lines) live in the source tree. These could be referenced via git history instead.

---

## 5. Profitability Improvement Roadmap

### Phase 1 — Immediate Edge Protection (1-2 weeks)

| # | Action | Impact | Effort |
|---|--------|--------|--------|
| **1** | **Fix backtest/live parity** — Unify V3Live and V11 exit parameters into a single shared config so backtests are trustworthy | 🔴 Critical — without this, no backtest result is valid | 2-3 days |
| **2** | **Implement TP1 breakeven in live** — When price hits 0.55R, move stop to entry + small buffer | 🔴 High — prevents ~15-20% of winners from turning into full losers | 1 day |
| **3** | **Implement partial exits** — Close 50% at TP1, trail remainder to TP2 | 🔴 High — classic day-trading technique; locks profits while allowing runners | 2-3 days |
| **4** | **Raise GivebackUsdCap** from $30 to at least $38 (1.72× risk) so TP2 at 1.45R can actually trigger | 🟡 Medium — currently clipping profits at 1.36R | 30 min |

### Phase 2 — Strategy Enhancement (2-4 weeks)

| # | Action | Impact | Effort |
|---|--------|--------|--------|
| **5** | **Enable and test TMG strategies** — Backtest all 49 TMG exit strategies via replay, identify top 3-5 performers per regime | 🟡 High — 48 strategies already coded but unused | 1 week |
| **6** | **Add regime detection** — Classify market as trending/ranging/volatile; switch exit strategy + parameters dynamically | 🟡 High — different conditions need different exits | 1 week |
| **7** | **Add time-of-day filters** — Weight signals by time-of-day statistical edge; reduce or skip afternoon entries | 🟡 Medium — avoids low-edge trades | 2-3 days |
| **8** | **Add multi-timeframe confirmation** — Require 5m/15m trend alignment before entering on 1m signals | 🟡 Medium — filters counter-trend entries | 3-5 days |
| **9** | **Connect self-learning to signal weights** — Use V2.1 engine output to dynamically weight the 7 signal components based on recent performance | 🟡 Medium — adaptive signal scoring | 1 week |

### Phase 3 — Architecture Modernization (4-8 weeks)

| # | Action | Impact | Effort |
|---|--------|--------|--------|
| **10** | **Decompose SnapshotRuntime** — Extract 58 mode handlers into individual `IModeHandler` classes with DI container | 🟡 High (maintainability) — reduce 10,253-line God class to ~500-line router | 2 weeks |
| **11** | **Add feature caching** — Cache indicator values per bar; only recompute on new bars, not every tick | 🟡 Medium — ~90% CPU reduction during tick processing | 3-5 days |
| **12** | **Enrich Contracts library** — Move 1,177 lines of models from SnapshotRuntime.Models into Harvester.Contracts | 🟡 Medium — enables proper layering and testability | 3-5 days |
| **13** | **Add structured trade journal** — Log entry/exit reasoning, signal scores, risk guard results at decision time | 🟡 Medium — essential for debugging losing streaks | 1 week |
| **14** | **Consolidate LivePaperBot** — Merge paper-trading logic with live scanner loop to eliminate duplication | 🟡 Medium — single position-management path for paper + live | 3-5 days |
| **15** | **Walk-forward optimization pipeline** — Weekly automated retraining: expanding window → OOS validation → config deployment only if edge persists | 🟡 Medium — prevents parameter decay | 1-2 weeks |

---

## 6. Modern Approach Recommendations

### 6.1 Unified Strategy Framework

```
┌───────────────────────────────────────────┐
│         IStrategyEngine (unified)         │
│  ┌────────────┐  ┌─────────────────────┐  │
│  │ SignalGen   │  │   ExitManager       │  │
│  │ (entry)    │→ │ (TMG-based, hot-    │  │
│  │            │  │  swappable by regime)│  │
│  └────────────┘  └─────────────────────┘  │
│       ↕                ↕                  │
│  ┌────────────┐  ┌─────────────────────┐  │
│  │ RiskGate   │  │  RegimeDetector     │  │
│  │ (pre+in)   │  │  (adapt params)     │  │
│  └────────────┘  └─────────────────────┘  │
└──────────────────┬────────────────────────┘
                   ↓
          ┌────────────────┐
          │  IBrokerAdapter │
          │  (IBKR TWS)    │
          └────────────────┘
```

**Key principle**: One strategy definition runs identically in backtest, replay, paper, and live — same config, same exit logic, same parameters. No more parity gaps.

### 6.2 Walk-Forward Optimization Pipeline

```
Historical Data → Expanding Windows → Parameter Optimization → Out-of-Sample Validation
                                                                      ↓
                                          Live Trading ← Config Deploy (only if OOS passes)
```

Weekly automated cycle: train on expanding window (last 60 days) → validate on held-out 5-day OOS window → deploy only if edge persists → track parameter stability (if optimal params shift wildly, the edge is fragile — reduce size).

### 6.3 Enhanced Monitor UI

Extend the existing clean dashboard architecture to include:
- **Drawdown gauge** with circuit-breaker visualization
- **Rolling win rate / profit factor** (last 20 trades)
- **Signal quality heatmap** by time-of-day and symbol
- **Strategy attribution** — which signal components are contributing current edge

### 6.4 Self-Learning Feedback Loop

```
Live Trades → Self-Learning V2.1 → Signal Weight Adjustments → V3Live Config
     ↑                                                              ↓
     └──────────── Rolling Performance Tracking ←───────────────────┘
```

The self-learning engine already exists (V2.1, fixed and refactored). It should feed back into the live signal scorer's component weights, creating an adaptive system that down-weights signals that have stopped working and up-weights those performing well.

---

## 7. Priority Matrix

| Priority | Action | Risk Reduction | Profit Increase |
|----------|--------|:-:|:-:|
| **P0** | Fix backtest/live parity | ★★★★★ | ★★★ |
| **P0** | Implement TP1 breakeven in live | ★★★★ | ★★★ |
| **P0** | Implement partial exits | ★★★★ | ★★★★ |
| **P1** | Raise GivebackUsdCap | ★★ | ★★★ |
| **P1** | Enable & backtest TMG strategies | ★★★ | ★★★★ |
| **P1** | Add regime detection | ★★★ | ★★★★ |
| **P2** | Time-of-day filters | ★★ | ★★★ |
| **P2** | Multi-timeframe confirmation | ★★★ | ★★ |
| **P2** | Self-learning → signal weight feedback | ★★ | ★★★ |
| **P3** | Decompose SnapshotRuntime | ★★★★ | ★ |
| **P3** | Feature caching | ★★ | ★ |
| **P3** | Enrich Contracts library | ★★ | — |
| **P3** | Structured trade journal | ★★★ | ★★ |
| **P3** | Consolidate LivePaperBot | ★★ | ★ |
| **P3** | Walk-forward optimization pipeline | ★★★ | ★★★ |

---

## 8. Summary

| Category | Count |
|----------|------:|
| Critical issues (directly impact P&L) | **5** |
| High-severity design weaknesses | **3** |
| Medium-severity issues | **5** |
| Low-severity issues | **1** |
| Improvement recommendations | **15** |
| Estimated total effort (all phases) | **10-14 weeks** |

**Bottom line**: The platform has sophisticated components — the replay simulator with 49 TMG exits, the self-learning V2.1 engine, the 8-factor scanner — but they are **poorly integrated with the live trading path**. The three highest-impact actions require no new invention, only alignment: **(1)** make backtest and live use identical exit parameters, **(2)** implement TP1 breakeven protection in live, and **(3)** add partial exits. These P0 items alone, achievable in ~1 week, can meaningfully improve win rate and reduce drawdowns by preventing winning trades from turning into losers.
