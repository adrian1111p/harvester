# zipline-trader — Detailed Findings (for Harvester)

Date: 2026-02-23  
Repository analyzed: `shlomiku/zipline-trader`

## Executive Summary

`zipline-trader` is a practical **live-trading extension** of Zipline with strong value in three areas:

1. **Runtime abstraction boundaries** (Broker interface + live blotter + live algorithm path)
2. **Stateful live execution model** (realtime clock, persistence, broker-backed account/portfolio refresh)
3. **Pluggable data ingestion architecture** (bundle registration and normalized writers)

For Harvester, this repository is less about IB API parity and more about **live orchestration patterns**, **broker contract boundaries**, and **operational state handling**.

---

## 1) Architecture Overview

## Core separation

- `zipline.gens.brokers.broker.Broker` defines a strict broker contract (`order`, `cancel_order`, `orders`, `transactions`, `get_spot_value`, `get_realtime_bars`, account/portfolio/positions/time_skew).
- Concrete adapters implement that contract:
  - `zipline.gens.brokers.alpaca_broker.ALPACABroker`
  - `zipline.gens.brokers.ib_broker.IBBroker`
- `TradingAlgorithm` is extended by `LiveTradingAlgorithm` to switch from simulation-first behavior to broker-backed behavior.
- `BlotterLive` bridges broker order/transaction streams into Zipline metrics and lifecycle.

## Harvester relevance

This boundary design is directly applicable to Harvester’s connector/service layering: keep strategy/runtime layer stable while swapping broker adapters and market-data implementations.

---

## 2) Live Execution Runtime Patterns

## What stands out

- `LiveTradingAlgorithm` injects a live blotter and broker at construction time.
- Uses a dedicated `RealtimeClock` replacing simulation clock behavior.
- Supports lifecycle callbacks beyond backtest usage (`initialize`, `handle_data`, `teardown`, `run`, `on_exit`).
- Includes explicit persistence hooks for algorithm context (`state_filename`, include/exclude lists, checksum enforcement).
- Syncs account/portfolio from broker-facing state for live correctness.

## Harvester implication

Treat live runtime as a first-class mode with:

- deterministic startup pipeline,
- persisted state checkpoints,
- clean shutdown semantics,
- and periodic broker-to-local state reconciliation.

---

## 3) Broker Adapter Design (IB + Alpaca)

## Shared contract quality

- Both brokers implement a common API shape.
- Order style conversion is explicit (market/limit/stop/stop-limit) and adapter-specific.
- External order IDs are mapped into internal order models.
- Transaction extraction is separate from order-state extraction.

## IB-specific implementation insights

- Callback-rich TWS wrapper (`TWSConnection`) captures order status, executions, commissions, account values, and market data updates.
- Order metadata is serialized into `orderRef` and parsed back to reconstruct missing context.
- Orders are reconciled from multiple sources (open orders, statuses, executions, commissions) instead of trusting one callback path.
- Includes symbol safety handling when an instrument is missing from ingested universe.

## Alpaca-specific implementation insights

- Uses REST-first flows with UUID-based client order IDs.
- Converts broker orders to internal order representation with status mapping (`OPEN`, `CANCELLED`, `REJECTED`, `FILLED`).
- Position refresh path updates portfolio tracker with broker truth.

## Harvester implication

Adopt a **single internal broker contract**, then enforce strict per-broker adapter modules for:

- order translation,
- status mapping,
- execution/commission reconciliation,
- and symbol-resolution fallback behavior.

---

## 4) Data Ingestion & Bundle Architecture

## Patterns observed

- Data bundles are registered with metadata (`calendar_name`, session bounds, `minutes_per_day`, writer creation mode).
- Ingestion functions receive canonical writers (`asset_db_writer`, `daily_bar_writer`, `minute_bar_writer`, `adjustment_writer`) and calendar/session context.
- Multi-source ingestion present (`quandl`, `alpaca_api`, `alpha_vantage`, `csvdir`).
- Bundle lifecycle has explicit `register`, `ingest`, `load`, and `clean` paths.
- `run_algorithm` selects `DataPortalLive` when broker mode is active.

## Harvester implication

This is a strong template for a **normalized historical data ingestion pipeline** that decouples:

- vendor extraction,
- canonical schema writes,
- and runtime consumers.

---

## 5) Trading Controls, Slippage, and Commission Modeling

## Useful capabilities

- Rich control APIs: max leverage, min leverage, max order size/count, max position size, long-only constraints.
- Multiple slippage models:
  - fixed spread,
  - volume-share impact,
  - fixed basis points with volume caps,
  - futures volatility-volume model.
- Commission models include per-share/per-trade and futures contract costs.

## Harvester implication

Even if Harvester executes at broker prices, these models are valuable for:

- pre-trade simulation checks,
- expected-cost previews,
- and risk guardrails before live routing.

---

## 6) Metrics and Ledger Model

## Notable structure

- `MetricsTracker` orchestrates metric hooks (`start/end session`, `end_of_bar`, `end_of_simulation`).
- `Ledger` and portfolio/account structures are treated as central truth objects for performance packets.
- Live mode still flows through metrics hooks, preserving observability consistency.

## Harvester implication

Use a unified metrics pipeline across paper/live modes, with source-aware deltas (broker-confirmed vs inferred).

---

## 7) Reliability & Operational Caveats Observed

## Positive patterns

- Clock abstraction for live timing.
- State persistence and recovery tests.
- Runtime broker-alive checks and time skew fields.

## Cautions from repository state

- Several live test suites are marked skipped/failing in CI (`test_alpaca_broker`, `test_ib_broker`, `test_blotter_live`, realtime-clock crosschecks).
- Some docs and scripts indicate version/platform coupling (legacy Python constraints, C-extension complexity).
- Certain ingestion workflows are documented as “use script path, not default old ingest command”, which signals historical compatibility friction.

## Harvester implication

Use this repo for architecture and patterns, but treat direct behavior as **reference, not drop-in truth**.

---

## 8) Adopt / Adapt / Not-Now Matrix for Harvester

## Adopt now (high ROI)

1. **Strict broker adapter interface** (single contract for all broker implementations)
2. **Live runtime state persistence** (startup restore + guarded serialization + checksum)
3. **Order/execution/commission reconciliation loop** from multi-source broker events
4. **Live blotter separation** from simulation blotter semantics
5. **Pluggable ingest pipeline contract** (extract → normalize → write canonical)
6. **Runtime metrics pipeline parity** across paper/live modes

## Adapt later (important, higher effort)

1. Realtime clock semantics with exchange calendar + broker-time skew integration
2. Full trading-control DSL and validation hooks before order routing
3. Multi-model slippage/commission pre-trade estimation service
4. Broker-specific order metadata encoding for recovery/reconstruction

## Not-now (or optional)

1. Zipline-specific API surface parity (algorithm scripting compatibility layer)
2. Legacy Python/package constraints and C-extension specific build paths
3. Full backtest framework parity when Harvester focus is broker runtime first

---

## 9) Concrete Implementation Tasks for Harvester (v1-oriented)

## Wave A — Runtime/Broker Hardening

- Define `IBrokerAdapter` contract in Harvester domain layer.
- Enforce separate modules for `order mapping`, `status mapping`, `execution mapping`, `commission mapping`.
- Add adapter reconciliation worker that joins open orders, statuses, fills, and commissions into one canonical order ledger.

## Wave B — Live Lifecycle Safety

- Implement persisted runtime state checkpoints for long-running mode.
- Add startup restore policy with validation checksum/version stamp.
- Add broker health and clock-skew monitor to session loop.

## Wave C — Data and Risk Modeling

- Define vendor-neutral historical bar ingest interface.
- Add a first slippage/commission estimation profile for pre-trade checks.
- Add guardrail controls (max notional/qty/day-order-count/leverage-like constraints where applicable).

---

## 10) Standing Reference Note

When implementing Harvester broker/runtime internals, consult this repository for:

- adapter boundary design,
- live blotter + runtime orchestration,
- data bundle ingestion contracts,
- and persistence patterns.

For IB API-specific semantics and reconnect/error-code behavior, continue to cross-reference:

- `QuantConnect/Lean.Brokerages.InteractiveBrokers` (primary IB hardening reference)
- `shlomiku/zipline-trader` (runtime/orchestration and broker contract reference)
