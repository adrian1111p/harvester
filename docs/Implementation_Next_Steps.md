# Implementation Next Steps

This checklist captures what is still needed after the current connection, contracts, orders, and market-data groundwork.

## 1) Core trading safety (must-have before autonomous trading)

- Central risk policy config file (hard limits) loaded at startup
- Unified pre-trade risk validator (symbol allow-list, max notional, max qty, max order rate)
- Runtime kill switch states (`RUNNING`, `SAFE_MODE`, `HALT`) and flatten-all command
- Daily loss cap enforcement with order blocking

## 2) Order lifecycle and reliability

- Persistent order state store (submitted/partial/filled/cancelled/rejected)
- Reconnect resync routine for open orders + positions + executions
- Idempotent client order references to avoid duplicate placements
- Cancel/replace manager with throttling

## 3) Market-data quality and normalization

- Symbol metadata cache from `reqContractDetails` for trading universe
- Data quality guards (stale ticks, crossed book, missing depth updates)
- Event timestamp normalization and sequencing
- Runtime metrics for data gaps and latency

## 4) Strategy layer scaffolding

- Strategy interface abstraction (`OnTopTick`, `OnDepthUpdate`, `OnRealtimeBar`)
- Deterministic signal record export for replay
- Session/time-window rules (open ramp, no-trade windows, close cutoff)

## 5) Ops and observability

- Structured logs (JSON) with correlation/order refs
- Daily run summary command (PnL, fills, rejects, risk events)
- Task Scheduler integration for recurring snapshot and diagnostics jobs

## 6) Post-subscription activation checks (after 2026-03-01)

- Re-run `market-depth` and verify non-zero depth updates
- Re-run `realtime-bars` with valid entitlement stream
- Validate depth on at least 3 liquid NASDAQ symbols
