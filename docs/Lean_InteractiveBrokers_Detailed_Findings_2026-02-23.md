# QuantConnect LEAN Interactive Brokers Plugin — Detailed Findings (for Harvester)

Date: 2026-02-23  
Repository analyzed: `QuantConnect/Lean.Brokerages.InteractiveBrokers`

## Executive Summary

The LEAN IB plugin is a production-hardened adapter around the IB API with three major strengths:

1. **Connection resilience** (automated gateway lifecycle + retries + heartbeat + controlled reconnect behavior)
2. **High-fidelity translation layer** (orders, contracts, symbols, account/FA behavior, and market-data normalization)
3. **Operational safety controls** (error-code taxonomy, throttling, request correlation, subscription recovery, and broad live tests)

For Harvester, the highest-value inspirations are not strategy logic, but **broker-adapter reliability patterns** and **contract/symbol normalization safeguards**.

---

## 1) Architecture Overview

## Core composition

- `InteractiveBrokersBrokerage` is the main adapter and implements:
  - Brokerage behavior (connect/order/account/executions)
  - Data queue handling (`IDataQueueHandler`)
  - Universe lookup/provider (`IDataQueueUniverseProvider`)
- `InteractiveBrokersClient` wraps `EWrapper` callbacks into strongly-typed C# events.
- `InteractiveBrokersStateManager` tracks connection state flags (notably disconnect/reconnect lifecycle state).
- `InteractiveBrokersAccountData` centralizes account properties, cash balances, holdings in concurrent containers.
- `InteractiveBrokersSymbolMapper` performs Lean↔IB symbol translation, including malformed-contract recovery paths.

## Runtime model

- Event-driven callback bridge from IB API to internal handlers.
- Explicit request/response orchestration with `ManualResetEvent` for blocking workflows (open orders, contract details, executions, account download).
- Internal request tracking (`requestId -> request metadata`) for better diagnostics and provenance in errors.

---

## 2) Connection & Session Reliability Patterns

## Notable implementation behaviors

- **Gateway orchestration via IBAutomater**
  - Starts/restarts gateway process with credentials/mode.
  - Handles weekly restart windows and restart task scheduling.
- **Multi-stage connect handshake**
  - `eConnect` call
  - wait for connect callback
  - start reader/message-processing thread
  - wait for `NextValidId`
  - request account summary/managed accounts/family codes
  - perform account download before declaring healthy state
- **Retry strategy** with bounded attempts and backoff.
- **Heartbeat thread** using `reqCurrentTime` + timeout-based fail detection.
- **Reconnect semantics** tied to IB message codes:
  - 1100 (lost), 1101 (restored-data-lost), 1102 (restored-data-maintained)
  - reconnect event publication + data subscription restore when needed.

## Why this matters for Harvester

This is a proven template for moving from “works in happy path” to “stable in live trading under noisy infrastructure conditions”.

---

## 3) Error Handling & Safety Taxonomy

## Strong patterns observed

- Error code classification to information/warning/error paths.
- Message rewrite/normalization to keep logs one-line and readable.
- **Special-case handling** for known IB operational behavior (e.g., no-data responses, nightly resets).
- **Competing live session throttle** (error 10197) to avoid log/message floods.
- Invalidating codes unblock pending order waits and produce deterministic failure surfaces.
- Correlation context included: request origin/details appended to error outputs.

## Harvester inspiration

Adopt a first-class `IbErrorPolicy` module with:
- code groups
- action policy (ignore / warn / retry / hard-fail)
- throttling windows
- structured incident records

---

## 4) Order Translation Coverage

## Conversions Lean↔IB include

- Market, Limit, Stop, StopLimit, TrailingStop, LimitIfTouched
- MarketOnOpen / MarketOnClose
- Combo orders:
  - ComboMarket
  - ComboLimit
  - ComboLegLimit
- Time in force conversion (DAY/GTC/GTD/OPG etc.)
- Price normalization to brokerage tick rules and reverse normalization when reading back.
- Trailing stop details (amount vs percentage) with update reconciliation.

## Additional robustness

- Distinguishes place vs update flows.
- Handles disconnected-state order attempts explicitly.
- Recovers combo contract context from open orders when needed.

## Harvester implication

Your current chapter-driven order work can evolve into a formal **bidirectional order translation layer**, independent from runtime modes.

---

## 5) Financial Advisor (FA) & Multi-Account Handling

## Capabilities present

- FA-aware account download paths.
- Group filter support (`ib-financial-advisors-group-filter`).
- Managed accounts + family codes callbacks.
- FA-specific order properties and mutual exclusivity validation.
- Fill emission logic that validates account routing semantics.

## Harvester implication

You already implemented FA chapter features; this repo validates that FA behavior needs dedicated routing/validation logic, not generic account code.

---

## 6) Symbol/Contract Mapping & Malformed Data Recovery

## Advanced mapping capabilities

- Security type conversion in both directions.
- Exchange/market mapping and root symbol mapping tables.
- Handling of equity dot/space transformations and special ticker cases.
- Future/FOP/index-option specific mapping nuances.
- **Malformed contract recovery**:
  - options with missing/zero fields reconstructed from encoded symbol strings
  - futures with malformed expiry inferred via expiry functions/symbol metadata

## Why this is high value

In live IB environments, malformed/partial contract payloads are realistic under load. This is one of the best practical hardening areas for Harvester.

---

## 7) Market Data Pipeline Patterns

## Data queue behavior

- Subscription manager abstraction with explicit subscribe/unsubscribe hooks.
- Subscription gating during restart windows to avoid invalid request races.
- Symbol↔request id tracking and subscription-time bookkeeping.
- Tick assembly logic:
  - `tickPrice` stores state
  - `tickSize` finalizes emissions (quote/trade/open-interest)
- Quote value synthesis using bid/ask midpoint fallback logic.
- Data normalization:
  - negative price/size normalization
  - delayed tick fields handling
  - price magnifier application
  - special-case skip logic for known feed anomalies
- Reroute market data callbacks handled (CFD/underlying related flows).

## Harvester implication

This pattern maps directly to improving your snapshot/live feed quality, especially for robust quote/trade event assembly.

---

## 8) Historical Data & Request Utilities

## Useful implementations

- Resolution→bar size mapping.
- Duration generation and parsing helpers (`GetDuration`, `ParseDuration`).
- History bar conversion with daily/intraday timestamp handling and magnifier normalization.
- Contract discovery utilities with timeout + event cleanup guarantees.

## Harvester implication

Directly useful for your historical replay/self-learning pipeline: deterministic request sizing and consistent bar normalization.

---

## 9) Testing Strategy Observed

## Test breadth

- Core brokerage behavior tests (connect/order/account/open orders/executions).
- Data queue handler tests across asset classes and markets.
- Symbol mapper unit tests including malformed contract edge-cases.
- Additional live/explicit tests for combo/trailing/history/FA and resilience scenarios.
- Factory/config bootstrapping tests.

## Key practical point

Many tests are marked explicit/ignored because they require IBGateway and live credentials, but they still encode valuable real-world edge cases.

---

## 10) Configuration Surface

Common keys used by factory/runtime include:

- `ib-account`
- `ib-host`
- `ib-port`
- `ib-tws-dir`
- `ib-version`
- `ib-user-name`
- `ib-password`
- `ib-trading-mode`
- `ib-agent-description`
- `ib-weekly-restart-utc-time`
- `ib-financial-advisors-group-filter`
- `load-existing-holdings`

This is a good reference for designing Harvester’s own environment/config contract.

---

## 11) Adopt / Adapt / Not-Now Matrix for Harvester

## Adopt now (highest ROI)

1. **Error-code policy table + throttling + request-origin tagging**
2. **Connection state machine** (connect stages, nextValidId gate, reconnect semantics)
3. **Heartbeat with controlled failover**
4. **Subscription restore and restart-window gating**
5. **Bid/ask/trade tick assembly rules + normalization guards**
6. **Timeout-and-cleanup request wrappers** for blocking calls

## Adapt later (high value, more effort)

1. Full bidirectional order translator abstraction (including complex combos)
2. Full malformed contract recovery paths (options/futures/FOP)
3. Extended FA routing logic parity (group/profile/account path strictness)
4. Weekly restart scheduling automation and process supervision around gateway

## Not now (or optional for your scope)

1. LEAN-specific integration layers (`IAlgorithm`, `LiveNodePacket`, Composer wiring)
2. Backtest brokerage model internals unrelated to Harvester runtime
3. Very broad multi-asset combo order surface if your near-term trading scope is narrower

---

## 12) Concrete Harvester Upgrade Blueprint

## Phase A — Reliability baseline

- Add `IbConnectionState` module with explicit states and transition logs.
- Add `IbErrorPolicy` with error code grouping + per-code action.
- Add `RequestRegistry` (`requestId`, type, symbol, start time, timeout, origin).

## Phase B — Data quality hardening

- Implement paired quote/trade tick assembly and emission rules.
- Add normalization utilities (negative values, magnifier, delayed fields).
- Add subscription replay on reconnect, guarded during restart windows.

## Phase C — Contract/order normalization

- Centralize symbol/contract conversion in dedicated service.
- Add malformed contract fallback parser paths for options/futures as needed.
- Refactor runtime modes to consume normalized adapter services instead of direct callback assumptions.

## Phase D — Learning loop integration

- Persist adapter incidents (error code, request context, recoverability outcome).
- Feed these incidents into self-learning memory to adjust retry/ignore/escalation policy over time.

---

## Final Assessment

This repository is a strong blueprint for **broker adapter robustness at production scale**.  
For Harvester’s day-trading + self-learning goals, the most valuable inspiration is:

- deterministic connection lifecycle
- defensive data/order normalization
- structured error policy with provenance and throttling

Implementing these patterns will materially reduce live fragility and improve the quality of your self-learning feedback loop.