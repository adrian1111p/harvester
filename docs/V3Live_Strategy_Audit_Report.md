# V3Live Strategy — Architecture & Code Audit Report

**Document ID:** HARV-AUDIT-V3L-001  
**Date:** 2026-03-04  
**Scope:** V3Live strategy layer + order lifecycle pipeline  
**Purpose:** Starting point for code refactoring. Intended for both developer and non-developer stakeholders.  
**Auditor:** GitHub Copilot (automated analysis)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [System Overview (Non-Technical)](#2-system-overview-non-technical)
3. [Architecture Map](#3-architecture-map)
4. [File Inventory & Responsibilities](#4-file-inventory--responsibilities)
5. [Data Flow: Signal → Order → Fill → Exit](#5-data-flow-signal--order--fill--exit)
6. [Component-by-Component Analysis](#6-component-by-component-analysis)
7. [Backtest vs. Live: Divergence Analysis](#7-backtest-vs-live-divergence-analysis)
8. [Critical Findings](#8-critical-findings)
9. [Risk Assessment Matrix](#9-risk-assessment-matrix)
10. [Configuration Audit](#10-configuration-audit)
11. [Code Quality & Maintainability](#11-code-quality--maintainability)
12. [Refactoring Recommendations](#12-refactoring-recommendations)
13. [Appendix A — Full Config Parameter Reference](#appendix-a--full-config-parameter-reference)
14. [Appendix B — Glossary](#appendix-b--glossary)

---

## 1. Executive Summary

### What V3Live Does

V3Live is a **live trading strategy** that watches real-time market data from Interactive Brokers (IBKR), computes technical indicators, evaluates three sub-strategies (VWAP Reversion, Bollinger Band Bounce, and Keltner Squeeze Breakout), and generates trade signals with proposed orders.

### Key Finding

> **V3Live's order intents are NEVER transmitted to IBKR.** The strategy generates signals and proposed orders, but these are only exported to JSON files for journaling. The actual live orders flowing to the broker are placed by a separate system (`SnapshotRuntime`) via CLI arguments — **the two systems are completely disconnected.**

### For Non-Developers

Think of V3Live as a **research analyst** who watches the screen, writes a recommendation on paper, and puts it in a filing cabinet — but nobody picks it up and gives it to the trader. The actual "trader" (SnapshotRuntime) makes their own decisions based on separate instructions (command-line arguments). The analyst's filed recommendations are useful as a record, but they don't drive any real trades.

### Impact Assessment

| Area | Status | Severity |
|------|--------|----------|
| Signal generation logic | ✅ Functional | — |
| Risk guard checks | ✅ Functional | — |
| Order intent → IBKR bridge | 🔴 **Not connected** | **CRITICAL** |
| Live exit management | ✅ Handled by Conduct V1.2 | — |
| Backtest-Live parameter parity | ⚠️ Significant drift | HIGH |
| Single-symbol resolution | ⚠️ Bug | MEDIUM |
| Code duplication | ⚠️ Substantial | MEDIUM |

---

## 2. System Overview (Non-Technical)

### The Trading Pipeline in Plain Language

The Harvester system works like a factory assembly line:

```
┌─────────────┐     ┌──────────────┐     ┌───────────────┐     ┌─────────────┐
│  1. WATCH   │ ──► │  2. ANALYZE  │ ──► │  3. DECIDE    │ ──► │  4. ACT     │
│  Market     │     │  Compute     │     │  Generate     │     │  Send order │
│  data feeds │     │  indicators  │     │  signals      │     │  to broker  │
└─────────────┘     └──────────────┘     └───────────────┘     └─────────────┘
  SnapshotRuntime      V3LiveFeature       V3LiveSignal          ORDER BRIDGE
  (IBKR API)           Builder             Engine                IS MISSING ⚠️
```

**Step 1 (WATCH):** The system connects to IBKR and receives live prices, order book depth, and historical candles.

**Step 2 (ANALYZE):** The Feature Builder transforms raw data into meaningful metrics — like "how far is the price from the average?" or "is the order book showing more buyers than sellers?"

**Step 3 (DECIDE):** The Signal Engine runs three sub-strategies to find trade opportunities. The Risk Guard checks if the proposed trade is safe (within daily loss limits, position size limits, etc.).

**Step 4 (ACT):** ⚠️ This is where the gap exists. The strategy writes its trade recommendation to a JSON file, but **there is no automated bridge that picks it up and sends it to Interactive Brokers**. Live order execution currently requires manual CLI invocation via a separate command.

### Three Sub-Strategies in Plain Language

| Strategy | What It Looks For | Analogy |
|----------|-------------------|---------|
| **VWAP Reversion** | Price has moved far from its daily average and is due to snap back | A rubber band stretched too far — it tends to return to normal |
| **BB Bounce** | Price has hit the edge of its normal trading range (Bollinger Band) | A ball hitting the wall of a corridor — it bounces back |
| **Keltner Squeeze** | Volatility has compressed (bands narrowed) and is about to expand | A coiled spring — energy builds up, then releases in one direction |

---

## 3. Architecture Map

### Component Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    SnapshotRuntime (IBKR Layer)                  │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────────┐ │
│  │ EClientSocket│  │ EWrapper     │  │ IBrokerAdapter         │ │
│  │ (TWS API)    │  │ (callbacks)  │  │ (contract/order build) │ │
│  └──────┬───────┘  └──────┬───────┘  └────────────┬───────────┘ │
│         │                 │                       │             │
│         │    TopTickRow, DepthRow, HistoricalBarRow│             │
│         │                 │                       │             │
│  ┌──────▼─────────────────▼───────────────────────▼───────────┐ │
│  │                 StrategyDataSlice                           │ │
│  │  (L1 ticks + L2 depth + bars + positions + order events)   │ │
│  └──────────────────────────┬─────────────────────────────────┘ │
│                             │ OnDataAsync()                     │
│                             ▼                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │          ★ V3LiveRuntime (IStrategyRuntime)              │   │
│  │  ┌──────────────┐ ┌────────────────┐ ┌────────────────┐ │   │
│  │  │FeatureBuilder│ │ SignalEngine   │ │ OrderBridge    │ │   │
│  │  │ (indicators) │ │ (3 setups)     │ │ (sizing/price) │ │   │
│  │  └──────────────┘ └────────────────┘ └────────────────┘ │   │
│  │  ┌──────────────┐ ┌────────────────┐ ┌────────────────┐ │   │
│  │  │ RiskGuard    │ │ ScannerV2 Gate │ │ MTF Candle Eng │ │   │
│  │  │ (limits)     │ │ (eligibility)  │ │ (confirmation) │ │   │
│  │  └──────────────┘ └────────────────┘ └────────────────┘ │   │
│  │                             │                            │   │
│  │                    _orderIntents list                     │   │
│  │                             │                            │   │
│  │                    ┌────────▼────────┐                   │   │
│  │                    │  JSON EXPORT ✎  │  ← DEAD END      │   │
│  │                    │ (file on disk)  │                   │   │
│  │                    └─────────────────┘                   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  RunOrdersPlaceSimMode (Separate Order Pipeline)          │ │
│  │  CLI args → ResolveLiveOrderPlacementPlan → PlaceOrder    │ │
│  │  → Fill → ConductExitConfig → TryApplyPeakDrawdownExit   │ │
│  │  → [STP/LMT brackets] → ConductFlattenAsync (MKT close)  │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Key Interfaces

| Interface | Methods | Implementors |
|-----------|---------|--------------|
| `IStrategyRuntime` | `InitializeAsync`, `OnScheduledEventAsync`, `OnDataAsync`, `OnShutdownAsync` | `V3LiveRuntime`, `ScannerCandidateReplayRuntime`, `NullStrategyRuntime` |
| `IReplayOrderSignalSource` | (returns order signals to host) | `ScannerCandidateReplayRuntime` only — **V3LiveRuntime does NOT implement this** |
| `IBrokerAdapter` | `BuildContract`, `BuildOrder`, `PlaceOrder`, `CancelOrder` | `IbBrokerAdapter` |

---

## 4. File Inventory & Responsibilities

### V3Live Strategy Files

| # | File | Lines | Responsibility | Depends On |
|---|------|-------|----------------|------------|
| 1 | `Strategy/V3LiveConfig.cs` | 131 | All tunable parameters (48 properties). Environment variable overrides via `FromEnvironment()`. | None |
| 2 | `Strategy/V3LiveFeatureBuilder.cs` | 209 | Transforms raw `StrategyDataSlice` (L1 ticks, L2 depth, historical bars) into `V3LiveFeatureSnapshot` with computed indicators (ATR, RSI, VWAP, BB, KC, Stochastic, RVOL). | `TechnicalIndicators`, `BacktestBar` |
| 3 | `Strategy/V3LiveSignalEngine.cs` | 71 | Evaluates three sub-strategies (VWAP, BB, Squeeze) against feature snapshots. Resolves long/short conflicts via OFI tiebreaker. | `V3LiveFeatureSnapshot`, `V3LiveConfig` |
| 4 | `Strategy/V3LiveOrderBridge.cs` | 80 | Builds proposed order from signal: entry price from L1 quotes, ATR-based stops, risk-based position sizing, notional cap. | `V3LiveConfig`, `V3LiveFeatureSnapshot` |
| 5 | `Strategy/V3LiveRiskGuard.cs` | 71 | Pre-trade risk validation: daily loss limit, open risk cap, slippage estimate, L2 depth and imbalance checks. | `V3LiveConfig`, `V3LiveSymbolRiskState` |
| 6 | `Strategy/V3LiveRuntime.cs` | 573 | Orchestrator. Implements `IStrategyRuntime`. Wires all components together. Manages per-symbol state, scheduled events, pre-trade gates (session, cooldown, L1/L2 quality, scanner, MTF). Exports all artifacts to JSON on shutdown. | All above + `ScannerSelectionEngineV2`, `ReplayMtfCandleSignalEngine` |

### Original Backtest Reference

| File | Lines | Responsibility |
|------|-------|----------------|
| `Backtest/Strategies/StrategyV3.cs` | 255 | Backtest version of the same three sub-strategies. Uses `ExitEngine` for bar-by-bar trade simulation. Operates on `EnrichedBar[]` arrays (offline). |

### Supporting Infrastructure (Not Part of V3Live, But Used By It)

| File | Responsibility |
|------|----------------|
| `Strategy/IStrategyRuntime.cs` | 4-method lifecycle interface |
| `Strategy/StrategyRuntimeContracts.cs` | `StrategyRuntimeContext`, `StrategyDataSlice` records |
| `Strategy/ScannerSelectionEngineV2.cs` | Multi-factor scanner candidate scoring/ranking |
| `Strategy/ReplayMtfCandleSignals.cs` | Multi-timeframe (6 TF) bullish/bearish confluence filter |
| `Backtest/Indicators/TechnicalIndicators.cs` | ATR, RSI, VWAP, Bollinger Bands, Keltner Channels, Stochastic, RVOL |
| `Backtest/Strategies/ExitEngine.cs` | Backtest-only exit simulator (10-step exit chain) |
| `IBKR/Runtime/SnapshotRuntime.cs` | ~10,800 lines. IBKR connection, live order management, Conduct V1.2 exit engine |
| `IBKR/Broker/IbBrokerAdapter.cs` | IBKR TWS API wrapper |

---

## 5. Data Flow: Signal → Order → Fill → Exit

### Current State (As-Is)

```
Phase 1: Data Collection (SnapshotRuntime)
──────────────────────────────────────────
IBKR TWS API
  ├── tickPrice() / tickSize()  ──► TopTickRow
  ├── updateMktDepth()          ──► DepthRow
  └── historicalData()          ──► HistoricalBarRow
        │
        ▼
  StrategyDataSlice (bundled packet)
        │
        ▼

Phase 2: Strategy Evaluation (V3LiveRuntime)
──────────────────────────────────────────
  OnDataAsync(StrategyDataSlice)
    │
    ├── 1. FeatureBuilder.Build()     ──► V3LiveFeatureSnapshot
    │                                      (ATR=14, RSI=14, VWAP, BB%B,
    │                                       KC, Stoch, RVOL, L1, L2)
    │
    ├── 2. Pre-Trade Gates            ──► Pass / Reject with reason codes
    │       ├── Session window
    │       ├── Max entries per day
    │       ├── Cooldown timer
    │       ├── L1 quality (spread, size, staleness)
    │       ├── L2 quality (depth, imbalance)
    │       └── ScannerV2 eligibility
    │
    ├── 3. SignalEngine.Evaluate()    ──► V3LiveSignalDecision
    │       ├── VWAP Reversion check
    │       ├── BB Bounce check
    │       ├── Squeeze Breakout check
    │       └── Conflict resolution (OFI tiebreaker)
    │
    ├── 4. MTF Confirmation Gate      ──► Pass / Reject
    │       └── All 6 timeframes agree on direction?
    │
    ├── 5. OrderBridge.BuildOrder()   ──► V3LiveProposedOrder
    │       ├── Entry price from L1 (ask for long, bid for short)
    │       ├── Stop = entry ± HardStopR × ATR
    │       ├── Qty = min(risk-sized, notional-capped, maxShares)
    │       └── TP1 = entry ± Tp1R × ATR
    │
    ├── 6. RiskGuard.Evaluate()       ──► Pass / Reject
    │       ├── Daily loss limit ($300)
    │       ├── Open risk cap ($150)
    │       ├── Slippage check (15 bps max)
    │       └── L2 imbalance direction check
    │
    └── 7. _orderIntents.Add(order)   ──► STORED IN MEMORY
                                           │
                                           ▼
Phase 3: Export (OnShutdownAsync)     ──► JSON file on disk
                                           v3live_order_intents_{stamp}.json
                                           ← NOT SENT TO IBKR ───────── ✘

Phase X: ACTUAL Order Placement (SEPARATE PATH — Not Connected)
──────────────────────────────────────────
  RunOrdersPlaceSimMode()
    │
    ├── CLI args (--live-symbol NVDA --live-action BUY ...)
    ├── ResolveLiveOrderPlacementPlan()
    ├── Safety checks (notional, enable-live gate)
    ├── brokerAdapter.PlaceOrder(client, orderId, contract, order)  ──► IBKR TWS
    ├── Fill confirmation
    └── TryApplyPeakDrawdownExitAsync()   ──► Conduct V1.2 Monitor
          ├── E1: Immediate reversal overlay (5 sec)
          ├── E2: Hard stop (STP order at broker)
          ├── E3: Break-even activation (at 1R)
          ├── E4: Profit-lock (at 2R, guarantee 0.5R)
          ├── E5: Trailing stop (noise-floor adaptive)
          ├── E6: Giveback cap ($30 or 1% notional)
          ├── E7: Time stop (90 sec progress check)
          ├── E8: EOD flatten (15:55 ET)
          ├── E9: Safety overlay (kill switch, daily cap, disconnect)
          └── Flatten → ConductFlattenAsync() → MKT order → IBKR
```

### Desired State (To-Be) — After Refactoring

```
V3LiveRuntime._orderIntents  ──► IReplayOrderSignalSource interface
                                       │
                                       ▼
                              SnapshotRuntime reads intents
                                       │
                                       ▼
                              ExecuteLiveOrderPlacementPlanAsync()
                                       │
                                       ▼
                              brokerAdapter.PlaceOrder()  ──► IBKR TWS
```

---

## 6. Component-by-Component Analysis

### 6.1 V3LiveConfig (131 lines)

**Purpose:** Centralized configuration for all tunable parameters.

**Strengths:**
- ✅ Immutable `record` type — safe from accidental mutation
- ✅ Environment variable overrides via `FromEnvironment()` — deployable without recompilation
- ✅ Input validation via `Math.Clamp` / `Math.Max` on all numeric parameters
- ✅ Sensible defaults for paper trading ($25K account, $30 risk per trade)

**Weaknesses:**
- ⚠️ **48 parameters** is dense — no logical grouping (risk params mixed with signal params mixed with session params)
- ⚠️ All helper methods (`Read`, `ReadBool`, `ReadInt`, `ReadDouble`) are **local functions** inside `FromEnvironment()` — duplicated parsing logic that could be extracted
- ⚠️ No **validation** beyond per-field clamping — e.g., `Tp2R > Tp1R` is not enforced, `BreakevenR < HardStopR` is not enforced
- ⚠️ No **config dump/print** utility for operational logging

**Parameter Concerns:**

| Parameter | Default | Concern |
|-----------|---------|---------|
| `MaxShares` | 10,000 | Very high for a $25K account |
| `MaxHoldBars` | 45 | Unit is "bars" — but bar duration is undefined in live context (depends on data push rate) |
| `SessionEndUtc` | "20:00" | US market closes at 20:00 UTC but Conduct V1.2's EOD flatten is at 15:55 ET ≈ 19:55 UTC — 5 minute gap |
| `HardStopR` | 1.0 | Tighter than backtest (1.5R) — see §7 |

---

### 6.2 V3LiveFeatureBuilder (209 lines)

**Purpose:** Transforms raw market data into a feature vector for signal evaluation.

**Strengths:**
- ✅ Clean separation: raw data → feature snapshot → signal decision
- ✅ Defensive: returns `IsReady: false` when `bars.Length < 30`
- ✅ Full indicator suite: ATR(14), RSI(14), VWAP, BB(20,2), KC(20,14,1.5), Stochastic(14,3,3), RVOL(20)
- ✅ Squeeze detection: BB inside KC (classic Squeeze indicator)

**Weaknesses:**
- 🔴 **Full indicator recomputation every tick** — calls `TechnicalIndicators.Atr(bars, 14)`, `Rsi(closes, 14)`, etc. on the ENTIRE bar history every time `Build()` is called. No incremental computation.
  - **Performance impact:** For 390 bars (full trading day of 1-min candles), this means ~2,340 array traversals per tick per symbol.
- ⚠️ **L2 depth snapshot logic** takes `latest.Take(500)` rows then filters by side — the 500-row cutoff is arbitrary and may drop valid depth levels under heavy update rates
- ⚠️ **OFI (Order Flow Imbalance)** is computed only from `topBidSize` and `topAskSize` (position 0) — not a true cumulative OFI across all levels
- ⚠️ **`ResolveTopPrice`** takes the first row matching a field, but `TopTickRow` may contain stale data — no timestamp proximity check
- ❓ **Volume acceleration** (`volAccel`) uses only the last 2 bars — highly noisy for a single-bar look-back

**Indicator Chain:**

| Indicator | Parameters | Source | Notes |
|-----------|-----------|--------|-------|
| ATR | period=14 | Historical bars | Standard |
| RSI | period=14 | Closes | Standard |
| VWAP | — | Historical bars | Intraday cumulative |
| Bollinger Bands | period=20, stddev=2.0 | Closes | Standard |
| Keltner Channels | ema=20, atr=14, mult=1.5 | Historical bars | Standard |
| Stochastic | K=14, D=3, smooth=3 | Historical bars | Standard |
| Relative Volume | period=20 | Volumes | Ratio vs. 20-bar average |

---

### 6.3 V3LiveSignalEngine (71 lines)

**Purpose:** Evaluates three technical setups and produces a trade signal.

**Strengths:**
- ✅ Simple, readable logic — each setup is 2-3 lines
- ✅ Conflict resolution via OFI tiebreaker when both long and short fire
- ✅ RVOL filter (≥ 0.5) prevents signals in low-volume conditions

**Weaknesses:**
- 🔴 **Missing VWAP direction sign check** — Backtest's StrategyV3 checks `distFromVwap < -VwapStretchAtr` for longs and `> VwapStretchAtr` for shorts. V3Live checks `<= -cfg.VwapStretchAtr` and `>= cfg.VwapStretchAtr`. The `<=` vs `<` difference is minor but the **absence of asymmetric ATR handling** means the live version may trigger on exact boundary values where the backtest wouldn't.
- 🔴 **BB Bounce logic diverged from backtest** — Backtest requires price > Open OR stochK < 25 with stochK > stochD. Live version uses simplified stochK >= stochD without the stochK < 25 threshold. This means **live fires on weaker confirmation** than backtest.
- ⚠️ **Squeeze Breakout logic diverged** — Backtest tracks cumulative squeeze bar count (≥ 10 bars) and only triggers on the **fire bar** (transition out of squeeze). Live version triggers **while** squeeze is active (`f.SqueezeOn && price > KcMid`). This is a fundamentally different signal:
  - **Backtest:** "Squeeze just released → breakout trade"
  - **Live:** "Squeeze is active → anticipation trade"
- ⚠️ **No HtfGuard** — Backtest has a Higher Timeframe guard that blocks longs in STRONG_BEAR and shorts in STRONG_BULL. Live version has no equivalent filter.
- ⚠️ **OFI tiebreaker threshold** of 0.05 is hardcoded and not configurable

**Signal Logic Comparison Table:**

| Setup | Backtest (StrategyV3) | Live (V3LiveSignalEngine) | Status |
|-------|----------------------|--------------------------|--------|
| VWAP Long | dist < -stretch AND rsi < oversold AND ofi > 0 | dist <= -stretch AND rsi <= oversold AND ofi > 0 | ⚠️ Sign boundaries differ |
| VWAP Short | dist > stretch AND rsi > overbought AND ofi < 0 | dist >= stretch AND rsi >= overbought AND ofi < 0 | ⚠️ Sign boundaries differ |
| BB Long | pctB < low AND (price > open OR (stochK < 25 AND stochK > stochD)) | pctB <= low AND stochK >= stochD | 🔴 Missing strong confirm |
| BB Short | pctB > high AND (price < open OR (stochK > 75 AND stochK < stochD)) | pctB >= high AND stochK <= stochD | 🔴 Missing strong confirm |
| Squeeze Long | squeeze released after ≥10 bars AND price > KcMid | squeezeOn AND price > KcMid AND ofi > 0 | 🔴 Fundamentally different |
| Squeeze Short | squeeze released after ≥10 bars AND price < KcMid | squeezeOn AND price < KcMid AND ofi < 0 | 🔴 Fundamentally different |
| HTF Guard | Yes (STRONG_BULL/BEAR blocking) | **No** | 🔴 Missing |
| RVOL Filter | rvol >= 0.5 | rvol >= 0.5 (with NaN fallback to true) | ✅ Consistent |

---

### 6.4 V3LiveOrderBridge (80 lines)

**Purpose:** Converts a signal + features into a concrete proposed order with sizing.

**Strengths:**
- ✅ Risk-based sizing: `qty = floor(RiskPerTrade / riskPerShare)`
- ✅ Dual cap: `min(risk-sized, notional-capped, maxShares)`
- ✅ Entry price uses L1 ask (for longs) / bid (for shorts) — realistic pricing
- ✅ Guards against `riskPerShare < MinRiskPerShare`

**Weaknesses:**
- ⚠️ **LMT order type is always "LMT"** but Conduct V1.2 expects MKT orders for exits — the entry type may conflict if ever wired to live placement
- ⚠️ **TP2 is not included** in the proposed order — only TP1 is calculated. TP2 exists in config (`Tp2R = 1.8`) but is unused in the bridge
- ⚠️ **No slippage estimation** on the entry price — slippage is only checked later in RiskGuard, but the entry price itself is not adjusted
- ⚠️ **No partial fill handling** — assumes fill-or-kill semantics

---

### 6.5 V3LiveRiskGuard (71 lines)

**Purpose:** Pre-trade risk validation gate.

**Strengths:**
- ✅ Daily loss limit check (hard $300 cap)
- ✅ Open risk accumulation tracking
- ✅ Slippage BPS estimation from mid price
- ✅ L2 imbalance direction alignment

**Weaknesses:**
- 🔴 **`RealizedPnlToday` is never updated** — `V3LiveSymbolRiskState.RealizedPnlToday` starts at 0.0 and no code anywhere sets it to a non-zero value. The daily loss limit check will **never trigger**.
- 🔴 **`OpenRiskDollars` is only incremented, never decremented** — when an order intent is accepted, `symbolState.RiskState.OpenRiskDollars += proposed.EstimatedRiskDollars` is called, but exits/closes/cancels never reduce it. After 5 accepted intents at $30 risk each = $150, the guard permanently blocks all further orders.
- ⚠️ **Risk state is per-symbol** (`_stateBySymbol`) but `MaxDailyLossDollars` and `MaxOpenRiskDollars` are **account-wide** config values checked at symbol level — a 4-symbol setup could theoretically accept 4 × $150 = $600 in open risk
- ⚠️ **No position-level risk check** — doesn't validate against actual IBKR positions (from `StrategyDataSlice.Positions`)

---

### 6.6 V3LiveRuntime — Orchestrator (573 lines)

**Purpose:** Wires all components together. Implements the `IStrategyRuntime` lifecycle.

**Strengths:**
- ✅ Clean lifecycle: initialize → process data → shutdown with export
- ✅ Comprehensive pre-trade gate chain (11 gate checks)
- ✅ Full audit trail: every evaluation tick is logged with all feature values
- ✅ Scheduled event handling: close-only mode, session-open reset
- ✅ ScannerV2 integration as optional gate

**Weaknesses:**
- 🔴 **Symbol resolution bug** — `ResolveSymbol()` returns `_context.Symbol` if set (single symbol from CLI), otherwise returns `_config.Symbols.FirstOrDefault()`. When processing multi-symbol data, all ticks are attributed to a **single symbol**. The `dataSlice` itself doesn't carry a symbol field, and `_context.Symbol` is one value.
- 🔴 **Order intents are orphaned** — `_orderIntents` is a list that's only ever read during `OnShutdownAsync()` for JSON export. No method exposes it to `SnapshotRuntime`.
- ⚠️ **`EvaluateScannerV2Gate` creates a synthetic candidate** with `WeightedScore = 100.0` and `Eligible = true` — this bypasses the scanner file scoring system and only uses the L1/L2 gate portion of ScannerV2. The "scanner gate" name is misleading.
- ⚠️ **MTF confirmation is overly restrictive** — requires ALL 6 timeframes (30s, 1m, 5m, 15m, 1h, 1d) to agree. In practice this rarely aligns, especially with the 1d timeframe. The `AllowMtfUnready = true` default mitigates this by allowing signals when the MTF engine hasn't built all timeframes yet, which is a workaround that reduces the gate to a no-op during warm-up.
- ⚠️ **`passedPreTrade` is recalculated twice** (line ~170 and ~194) — the second assignment overwrites the first, making the first one dead code.
- ⚠️ **No position tracking** — the runtime doesn't track whether it already has an open position. It could generate multiple entry signals for the same symbol (mitigated only by `MaxEntriesPerSymbolPerDay`).

---

## 7. Backtest vs. Live: Divergence Analysis

### Parameter Defaults Comparison

| Parameter | Backtest (V3Config) | Live (V3LiveConfig) | Drift | Impact |
|-----------|--------------------|--------------------|-------|--------|
| RiskPerTrade | $50 | $30 | -40% | Smaller positions live |
| HardStopR | 1.5 | 1.0 | -33% | Tighter stops live → more stop-outs |
| BreakevenR | 0.8 | 0.5 | -38% | Earlier BE activation live |
| TrailR | 1.0 | 0.4 | -60% | Much tighter trail live |
| GivebackPct | 0.60 | 0.30 | -50% | Less giveback allowed live |
| Tp1R | 1.0 | 0.9 | -10% | Slightly earlier TP1 live |
| Tp2R | 2.5 | 1.8 | -28% | Earlier TP2 live |
| MaxHoldBars | 90 | 45 | -50% | Half the hold time live |
| BbEntryPctbLow | 0.05 | 0.12 | +140% | Less extreme BB trigger live |
| BbEntryPctbHigh | 0.95 | 0.88 | -7% | Less extreme BB trigger live |

**Net effect:** The live version is significantly more **conservative in exit management** (tighter stops, less giveback, shorter hold) but more **aggressive in entry** (wider BB thresholds catch more setups).

> ⚠️ **These parameter drifts mean that backtest results are NOT representative of live performance.** Any backtest validation done with `StrategyV3` default parameters does not predict what `V3LiveRuntime` would do in production.

### Structural Differences

| Feature | Backtest | Live | Notes |
|---------|----------|------|-------|
| Exit system | `ExitEngine` (10-step bar-by-bar) | `ConductExitConfig` (real-time monitor) | Completely different engines |
| Slippage model | Fixed 1.5¢ per share | Estimated from L1 mid (BPS check) | Different methodology |
| Commission model | $0.005/share deducted | Not modeled | Live ignores commission |
| Price filter | $8 – $50 range | None | Live has no price range filter |
| L2 data source | Synthetic proxy (L2Liquidity, SpreadZ) | Real IBKR depth | Different fidelity |
| HTF Guard | 1h + 1d bias filter | None | Missing in live |
| Squeeze detection | Cumulative bar count ≥ 10 | Instantaneous boolean | Different semantics |
| Position tracking | Full `BacktestTradeResult` lifecycle | No position tracking | Missing in live |
| Multi-symbol | Sequential per symbol | Single-symbol resolution bug | See §8 |

---

## 8. Critical Findings

### CRITICAL-01: Order Intents Are Not Wired to IBKR

**Severity:** 🔴 CRITICAL  
**Impact:** V3Live cannot execute trades autonomously  

**Description:** `V3LiveRuntime._orderIntents` is a `List<V3LiveProposedOrder>` that accumulates order intents during the session. These are exported to `v3live_order_intents_{stamp}.json` during `OnShutdownAsync()`. However:

1. `IStrategyRuntime` has no method to return order intentions to the host
2. `SnapshotRuntime` checks `if (_strategyRuntime is IReplayOrderSignalSource)` but V3LiveRuntime does **not** implement `IReplayOrderSignalSource`
3. The actual IBKR order placement in `ExecuteLiveOrderPlacementPlanAsync` reads from CLI args and scanner files — never from V3LiveRuntime

**Evidence:** Search for any consumer of `_orderIntents` outside `OnShutdownAsync` — none exists.

**Recommendation:** Implement `IReplayOrderSignalSource` on V3LiveRuntime, or create a new `ILiveOrderSignalSource` interface that SnapshotRuntime can poll after each `OnDataAsync` call.

---

### CRITICAL-02: RealizedPnlToday Is Never Updated

**Severity:** 🔴 CRITICAL  
**Impact:** Daily loss limit will never trigger  

**Description:** `V3LiveSymbolRiskState.RealizedPnlToday` defaults to 0.0 and is never modified anywhere in the codebase. The risk guard check `state.RealizedPnlToday <= -Math.Abs(_config.MaxDailyLossDollars)` (i.e., `0.0 <= -300.0`) is always `false`.

**Evidence:** `V3LiveSymbolRiskState` is a plain class:
```csharp
public sealed class V3LiveSymbolRiskState
{
    public double OpenRiskDollars { get; set; }     // Only incremented
    public double RealizedPnlToday { get; set; }    // Never set
}
```

**Recommendation:** Feed realized PnL from `StrategyDataSlice.Positions` or IBKR PnL callbacks into the risk state on every tick.

---

### CRITICAL-03: OpenRiskDollars Never Decreases

**Severity:** 🔴 HIGH  
**Impact:** After a few accepted intents, the risk guard permanently blocks all further entries  

**Description:** When an order intent passes the risk guard:
```csharp
symbolState.RiskState.OpenRiskDollars += proposed.EstimatedRiskDollars;
```
But there is no corresponding decrement when a position is closed, cancelled, or stopped out. After 5 orders at $30 risk each ($150 total), `MaxOpenRiskDollars` ($150) is permanently reached.

**Recommendation:** Track position lifecycle events from `StrategyDataSlice.CanonicalOrderEvents` and decrement `OpenRiskDollars` on fills, cancels, and exits.

---

### HIGH-01: Single-Symbol Resolution Bug

**Severity:** ⚠️ HIGH  
**Impact:** Multi-symbol V3Live config (4 symbols by default) processes all data under one symbol name  

**Description:** `ResolveSymbol()` at line 356:
```csharp
private string ResolveSymbol(StrategyDataSlice dataSlice)
{
    if (_context is not null && !string.IsNullOrWhiteSpace(_context.Symbol))
        return _context.Symbol.Trim().ToUpperInvariant();
    return _config.Symbols.FirstOrDefault() ?? string.Empty;
}
```
`_context.Symbol` is a single string. If set, all ticks for all symbols are attributed to this one symbol. If not set, all ticks default to the first configured symbol ("NVDA"). `StrategyDataSlice` does not carry a `.Symbol` field.

**Recommendation:** Either:
- Add `Symbol` field to `StrategyDataSlice` and use it in `ResolveSymbol()`
- Or restructure SnapshotRuntime to call `OnDataAsync` per-symbol with the context primed

---

### HIGH-02: Squeeze Signal Semantic Mismatch

**Severity:** ⚠️ HIGH  
**Impact:** Live squeeze signals fire during squeeze (anticipation), backtest fires on breakout (release)  

**Description:** Backtest tracks `squeezeCount++` while BB is inside KC, then only generates a signal on the **first bar after exit** (`wasSqueezed = squeezeCount >= 10`). Live checks `f.SqueezeOn` (squeeze is active NOW) and triggers immediately. These are opposite market conditions:
- Backtest: "Volatility just expanded — ride the breakout"
- Live: "Volatility is compressed — bet on future expansion"

**Recommendation:** Add a `_previousSqueezeState` tracker per symbol in `V3LiveRuntime` and only emit squeeze signals on the transition from `true → false` (matching backtest behavior).

---

### MEDIUM-01: Full Indicator Recomputation Per Tick

**Severity:** ⚠️ MEDIUM  
**Impact:** CPU overhead scales with bar count × tick rate × symbol count  

**Description:** `V3LiveFeatureBuilder.Build()` computes all 7 indicators from scratch on the full historical bar array every call. For a 390-bar session with 4 symbols receiving data every second, this is ~5,600 full indicator computations per minute.

**Recommendation:** Implement incremental indicator computation (rolling ATR, incremental RSI, etc.) or cache+invalidate pattern.

---

### MEDIUM-02: Dead Code — Double passedPreTrade Assignment

**Severity:** ⚠️ LOW  
**Impact:** Cosmetic — first assignment is overwritten  

**Description:** In `OnDataAsync()`:
```csharp
var passedPreTrade = reasonCodes.Count == 0;       // Line ~170

// ... MTF confirmation may add to reasonCodes ...

passedPreTrade = reasonCodes.Count == 0;            // Line ~194 (overwrites)
```

The first assignment at line ~170 is dead code.

**Recommendation:** Remove the first `passedPreTrade` assignment or move it after all gate checks are complete.

---

## 9. Risk Assessment Matrix

| # | Finding | Probability | Impact | Risk Level | Effort to Fix |
|---|---------|-------------|--------|------------|---------------|
| C-01 | Order intents not wired | Certain | Blocking | 🔴 CRITICAL | Medium (interface + bridge) |
| C-02 | Daily loss limit dead | Certain | High | 🔴 CRITICAL | Low (wire PnL data) |
| C-03 | Open risk ratchets up | Certain | Medium | 🔴 HIGH | Low (decrement on close) |
| H-01 | Single-symbol resolution | Likely | Medium | ⚠️ HIGH | Medium (data model change) |
| H-02 | Squeeze semantics differ | Certain | Medium | ⚠️ HIGH | Low (add state tracker) |
| M-01 | Indicator recomputation | Certain | Low-Med | ⚠️ MEDIUM | High (incremental impl.) |
| M-02 | Dead code passedPreTrade | Certain | None | ⚠️ LOW | Trivial |
| — | BB Bounce weaker confirm | Certain | Low | ⚠️ MEDIUM | Low (add stochK threshold) |
| — | No HTF Guard in live | Certain | Low-Med | ⚠️ MEDIUM | Medium (port from backtest) |
| — | No price range filter | Certain | Low | ⚠️ LOW | Low (add config params) |
| — | Config param drift | Certain | High | ⚠️ HIGH | Low (align defaults) |
| — | No commission model | Certain | Low | ⚠️ LOW | Low (add to bridge) |

---

## 10. Configuration Audit

### Environment Variable Coverage

All 48 parameters are overridable via `V3LIVE_*` environment variables. Each has:
- A well-named env var key
- A sensible default
- Bounds checking via `Math.Max` / `Math.Clamp`

### Missing Cross-Parameter Validation

| Constraint | Current | Should Be |
|------------|---------|-----------|
| `Tp2R > Tp1R` | Not validated | Required (TP2 must be farther than TP1) |
| `BreakevenR < HardStopR` | Not validated | Required (BE must activate before stop) |
| `TrailR < HardStopR` | Not validated | Required (trail must be tighter than stop) |
| `SessionStartUtc < SessionEndUtc` | Not validated | Required (start before end) |
| `MaxOpenRiskDollars ≤ MaxDailyLossDollars` | Not validated | Recommended |
| `RiskPerTradeDollars ≤ MaxOpenRiskDollars` | Not validated | Recommended |

### Security Review

- ✅ No secrets in config
- ✅ No file system access beyond `exports/` directory
- ✅ No network access from strategy layer
- ⚠️ Environment variables are readable by any process on the machine — not a concern for paper trading, but note for production

---

## 11. Code Quality & Maintainability

### Positive Patterns

| Pattern | Where | Assessment |
|---------|-------|------------|
| Immutable records | All snapshot/decision types | ✅ Excellent — thread-safe, debuggable |
| Single Responsibility | Each file has one job | ✅ Good decomposition |
| Config injection | Constructor DI throughout | ✅ Testable |
| Comprehensive audit trail | Evaluations, signals, risk events, intents | ✅ Excellent operational visibility |
| Defensive coding | NaN checks, empty array guards, null coalescence | ✅ Robust |

### Negative Patterns

| Pattern | Where | Assessment |
|---------|-------|------------|
| God class | `SnapshotRuntime.cs` (10,800 lines) | 🔴 Unmaintainable |
| Feature envy | `EvaluateScannerV2Gate` in V3LiveRuntime creates `ScannerV2CandidateFileRow` with fake data | ⚠️ Coupling to scanner internals |
| Missing abstraction | No `IOrderSignalSource` for strategy → host communication | 🔴 Blocks key feature |
| Stringly typed | `eventName`, `Side`, `Setup`, `RejectReason` all strings | ⚠️ Error-prone |
| Temporal coupling | `InitializeAsync` must be called before `OnDataAsync` — no state machine enforcement | ⚠️ Fragile lifecycle |
| Naming inconsistency | `_closeOnly` (field) vs `CloseOnlyMode` (record prop) vs `close-only-mode` (reason code) | ⚠️ Cognitive load |

### Test Coverage

No unit tests found for V3Live components. The `Backtest/Strategies/` folder has `StrategyV3.cs` but no corresponding test files were discovered.

**Recommendation:** Prioritize tests for:
1. `V3LiveSignalEngine.Evaluate()` — pure function, easy to test
2. `V3LiveRiskGuard.Evaluate()` — pure function with clear pass/fail
3. `V3LiveOrderBridge.BuildOrder()` — sizing logic
4. `V3LiveFeatureBuilder.Build()` — indicator computation accuracy

---

## 12. Refactoring Recommendations

### Priority 1 — CRITICAL (Pre-Production Blockers)

| # | Action | Estimated Effort | Files Changed |
|---|--------|-----------------|---------------|
| R-01 | **Wire V3Live order intents to IBKR.** Create `ILiveOrderSignalSource` interface with `IReadOnlyList<V3LiveProposedOrder> ConsumeAcceptedIntents()`. Implement on V3LiveRuntime. Add SnapshotRuntime consumer. | 3-5 days | 3 files |
| R-02 | **Fix daily PnL tracking.** Read realized PnL from `StrategyDataSlice.Positions` or account data. Update `V3LiveSymbolRiskState.RealizedPnlToday` in `OnDataAsync`. | 1 day | 2 files |
| R-03 | **Fix open risk lifecycle.** Decrement `OpenRiskDollars` when positions close (from position data or order events). | 1 day | 2 files |
| R-04 | **Fix symbol resolution.** Add `Symbol` property to `StrategyDataSlice` (breaking change) or extract from tick data. | 2 days | 4-5 files |

### Priority 2 — HIGH (Backtest-Live Parity)

| # | Action | Estimated Effort | Files Changed |
|---|--------|-----------------|---------------|
| R-05 | **Fix squeeze signal semantics.** Add per-symbol `bool previousSqueezeOn` state. Only emit signal on `prev=true → now=false` transition. Add configurable `MinSqueezeBarCount`. | 0.5 day | 2 files |
| R-06 | **Restore BB confirmation logic.** Add `stochK < 25` threshold for long and `stochK > 75` for short, matching backtest. | 0.5 day | 1 file |
| R-07 | **Port HTF Guard.** Port `HtfGuard()` from StrategyV3. Feed 1h/1d bars from `StrategyDataSlice.HistoricalBars` (may need SnapshotRuntime to provide multi-timeframe bars). | 2 days | 2-3 files |
| R-08 | **Align config defaults.** Create a `V3LiveConfig.FromBacktestDefaults()` factory for parity testing. Document intentional divergences. | 0.5 day | 1 file |

### Priority 3 — MEDIUM (Performance & Quality)

| # | Action | Estimated Effort | Files Changed |
|---|--------|-----------------|---------------|
| R-09 | **Incremental indicators.** Replace batch `TechnicalIndicators.*` calls with streaming/incremental versions that update on new bars only. | 3-5 days | 2-3 files |
| R-10 | **Config validation.** Add `Validate()` method to `V3LiveConfig` that checks cross-parameter constraints (Tp2R > Tp1R, etc.). Call in constructor. | 0.5 day | 1 file |
| R-11 | **Replace magic strings.** Create enums for event names, reason codes, setup names. | 1 day | 3-4 files |
| R-12 | **Unit tests.** Write tests for SignalEngine, RiskGuard, OrderBridge, FeatureBuilder. | 3-5 days | 4 new files |
| R-13 | **Config grouping.** Split `V3LiveConfig` into sub-records: `SignalConfig`, `RiskConfig`, `SessionConfig`, `L1L2Config`, `ExitConfig`. | 1 day | 6 files |
| R-14 | **Remove dead code.** Eliminate first `passedPreTrade` assignment. | 5 min | 1 file |

### Priority 4 — LOW (Nice to Have)

| # | Action | Estimated Effort | Files Changed |
|---|--------|-----------------|---------------|
| R-15 | **Config dump utility.** Add `V3LiveConfig.ToDisplayString()` for startup logging. | 0.5 day | 1 file |
| R-16 | **Price range filter.** Add optional `MinPrice`/`MaxPrice` to V3LiveConfig (matching backtest's $8-$50 filter). | 0.5 day | 2 files |
| R-17 | **Commission model.** Add `CommissionPerShare` to V3LiveConfig and deduct in order bridge PnL estimation. | 0.5 day | 2 files |

---

## Appendix A — Full Config Parameter Reference

### Signal Engine Parameters

| Parameter | Type | Default | Env Var | Description |
|-----------|------|---------|---------|-------------|
| `VwapStretchAtr` | double | 1.5 | `V3LIVE_VWAP_STRETCH_ATR` | ATR multiples from VWAP to trigger reversion |
| `BbEntryPctbLow` | double | 0.12 | `V3LIVE_BB_PCTB_LOW` | BB %B lower threshold for long entry |
| `BbEntryPctbHigh` | double | 0.88 | `V3LIVE_BB_PCTB_HIGH` | BB %B upper threshold for short entry |
| `RsiOversold` | double | 35.0 | `V3LIVE_RSI_OVERSOLD` | RSI below this = oversold confirmation |
| `RsiOverbought` | double | 65.0 | `V3LIVE_RSI_OVERBOUGHT` | RSI above this = overbought confirmation |

### Risk Management Parameters

| Parameter | Type | Default | Env Var | Description |
|-----------|------|---------|---------|-------------|
| `RiskPerTradeDollars` | double | 30.0 | `V3LIVE_RISK_PER_TRADE` | Max dollar risk per trade (position sizing input) |
| `MaxDailyLossDollars` | double | 300.0 | `V3LIVE_MAX_DAILY_LOSS` | Daily loss circuit breaker (**currently non-functional — see C-02**) |
| `MaxOpenRiskDollars` | double | 150.0 | `V3LIVE_MAX_OPEN_RISK` | Max concurrent exposed risk (**accumulates forever — see C-03**) |
| `AccountSize` | double | 25,000 | `V3LIVE_ACCOUNT_SIZE` | Account size for notional calculations |
| `MaxPositionNotionalPctOfAccount` | double | 0.25 | `V3LIVE_MAX_POSITION_NOTIONAL_PCT` | Max 25% of account in single position |
| `MaxShares` | int | 10,000 | `V3LIVE_MAX_SHARES` | Hard quantity cap |
| `MinRiskPerShare` | double | 0.01 | `V3LIVE_MIN_RISK_PER_SHARE` | Minimum risk/share to avoid micro-stops |
| `MaxSlippageBps` | double | 15.0 | `V3LIVE_MAX_SLIPPAGE_BPS` | Max estimated entry slippage in basis points |

### Market Microstructure Parameters

| Parameter | Type | Default | Env Var | Description |
|-----------|------|---------|---------|-------------|
| `RequireL2Depth` | bool | true | `V3LIVE_REQUIRE_L2` | Require order book depth data |
| `DepthLevels` | int | 5 | `V3LIVE_DEPTH_LEVELS` | Number of L2 levels to analyze |
| `MaxSpreadPct` | double | 0.015 | `V3LIVE_MAX_SPREAD_PCT` | Max bid-ask spread as % of mid |
| `MaxQuoteStalenessSeconds` | int | 2 | `V3LIVE_MAX_QUOTE_STALENESS_SECONDS` | Max age of L1 quote before stale |
| `MinTopQuoteSize` | double | 100 | `V3LIVE_MIN_TOP_QUOTE_SIZE` | Min shares at top of book |
| `MinDepthPerSideShares` | double | 1,500 | `V3LIVE_MIN_DEPTH_PER_SIDE` | Min aggregate depth per side |
| `MinImbalanceLong` | double | 1.10 | `V3LIVE_MIN_IMBALANCE_LONG` | Min bid/ask ratio for longs |
| `MaxImbalanceShort` | double | 0.90 | `V3LIVE_MAX_IMBALANCE_SHORT` | Max bid/ask ratio for shorts |

### Session & Throttle Parameters

| Parameter | Type | Default | Env Var | Description |
|-----------|------|---------|---------|-------------|
| `SessionStartUtc` | string | "13:35" | `V3LIVE_SESSION_START_UTC` | Trading window open (UTC) |
| `SessionEndUtc` | string | "20:00" | `V3LIVE_SESSION_END_UTC` | Trading window close (UTC) |
| `CooldownSeconds` | int | 20 | `V3LIVE_COOLDOWN_SECONDS` | Min seconds between signals per symbol |
| `MaxEntriesPerSymbolPerDay` | int | 3 | `V3LIVE_MAX_ENTRIES_PER_DAY` | Max entries per symbol per day |

### Exit Parameters (Used by OrderBridge, Not by Conduct V1.2)

| Parameter | Type | Default | Env Var | Description |
|-----------|------|---------|---------|-------------|
| `HardStopR` | double | 1.0 | `V3LIVE_HARD_STOP_R` | Stop distance in R-multiples of ATR |
| `BreakevenR` | double | 0.5 | `V3LIVE_BREAKEVEN_R` | R-multiple to activate break-even |
| `TrailR` | double | 0.4 | `V3LIVE_TRAIL_R` | Trailing stop distance in R |
| `GivebackPct` | double | 0.30 | `V3LIVE_GIVEBACK_PCT` | Max profit giveback % |
| `Tp1R` | double | 0.9 | `V3LIVE_TP1_R` | Take-profit 1 target in R |
| `Tp2R` | double | 1.8 | `V3LIVE_TP2_R` | Take-profit 2 target in R |
| `MaxHoldBars` | int | 45 | `V3LIVE_MAX_HOLD_BARS` | Max bars before time stop |

### Gate/Filter Parameters

| Parameter | Type | Default | Env Var | Description |
|-----------|------|---------|---------|-------------|
| `EmitOrderIntents` | bool | true | `V3LIVE_EMIT_ORDER_INTENTS` | Whether to generate order intents |
| `UseScannerSelectionV2Gate` | bool | true | `V3LIVE_USE_SCANNER_V2_GATE` | Use ScannerV2 as pre-trade gate |
| `ScannerMinCompositeScore` | double | 55.0 | `V3LIVE_SCANNER_MIN_SCORE` | Min scanner composite score |
| `RequireMtfConfirmation` | bool | true | `V3LIVE_REQUIRE_MTF_CONFIRMATION` | Require MTF alignment |
| `AllowMtfUnready` | bool | true | `V3LIVE_ALLOW_MTF_UNREADY` | Allow signals when MTF not ready |
| `Symbols` | string[] | NVDA,META,AMD,AAPL | `V3LIVE_SYMBOLS` | Comma-separated symbol list |

---

## Appendix B — Glossary

| Term | Definition |
|------|-----------|
| **ATR** | Average True Range — a measure of price volatility over N bars |
| **R-multiple** | Risk multiple — 1R = the initial risk per share (stop distance) |
| **VWAP** | Volume-Weighted Average Price — the intraday "fair value" line |
| **BB / Bollinger Bands** | A volatility envelope: middle line ± 2 standard deviations |
| **BB %B** | Where price sits within Bollinger Bands (0 = lower band, 1 = upper band) |
| **KC / Keltner Channels** | A volatility envelope: EMA(20) ± 1.5 × ATR(14) |
| **Squeeze** | When BB contracts inside KC — indicates compressed volatility |
| **OFI** | Order Flow Imbalance — (bid_size - ask_size) / (bid_size + ask_size) |
| **RSI** | Relative Strength Index — momentum oscillator (0-100) |
| **Stochastic (K/D)** | Momentum oscillator comparing close to high-low range |
| **RVOL** | Relative Volume — current volume vs. 20-bar average |
| **MTF** | Multi-Time-Frame — combining signals across 30s, 1m, 5m, 15m, 1h, 1d |
| **L1** | Level 1 — top-of-book bid/ask/last prices and sizes |
| **L2** | Level 2 — full order book depth (multiple price levels) |
| **Conduct V1.2** | The live position management engine inside SnapshotRuntime |
| **STP order** | Stop order — resting order that triggers at a specified price |
| **LMT order** | Limit order — order with a price cap/floor |
| **MKT order** | Market order — immediate execution at best available price |
| **BPS** | Basis points — 1 bps = 0.01% |
| **Notional** | Dollar value of a position (price × quantity) |
| **Dead code** | Code that is written but never executed / has no effect |

---

*End of Report — HARV-AUDIT-V3L-001*
