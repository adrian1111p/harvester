# v1_001_Harvester_API — Detailed Development Roadmap

## 1) Project Scope and Constraints

**Application goal**  
Build a fully automated intraday (day trading) system using IBKR Trader Workstation (TWS) API.

**Given prerequisites**
- IBKR API location: `C:\TWS API`
- TWS location: `C:\Jts`
- IB account: `U22462030`
- Market data package: NASDAQ TotalView-OpenView (L2 depth) with required underlying US equity feeds
- API documentation baseline: https://interactivebrokers.github.io/tws-api/index.html

**Operating constraints**
- No paper account phase.
- Initial live deployment must use very small share size and low-priced symbols (target under $10).
- System must enforce hard risk limits in code (pre-trade and runtime) to reduce live risk.

---

## 2) Target System Outcomes (v1)

By end of v1, system should:
1. Connect reliably to TWS and auto-recover after disconnects.
2. Subscribe to and process market data (L1 + NASDAQ L2 where available).
3. Generate signals from a deterministic intraday strategy.
4. Route orders automatically with full audit trail.
5. Enforce multi-layer risk controls (symbol, position, daily loss, circuit breakers).
6. Produce end-of-day reconciliation and performance report.

---

## 3) Milestone Plan (Detailed)

## Milestone 0 — Project Foundation and Governance
**Objective:** Create a production-ready project skeleton and operational guardrails.

**Deliverables**
- Repository structure (`app`, `infra`, `ops`, `tests`, `docs`, `configs`).
- Environment profiles (`dev`, `uat`, `live`) with strict separation.
- Secure secrets approach (no credentials in code/logs).
- Standard logging format (JSON) with timestamps and correlation IDs.
- Decision records for architecture and trading assumptions.

**Acceptance criteria**
- Team can run a health-check command and validate system prerequisites.
- Config loading validates all required keys and blocks startup when missing.

**Estimated duration:** 2–3 days.

---

## Milestone 1 — TWS/IBKR Connectivity and Session Stability
**Objective:** Reliable API session management with reconnect logic.

**Deliverables**
- TWS connection manager (host/port/clientId profile support).
- Heartbeat and connection-state monitor.
- Auto-reconnect with backoff and session re-sync.
- Account/portfolio snapshot fetch and verification routines.

**Acceptance criteria**
- Survives manual TWS restart and resumes subscriptions.
- Correctly identifies account `U22462030` and available buying power.
- No orphan state after reconnect (subscriptions/orders resynced).

**Estimated duration:** 3–5 days.

---

## Milestone 2 — Market Data Ingestion (L1 + L2)
**Objective:** Build normalized market data pipeline for strategy and execution.

**Deliverables**
- L1 handlers (bid/ask/last/volume).
- L2 depth handlers for NASDAQ TotalView-OpenView book updates.
- Symbol universe loader (initially low-priced liquid symbols).
- Data quality checks: stale ticks, crossed books, missing depth tiers.
- In-memory book model with event timestamps and sequence tracking.

**Acceptance criteria**
- For selected symbols, order book updates are ingested continuously.
- Data latency and gap metrics captured and exported.
- Session open/close transitions handled without crashes.

**Estimated duration:** 5–7 days.

---

## Milestone 3 — Strategy Engine (Signal Layer)
**Objective:** Implement deterministic signal pipeline for intraday decisions.

**Deliverables**
- Strategy interface (input market events → output intents).
- First strategy module (simple, transparent logic).
- Feature calculators (spread, imbalance, momentum, short-window volatility).
- Time window controls (open ramp, no-trade windows, close cutoff).
- Cooldown and duplicate-signal suppression.

**Acceptance criteria**
- Signals are reproducible from recorded market data.
- Strategy decisions are explainable via structured logs.
- No strategy output outside permitted trading session.

**Estimated duration:** 7–10 days.

---

## Milestone 4 — Execution Engine and Order Lifecycle
**Objective:** Safely translate strategy intents into IBKR orders.

**Deliverables**
- Order router (marketable limit and passive limit templates).
- Order state machine (Created → Submitted → Partial → Filled/Cancelled/Rejected).
- Cancel/replace workflow.
- Slippage and fill-quality tracker.
- Duplicate-order prevention and idempotency keys.

**Acceptance criteria**
- Full order lifecycle tracked and persisted.
- Rejections handled with deterministic fallback/cancel behavior.
- Position state remains correct under partial fills and reconnects.

**Estimated duration:** 7–10 days.

---

## Milestone 5 — Risk Engine (Hard Controls, Mandatory)
**Objective:** Enforce hard limits before and during live execution.

**Deliverables**
- Pre-trade checks:
  - max shares per order
  - max position per symbol
  - max notional per symbol
  - spread and liquidity filters
  - prohibited symbol list
- Runtime kill-switches:
  - daily realized/unrealized drawdown stop
  - consecutive-loss pause
  - max open orders
  - max cancel/replace rate
- Circuit-breaker states (`RUNNING`, `SAFE_MODE`, `HALT`).

**Acceptance criteria**
- Any risk breach blocks new orders immediately.
- Kill-switch flattening procedure completes successfully.
- All breaches create immutable audit records.

**Estimated duration:** 5–8 days.

---

## Milestone 6 — Persistence, Audit, and Replay
**Objective:** Ensure every decision and event is auditable and replayable.

**Deliverables**
- Persistent storage for market snapshots, signals, orders, fills, PnL checkpoints.
- Event-sourcing style append logs for reproducibility.
- Trade blotter and compliance-style event timeline.
- Deterministic replay tool for a trading day.

**Acceptance criteria**
- Single trade can be traced end-to-end from signal to fill.
- Replay reproduces decisions for same inputs/config.
- Daily archives generated automatically.

**Estimated duration:** 4–6 days.

---

## Milestone 7 — Operations Dashboard and Alerting
**Objective:** Real-time operational visibility for live trading safety.

**Deliverables**
- Live dashboard: connection health, position, open orders, daily PnL, risk state.
- Alerting channels: disconnects, risk breaches, stale data, order rejects.
- Manual controls: global pause, symbol pause, flatten-all.

**Acceptance criteria**
- Critical alerts delivered within defined SLA.
- Manual controls verified in controlled drill.

**Estimated duration:** 3–5 days.

---

## Milestone 8 — Controlled Live Launch (Micro-Size Only)
**Objective:** Start live trading with strict capital and exposure limits.

**Launch rules (mandatory for first phase)**
- Universe: only pre-approved liquid symbols under $10.
- Position sizing: micro size (e.g., 1–10 shares per order, exact value configurable).
- Max concurrent positions: very low (e.g., 1–3).
- Daily loss cap: conservative hard stop.
- Session window: restricted intraday window only.

**Deliverables**
- Launch checklist and go/no-go signoff template.
- Runbook for open, midday checks, close, incident response.
- Daily post-trade review template.

**Acceptance criteria**
- 10 consecutive trading days with no control breaches.
- Order/fill integrity > 99% for expected workflows.
- Stable infra with no unresolved P1 incidents.

**Estimated duration:** 2 weeks minimum observation.

---

## Milestone 9 — Scale-Up Criteria and v1 Exit
**Objective:** Increase size only when objective quality gates are met.

**Scale-up gates**
- Strategy expectancy positive over minimum sample size.
- Max drawdown and intraday volatility within policy.
- Slippage within acceptable threshold.
- Zero unresolved high-severity risk incidents.

**Deliverables**
- Parameterized scaling policy (shares, symbols, time windows).
- Signed v1 exit review with metrics summary.

**Acceptance criteria**
- All scale-up gates met for two consecutive review cycles.

**Estimated duration:** rolling (weekly review cadence).

---

## 4) Work Breakdown by Streams

**A. Core API Integration**
- Connection/session manager
- Market data adapters
- Order API adapter

**B. Trading Logic**
- Signal engine
- Position manager
- Execution policy module

**C. Risk and Controls**
- Pre-trade limits
- Runtime limits
- Kill-switch framework

**D. Data and Analytics**
- Storage schema
- PnL/reconciliation
- Replay and diagnostics

**E. Ops/Infra**
- Service runner
- Monitoring/alerts
- Deployment and backup procedures

---

## 5) Proposed Timeline (10–14 Weeks)

- Weeks 1–2: Milestones 0–1
- Weeks 2–4: Milestone 2
- Weeks 4–6: Milestones 3–4
- Weeks 6–8: Milestones 5–6
- Weeks 8–9: Milestone 7
- Weeks 10–11+: Milestone 8 (micro-live observation)
- Weeks 12–14+: Milestone 9 (scale policy and v1 exit)

Timeline varies by team size and incident load during live ramp.

---

## 6) Go/No-Go Checklist Before First Live Order

- TWS auto-login/session policy documented and tested.
- All required market data subscriptions verified in-session.
- Risk caps configured and tested with forced-breach scenarios.
- Kill-switch tested end-to-end (including flatten-all).
- Logs, metrics, and alerts active and monitored.
- Runbook approved for normal and incident operations.
- Micro-size limits and symbol whitelist enforced by code.

---

## 7) KPI Dashboard (Daily)

- Connection uptime %
- Market data gap count and median latency
- Orders sent / rejected / cancelled / filled
- Fill ratio and average slippage
- Gross and net intraday PnL
- Max intraday drawdown
- Risk rule breach count by type

---

## 8) Immediate Next Actions (Week 1)

1. Confirm TWS build and API compatibility from `C:\Jts` and `C:\TWS API`.
2. Define initial micro-live risk policy values (shares/order, daily loss cap, max positions).
3. Create symbol whitelist (<$10, high liquidity, stable spread).
4. Implement Milestone 0 scaffolding and configuration validation.
5. Implement Milestone 1 connectivity with reconnect and account verification.

---

## 9) Notes

- The supplied documentation URL appears to include a trailing typo (`index.htmlf`). Use: https://interactivebrokers.github.io/tws-api/index.html
- Since paper trading is intentionally skipped, strict hard-coded risk controls are non-negotiable for initial rollout.
