# v1_001_Harvester_API — Detailed Development Roadmap (Updated 2026-02-23)

## 1) Project Mission and Core Constraints

**Mission**  
Build a fully automated intraday day-trading application (“Harvester”) using IBKR Trader Workstation API, starting from scratch, with a mandatory self-learning capability and strict live-risk controls.

**Primary platform inputs**
- IBKR API docs baseline: https://interactivebrokers.github.io/tws-api/index.html
- IBKR API installation path: `C:\TWS API`
- Trader Workstation path: `C:\Jts`
- IB account number: `U22462030`

**Operating constraints (non-negotiable)**
- No paper-account phase for final rollout planning (live-first with micro-size controls).
- Initial live deployment must use very small size and low-priced liquid symbols.
- Hard pre-trade and runtime risk limits are mandatory.
- Learning updates must be rollback-safe and cannot destabilize live behavior.

---

## 2) Market Data Entitlements and Feed Baseline

Harvester v1 assumes the following account-side subscriptions are active and verifiable by API:

1. NYSE (Network A/CTA) — Broker billed, P/L1
2. NYSE American, BATS, ARCA, IEX, and Regional Exchanges (Network B) — P/L1
3. NASDAQ (Network C/UTP) — P/L1
4. NASDAQ TotalView-OpenView — P/L2

**NASDAQ TotalView-OpenView note**
- Provides depth-of-book for NASDAQ-traded securities.
- Requires base US equity feeds (A/B/C) or equivalent bundle prerequisites.
- Pricing reference: increased from USD 86.5 to USD 90 effective 2026-01-01.
- Current planning value: **USD 90.00/month**.

---

## 3) Starting Setup (Must be complete before running Harvester)

### 3.1 TWS install and runtime
- Trader Workstation installed at `C:\Jts`.
- TWS started and logged in before Harvester starts.

### 3.2 TWS API settings (critical)
In TWS: `File -> Global Configuration -> API -> Settings`
- Enable ActiveX and Socket Clients.
- Socket Port set correctly:
  - `7497` paper
  - `7496` live (default)
- For order placement, **Read-Only API must be OFF**.
- Local access enabled:
  - prefer localhost-only, or
  - trusted IP includes `127.0.0.1`.

### 3.3 API connectivity parameters
- Host: `127.0.0.1`
- Port: `7497` (paper) or `7496` (live)
- `clientId`: fixed integer per Harvester instance (stable identity)

### 3.4 Market data readiness
- Real-time API data requires active account subscriptions.
- Harvester must support fallback behavior when depth is temporarily unavailable.

### 3.5 Market data line budget awareness
- Default account budget is typically 100 concurrent market data lines (TWS + API combined).
- Exceeding budget causes silent/missing data and explicit errors.
- L2 symbol concurrency is also constrained.
- v1 live policy: start with one active trading symbol at a time.

### 3.6 Storage + strategy memory on day 1
Create from day 1:
- Raw market data store (ticks + L2 snapshots), recommended Postgres + Parquet.
- Trade journal store (orders/fills/PnL/R-multiples).
- Strategy memory store:
  - `memory_latest.json` for hot config and learned state
  - `memory_versions/` for rollback history

Startup behavior:
- load `memory_latest.json` to RAM before trading loop
- after each closed trade, update memory and persist

### 3.7 Pacing and request discipline
- Historical requests must respect IBKR pacing limits.
- During live trading, minimize historical requests; rely primarily on streaming feeds.

---

## 4) Starting Conditions (Harvester may trade ONLY if all are true)

### 4.1 Mode and health gates
- Running in intended mode (for initial live: micro-size only).
- Connection heartbeat is healthy.
- Market data freshness checks pass (no stale ticks).
- Order placement is allowed (Read-Only API OFF).

### 4.2 Time window gate
- Allowed v1 window: `09:30–11:15` New York time.
- Later extension optional after stability phase.

### 4.3 Candidate eligibility gate (scanner output)
A symbol is tradable only if all pass:
- Price in target range (v1 scanner and strategy ranges below).
- Spread acceptable.
- Liquidity/top-of-book not thin.
- No obvious halt/broken feed.

### 4.4 Data prerequisites before any order
For selected symbol, must have:
- L1 streaming active.
- L2 book built and stable for top N levels.
- Tick-by-tick tape active.

### 4.5 Strategy gates pass
- L2 imbalance gate pass.
- Tape aggression gate pass.
- Spread gate pass.
- Pullback/support pattern present.

### 4.6 Risk gates pass
Before every new entry:
- Daily loss limit not breached.
- Max open positions not exceeded.
- Max trades/day not exceeded.
- Position size from risk-per-trade formula is valid.

### 4.7 Learning/memory gates
After each trade close:
- Build training sample.
- Update learner and strategy memory.
- Run sanity checks; rollback if unstable.

### 4.8 Kill-switch triggers (immediate flatten/stop)
- TWS disconnect/reconnect loop.
- Data stale for configured threshold.
- Repeated order rejections.
- Pacing or market-data-line errors.

---

## 5) Strategy v1.0 Specification — LOB Momentum Pullback (LMP)

**Core idea**  
Trade the strongest scanner candidate when buying pressure (tape) + L2 support are confirmed, enter on controlled pullback, and exit quickly when microstructure flips.

### 5.1 Session and universe
- Primary session: `09:30–11:15 ET`.
- Optional later: `14:30–15:45 ET`.
- Scanner-first universe (not fixed watchlist).

Typical v1 filters:
- Price: `$2–$50`
- Spread: `<= $0.03` or `<= 0.08%` of price (tighter bound)
- Relative volume: `>= 3.0` vs rolling baseline
- Exclude halted and structurally broken names

### 5.2 Required data feeds
1. L1 via `reqMktData`
2. L2 via `reqMktDepth` (maintain local top N order book)
3. Tape via `reqTickByTickData` (`Last`/`AllLast`)
4. Optional contextual bars via `reqHistoricalData`

### 5.3 Core microstructure metrics

#### L2 imbalance (top N)
- `BidVol_N = sum(bid sizes levels 1..N)`
- `AskVol_N = sum(ask sizes levels 1..N)`
- `Imb = (BidVol_N - AskVol_N) / (BidVol_N + AskVol_N)`

v1 thresholds:
- Long bias: `Imb >= +0.20`
- Short bias: `Imb <= -0.20`

#### Spread/liquidity gate
- Spread must satisfy `Spread <= max(0.03, 0.0008 * Mid)`
- Combined near-book size must exceed minimum liquidity threshold

#### Tape aggression over window W (3–10s, default 5s)
- classify prints by bid/ask crossing
- `TapeImb = (BuyAggVol - SellAggVol) / (BuyAggVol + SellAggVol)`

v1 threshold:
- Long confirm: `TapeImb >= +0.15`

#### Replenishment heuristic (anti-fake)
- best bid holds or steps up and replenishes after getting hit
- positive signal when refill pattern repeats in short window

### 5.4 Long setup gates
All required:
- momentum context present (impulse move in recent minutes)
- price above VWAP (or validated proxy)
- no spread blowout
- imbalance pass
- tape pass
- spread gate pass
- replenishment positive (recommended)

### 5.5 Entry logic — pullback to support shelf
- Avoid top-chase entries.
- Pullback target zone: `30%–55%` retracement of impulse.
- Must hold above VWAP or stable L2 shelf.

Shelf definition (v1):
- bid level within top N with size `>= K * median(top-N bid size)`
- persistence `>= 1.5s`
- default `K = 2.5`

Trigger:
- price approaches shelf within 1–2 ticks
- shelf remains present
- tape recovers (`TapeImb >= 0`)

Entry order:
- limit buy at `min(Ask1, ShelfPrice + 1 tick)`
- cancel if no fill in 2–4s and microstructure weakens

### 5.6 Stop logic

Hard stop:
- below pullback swing low or shelf minus buffer
- buffer 1–3 ticks based on volatility

Microstructure early-exit (immediate):
- imbalance flips below `+0.05`
- tape flips below `-0.10`
- spread breaches threshold > 1s
- shelf disappears and bid steps down quickly

### 5.7 Profit-taking and time stop
- TP1 at `+1R`, scale 50%
- move stop to break-even (or slight buffer)
- TP2 at `+2R` or trail based on microstructure
- time stop: if not reaching `+0.5R` in 60–90s, exit

### 5.8 Risk model
Position sizing:
- `RiskPerTrade = Equity * r`, default `r = 0.25%`
- `Shares = floor(RiskPerTrade / StopDistance)`

Caps:
- max position value
- max shares absolute

Daily stop conditions:
- daily PnL <= `-1.0%` equity
- or 3 consecutive losses
- or critical connectivity/data faults

Trade-frequency limits (v1):
- max trades/day = 5
- max re-entries/symbol = 2
- cooldown after stop = 3 minutes

### 5.9 Order construction
- Start with bracket structure:
  - parent entry limit
  - child stop (STP)
  - child take-profit (LMT)
- exits managed via OCA behavior
- first implementation: single symbol, single bracket profile

---

## 6) Self-Learning Process (Most Important Chapter)

Self-learning is mandatory and must be treated as part of the execution safety system, not an optional analytics add-on.

### 6.1 Closed-loop objective
After each trade closes:
1. Build training sample(s)
2. Update learner/model or adaptive thresholds
3. Update strategy memory
4. Persist versioned memory
5. Reload hot memory into decision engine

Pipeline:
`Trade Closed -> FeatureBuilder -> LabelBuilder -> Learner.update() -> StrategyMemory.update() -> MemoryStore.save() -> StrategyEngine.reload(memory_latest)`

### 6.2 Mandatory per-trade data logging
Capture feature snapshots at:
- `T-3s`, `T-1s`, `Entry`, `+5s`, `+15s`, `Exit`

Minimum feature set:
- `Imb_N`, `TapeImb`, spread, mid
- bid/ask sizes for levels `1..N`
- shelf features (size ratio, persistence, distance)
- volatility proxy and impulse strength
- entry/stop/TP levels
- slippage and fill quality

Labels/outcomes:
- R-multiple
- MAE / MFE
- win/loss
- time-in-trade
- exit reason code
- setup reason code

### 6.3 Learning update modes (v1-safe)
Supported safe modes:
- incremental online update
- mini-batch update on recent K trades (recommended first)

Recommended initial policy:
- update every 5 closed trades using rolling batch (e.g., 50–200 samples)

### 6.4 Strategy memory model

A) Model memory
- model parameters/weights
- scaler/normalizer state
- decision thresholds

B) Pattern memory
- good setup fingerprints
- bad setup fingerprints
- optional symbol/session regime stats

C) Execution memory
- slippage by spread/volatility regime
- fill probability by order type
- default entry/stop offsets by regime

### 6.5 Hot + persistent memory architecture
Hot memory (RAM):
- strategy uses latest memory object for every decision

Persistent memory:
- `memory_latest.json`
- versioned snapshots in `memory_versions/`
- optional binary model artifact (`model_latest.bin`)

Durability policy:
- persist after each trade close (or every N closes with journaled writes)
- always keep rollback history

### 6.6 Memory influence on next trade
Before order placement:
1. compute live feature vector
2. query memory/model score or rule fingerprints
3. apply adaptive filters:
   - skip known failure patterns
   - tighten/loosen thresholds by regime
   - adjust execution offsets using learned slippage profile

### 6.7 Safety guardrails for learning
To avoid self-destruction by overfitting:
- Shadow apply: candidate memory must pass sanity checks first
- Sanity checks examples:
  - no degradation on rolling back-check sample
  - MAE increase within allowed bounds
- Rate-limit memory promotion cadence
- Keep 5–10 previous versions for instant rollback
- Auto-rollback trigger on rapid PnL deterioration after promotion

### 6.8 Learning acceptance criteria
- memory update is deterministic and reproducible from stored samples
- rollback works without restart risk
- next trade explicitly uses newly promoted memory version
- learning subsystem cannot bypass risk engine or kill-switches

---

## 7) Target System Outcomes (v1)

By v1 completion, Harvester must:
1. Maintain reliable TWS connectivity with recovery.
2. Ingest and normalize L1 + L2 + tape data.
3. Generate deterministic strategy intents.
4. Execute orders with complete lifecycle audit trail.
5. Enforce strict hard risk controls.
6. Run closed-loop self-learning with safe promotion/rollback.
7. Produce end-of-day reconciliation and performance diagnostics.

---

## 8) Milestone Plan (Revised)

## Milestone 0 — Foundation, Config, and Governance
- Project skeleton and environment separation (`dev`, `uat`, `live`)
- Config validation and startup blockers for missing critical values
- Security and logging baseline

Acceptance:
- health-check validates prerequisites and blocks unsafe startup

## Milestone 1 — Connectivity and Session Stability
- connection manager, heartbeat, reconnect and re-sync
- account verification for `U22462030`

Acceptance:
- survives TWS restart and resubscribes cleanly

## Milestone 2 — Market Data Pipeline (L1 + L2 + Tape)
- L1/L2/tape handlers
- local order-book model with sequencing/timestamps
- latency and gap instrumentation

Acceptance:
- stable continuous ingestion for selected symbols

## Milestone 3 — Strategy Engine (LMP v1.0)
- deterministic gate pipeline
- setup/entry/exit logic including time stop
- explainable structured decision logs

Acceptance:
- reproducible decisions from recorded streams

## Milestone 4 — Execution and Order Lifecycle
- bracket routing and state machine
- cancel/replace/idempotency
- slippage and fill-quality tracking

Acceptance:
- full lifecycle trace from intent to fill

## Milestone 5 — Risk Engine (Mandatory)
- pre-trade and runtime limits
- kill-switch states (`RUNNING`, `SAFE_MODE`, `HALT`)
- flatten-all procedure

Acceptance:
- any breach blocks new entries immediately

## Milestone 6 — Persistence, Audit, Replay
- append-only event records
- replay tooling for decision reproducibility
- daily archives

Acceptance:
- any trade traceable end-to-end

## Milestone 7 — Self-Learning Engine (Primary)
- feature/label builders
- learner update pipeline
- memory promotion and rollback framework
- shadow validation of updates

Acceptance:
- post-trade updates safely influence next trades
- rollback proven in drill scenarios

## Milestone 8 — Operations and Alerting
- real-time dashboard and critical alerts
- manual controls: pause, symbol pause, flatten-all

Acceptance:
- alert SLA and control drills pass

## Milestone 9 — Controlled Live Launch (Micro-Size)
Mandatory launch rules:
- very small share size
- strict whitelist and session window
- low max concurrent positions

Acceptance:
- 10+ stable trading days with zero control breaches

## Milestone 10 — Scale-Up and v1 Exit
- objective gates for expectancy, drawdown, slippage, and incidents
- signed scale policy and v1 exit review

---

## 9) Go/No-Go Checklist Before First Live Order

- TWS auto-login/session policy tested.
- API settings verified (including Read-Only OFF for trading mode).
- Required market-data subscriptions verified in-session.
- Data-line and depth-line budgets validated for active symbol count.
- Risk caps tested via forced-breach scenarios.
- Kill-switch and flatten-all tested end-to-end.
- Learning promotion/rollback tested in controlled replay.
- Logs, metrics, alerts, and runbook active.

---

## 10) KPI Dashboard (Daily)

- connection uptime %
- market data gap count and median latency
- orders sent/rejected/cancelled/filled
- fill ratio and average slippage
- gross/net intraday PnL
- max intraday drawdown
- risk-breach count by type
- learning update success rate and rollback count
- performance delta before/after memory promotions

---

## 11) Immediate Next Actions (Pause-Ready Backlog)

1. Freeze this roadmap revision as baseline.
2. Validate entitlement health checks for A/B/C + TotalView from API session.
3. Define initial live micro-risk constants in one configuration profile.
4. Finalize memory schema (`memory_latest.json` + versioned snapshots).
5. Draft detailed implementation spec for Milestone 7 (self-learning engine) before resuming development.

---

## 12) LEAN-Inspired Implementation Backlog (Concrete Tasks)

This backlog is derived from the detailed findings in:
- `docs/Lean_InteractiveBrokers_Detailed_Findings_2026-02-23.md`

### 12.1 Priority A — Adopt Now (highest ROI, lowest regret)

1. **IB error-policy module (`IbErrorPolicy`)**
  - Implement code groups: `ignore`, `warn`, `retry`, `hard-fail`.
  - Add per-code throttling window support.
  - Add structured origin context (`requestId`, request type, symbol/mode).
  - Acceptance:
    - repeated noisy codes are throttled,
    - hard-fail codes deterministically trigger kill-switch path,
    - exported diagnostics include request provenance.

2. **Connection state machine (`IbConnectionState`)**
  - Explicit states: `Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `Degraded`, `Halting`.
  - Gate strategy/order flow on state.
  - Acceptance:
    - state transitions are logged and replayable,
    - no order-placement path executes outside `Connected`.

3. **Request registry (`RequestRegistry`)**
  - Track lifecycle for blocking and async requests.
  - Fields: `requestId`, `type`, `startedAtUtc`, `deadlineUtc`, `origin`, `status`.
  - Acceptance:
    - all request timeout errors include original request metadata,
    - dangling requests are auto-cleaned.

4. **Heartbeat + reconnect policy hardening**
  - Add periodic connectivity probe and bounded retry ladder.
  - Tie reconnect behavior to IB connectivity message classes.
  - Acceptance:
    - disconnect drills restore session and subscriptions,
    - repeated failures trigger controlled `HALT`.

5. **Market-data emission normalization rules**
  - Normalize invalid/negative values and delayed-field variants.
  - Assemble quote/trade emissions from paired updates safely.
  - Acceptance:
    - no malformed ticks enter strategy pipeline,
    - unit tests cover out-of-order and sparse callback sequences.

### 12.2 Priority B — Adapt Later (high value, higher effort)

1. **Bidirectional order translation layer**
  - Isolate Lean-like translation boundaries in Harvester terms:
    - strategy intent -> IB order payload
    - broker callbacks -> normalized Harvester order events.
  - Include robust handling for trailing, MOO/MOC, and update paths.

2. **Contract/symbol normalization service**
  - Centralize security-type, exchange, and option/future mapping logic.
  - Add malformed contract fallback parsing where callback data is incomplete.

3. **FA routing strictness model**
  - Validate mutually exclusive FA routing fields and account/group semantics.
  - Ensure fill attribution checks match routing intent.

4. **Gateway supervision policy**
  - Weekly restart scheduling and pre-open health verification.
  - Controlled cool-down and restart backoff policy.

### 12.3 Priority C — Not-Now / Scope Guardrail (v1)

- Do not mirror LEAN-specific framework abstractions unrelated to Harvester runtime.
- Defer broad multi-leg combo expansion beyond required v1 instrument set.
- Keep complexity budget focused on reliability + risk + self-learning quality.

### 12.4 Implementation Sequence (recommended)

Wave 1 (Reliability baseline):
- `IbErrorPolicy`
- `IbConnectionState`
- `RequestRegistry`

Wave 2 (Data-quality baseline):
- market-data normalization and emission hardening
- reconnect subscription restore discipline

Wave 3 (Translation baseline):
- contract/symbol normalization
- order translation layer hardening

Wave 4 (Learning integration):
- feed adapter incident telemetry into self-learning memory features
- adaptive retry/escalation behavior under strict safety limits

### 12.5 Definition of Done for this backlog section

This section is complete only when:
- every Priority A item has a tracked implementation task and acceptance test,
- reconnect + stale-data + noisy-error drills are reproducibly passing,
- post-incident records are available for self-learning feature generation.

---

## 13) zipline-trader-Inspired Implementation Backlog (Concrete Tasks)

This backlog is derived from the detailed findings in:
- `docs/Zipline_Trader_Detailed_Findings_2026-02-23.md`

### 13.1 Priority A — Adopt Now (highest ROI, lowest regret)

1. **Unified broker adapter contract (`IBrokerAdapter`)**
  - Define one canonical interface for order, cancel, positions, portfolio/account snapshot, spot value, realtime bars, and health/time-skew.
  - Enforce adapter conformance through integration tests for all active broker modes.
  - Acceptance:
    - runtime services consume only the canonical adapter,
    - no broker-specific branching leaks into core execution loop.

2. **Order/execution/commission reconciliation worker**
  - Merge asynchronous broker streams into one canonical order ledger.
  - Resolve state from multiple sources (open orders, statuses, fills, commissions) deterministically.
  - Acceptance:
    - every closed order has reconciled fill + commission record,
    - reconciliation emits explicit diagnostics for unmatched events.

3. **Live runtime state persistence (`RuntimeStateStore`)**
  - Persist guarded runtime state checkpoints and restore on restart.
  - Add schema version + checksum and fail-safe fallback on corruption.
  - Acceptance:
    - restart resumes safely from latest valid snapshot,
    - invalid snapshots are quarantined without crashing runtime.

4. **Live lifecycle orchestration hardening**
  - Formalize startup/steady-state/shutdown phases for live loop.
  - Add broker-health and staleness monitors into lifecycle gates.
  - Acceptance:
    - orders can only route in healthy steady-state,
    - shutdown path is deterministic and flushes final state.

5. **Pluggable ingestion contract (vendor-neutral)**
  - Standardize extract -> normalize -> write pipeline interfaces for historical market data.
  - Keep vendor logic isolated from storage and runtime readers.
  - Acceptance:
    - at least one source path can be swapped without changing runtime consumers,
    - ingestion errors produce source-scoped diagnostics.

### 13.2 Priority B — Adapt Later (high value, higher effort)

1. **Realtime clock discipline with time-skew compensation**
  - Integrate exchange calendar timing with broker/server time-skew model.
  - Add out-of-hours behavior and day-boundary transitions with deterministic event sequencing.

2. **Trading-control DSL in live pre-trade path**
  - Port core controls into explicit validators (max notional, max qty, order count, session limits).
  - Include configurable violation actions (`warn`, `reject`, `halt`).

3. **Pre-trade cost/risk estimators**
  - Add slippage/commission estimation profiles for intent validation and safety checks.
  - Feed outcomes back into learning-quality telemetry.

4. **Broker metadata encoding for recovery**
  - Add compact metadata encoding on outbound orders (traceable origin + intent fields) to improve reconstruction after reconnects.

### 13.3 Priority C — Not-Now / Scope Guardrail (v1)

- Do not attempt Zipline API-compatibility as a product goal.
- Defer backtest-framework parity work unrelated to Harvester live runtime.
- Avoid importing legacy environment constraints from zipline-trader stack.

### 13.4 Implementation Sequence (recommended)

Wave 1 (Adapter and ledger baseline):
- `IBrokerAdapter`
- reconciliation worker
- canonical order ledger contracts

Wave 2 (Lifecycle and state baseline):
- runtime persistence and restore policy
- startup/steady-state/shutdown gate enforcement

Wave 3 (Data baseline):
- vendor-neutral ingestion contract
- source adapter normalization standards

Wave 4 (Risk and timing refinements):
- pre-trade controls and cost estimators
- realtime clock/time-skew hardening

### 13.5 Definition of Done for this backlog section

This section is complete only when:
- every Priority A item has an implementation task + acceptance test,
- restart/reconnect drills preserve runtime state and order-ledger consistency,
- at least one historical source adapter validates the vendor-neutral ingestion contract.

---

## 14) Notes

- The legacy IBKR docs above are acceptable as implementation baseline; verify callable behavior against installed `IBApi` assembly signatures in code.
- Since paper is not the intended rollout path, risk controls and kill-switch behavior are non-negotiable and must be validated before any scaling.
- For implementation questions and adapter design decisions, use `QuantConnect/Lean.Brokerages.InteractiveBrokers` and `shlomiku/zipline-trader` as standing references alongside IBKR docs.

---

## 15) Unified Execution Checklist (Ticket-Ready)

This checklist merges:
- Section 12: LEAN-inspired backlog
- Section 13: zipline-trader-inspired backlog

Use IDs directly as tracker tickets (Azure DevOps/Jira/GitHub Issues).

### 15.1 P0 — Reliability + Runtime Foundation (must finish first)

- **HV-001 — Implement `IbErrorPolicy` classification/throttling (LEAN)**
  - Depends on: none
  - Deliverable: code groups (`ignore/warn/retry/hard-fail`) + per-code throttle windows + structured context
  - Done when: noisy-code storms are throttled and hard-fail codes deterministically trigger halt path

- **HV-002 — Implement `IbConnectionState` state machine (LEAN)**
  - Depends on: none
  - Deliverable: explicit states (`Disconnected/Connecting/Connected/Reconnecting/Degraded/Halting`) and transition logging
  - Done when: order paths are gated to `Connected` only

- **HV-003 — Implement `RequestRegistry` for request lifecycle tracking (LEAN)**
  - Depends on: HV-001
  - Deliverable: request metadata + timeout cleanup + provenance in error/report payloads
  - Done when: timeout diagnostics include full originating request context

- **HV-004 — Define and enforce `IBrokerAdapter` contract (zipline)**
  - Depends on: none
  - Deliverable: canonical broker interface for order/cancel/orders/transactions/portfolio/account/spot/realtime/health
  - Done when: core runtime code has no broker-specific branching

- **HV-005 — Build order/execution/commission reconciliation worker (zipline + LEAN)**
  - Depends on: HV-003, HV-004
  - Deliverable: canonical order ledger derived from open orders + statuses + fills + commissions
  - Done when: every closed order has reconciled fill and commission record, plus mismatch diagnostics

### 15.2 P1 — Live Safety + Data Integrity

- **HV-006 — Heartbeat/reconnect ladder with controlled halt escalation (LEAN)**
  - Depends on: HV-002, HV-003
  - Deliverable: periodic probe, bounded retries, reconnect policy bound to connectivity-code classes
  - Done when: disconnect drills restore session/subscriptions or enter controlled halt

- **HV-007 — Market-data normalization and emission hardening (LEAN)**
  - Depends on: HV-001
  - Deliverable: tick sanitation, delayed-field handling, paired update assembly guards
  - Done when: malformed/out-of-order data is filtered or normalized before strategy consumption

- **HV-008 — Live runtime state persistence + restore (`RuntimeStateStore`) (zipline)**
  - Depends on: HV-002
  - Deliverable: checkpoint store with schema version + checksum + corruption quarantine path
  - Done when: restart resumes from latest valid checkpoint without runtime crash

- **HV-009 — Startup/steady-state/shutdown lifecycle gates (zipline)**
  - Depends on: HV-002, HV-008
  - Deliverable: formal lifecycle orchestration with health/staleness gates and deterministic final flush
  - Done when: no order routes outside healthy steady-state

### 15.3 P2 — Translation + Data Platform

- **HV-010 — Contract/symbol normalization service (LEAN)**
  - Depends on: HV-004
  - Deliverable: centralized mapping for secType/exchange/derivative symbol forms with fallback handling
  - Done when: malformed/incomplete contract callbacks are recoverable or explicitly rejected with diagnostics

- **HV-011 — Bidirectional order translation layer (LEAN + zipline)**
  - Depends on: HV-004, HV-010
  - Deliverable: strategy-intent -> IB payload and broker-callback -> canonical event translation modules
  - Done when: supported order types produce stable normalized lifecycle events

- **HV-012 — Vendor-neutral historical ingestion contract (zipline)**
  - Depends on: none
  - Deliverable: extract -> normalize -> write interfaces with source-specific adapters
  - Done when: at least one source can be replaced without changing runtime readers

### 15.4 P3 — Risk/FA/Timing Refinements

- **HV-013 — FA routing validation strictness model (LEAN)**
  - Depends on: HV-011
  - Deliverable: validation for mutual-exclusion and account/group routing semantics
  - Done when: invalid FA route combinations are rejected pre-transmit

- **HV-014 — Pre-trade control DSL (`warn/reject/halt`) (zipline)**
  - Depends on: HV-011
  - Deliverable: configurable guards (max notional, max qty, max daily orders, session limits)
  - Done when: violations are consistently actioned according to policy mode

- **HV-015 — Pre-trade cost/risk estimator profiles (zipline)**
  - Depends on: HV-014
  - Deliverable: first slippage/commission estimate profile for order intent checks
  - Done when: estimated vs realized slippage/commission deltas are persisted to telemetry

- **HV-016 — Realtime clock + broker time-skew integration (zipline)**
  - Depends on: HV-006
  - Deliverable: calendar-aware event clock with skew correction and day-boundary handling
  - Done when: timing-driven tests pass for session open/close and reconnect boundaries

### 15.5 Tracker Fields (apply to each ticket)

Required fields per ticket:
- `ID`: HV-xxx
- `Priority`: P0/P1/P2/P3
- `Source`: LEAN / zipline / both
- `Area`: runtime, broker, market-data, order, risk, ingestion, FA, learning
- `Dependencies`: list of HV IDs
- `Acceptance`: explicit drill/test condition
- `Artifacts`: code path + exported report(s) in `exports/`

### 15.6 Suggested Sprint Cut

- **Sprint A (critical):** HV-001..HV-005
- **Sprint B (stability):** HV-006..HV-009
- **Sprint C (translation/data):** HV-010..HV-012
- **Sprint D (refinement):** HV-013..HV-016
