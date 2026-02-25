# L2 Multi-Timeframe Candlestick Strategy (Step-by-Step)

## Objective

Build strategy-ready candlesticks directly from L2 order-book updates and use aligned multi-timeframe signals (`30s`, `1m`, `5m`, `15m`, `1h`, `1D`) to drive entries/exits.

## Why this design

- L2 updates are event-driven and contain insert/update/remove semantics by book position.
- The safest deterministic approach is:
  1) reconstruct top-of-book from L2 updates,
  2) compute midpoint per event,
  3) aggregate midpoint into candles per timeframe,
  4) evaluate strategy only on completed candle boundaries.

This aligns with IBKR market-depth callback semantics (`updateMktDepth` / `updateMktDepthL2`) and avoids false signals from partial-book states.

## Data pipeline (implemented)

1. Capture depth stream with `--mode market-depth` or `--mode market-data-all`.
2. Persist raw L2 updates in `depth_data_*.json`.
3. Reconstruct top-of-book and derive midpoint timeline.
4. Aggregate midpoint into OHLC candles for:
   - `30s`
   - `1m`
   - `5m`
   - `15m`
   - `1h`
   - `1D`
5. Persist candles to `l2_candles_*.json`.

## Output schema (`l2_candles_*.json`)

Each row includes:

- `Timeframe`
- `BucketStartUtc`
- `BucketEndUtc`
- `Open`
- `High`
- `Low`
- `Close`
- `AverageMid`
- `AverageSpreadBps`
- `Samples`

## Strategy blueprint: MTF Trend + Pullback + Spread Guard

### Step 1 — Regime filter (`1D`, `1h`)

- Bull regime if `1D close > 1D EMA(20)` and `1h close > 1h EMA(20)`.
- Bear regime if both are below.
- No-trade otherwise.

### Step 2 — Setup (`15m`, `5m`)

Long setup:
- `15m` higher-high/higher-low structure, and
- `5m` pullback holds above `5m EMA(20)`.

Short setup:
- mirror logic.

### Step 3 — Trigger (`1m`, `30s`)

Long trigger:
- `1m` closes above previous `1m` high, and
- last `30s` candle closes green with expanding range.

Short trigger:
- mirror logic.

### Step 4 — Microstructure guard (L2 quality)

Before entry:
- require `AverageSpreadBps` (last completed `30s`) below threshold,
- require minimum `Samples` per `30s` bucket to avoid sparse books.

### Step 5 — Risk and exits

- Initial stop: below last `5m` swing low (long) / above swing high (short).
- Trail: move stop with completed `1m` structure.
- Hard flatten triggers:
  - regime invalidation (`1h` closes against position),
  - spread shock (`30s AverageSpreadBps` exceeds cap),
  - end-of-day flatten.

## Recommended implementation sequence

1. **Done**: L2 candlestick generation + exports.
2. Add an indicator layer over `l2_candles_*.json` (EMA, ATR, structure tags).
3. Add signal evaluator with strict completed-candle semantics.
4. Add replay harness using recorded `l2_candles_*.json`.
5. Add paper/live gate checks (spread cap, session window, max concurrent symbols).

## Run commands

- Market depth capture + L2 candles:
  - `dotnet run --project src/Harvester.App -- --mode market-depth --host 127.0.0.1 --port 7496 --client-id 9313 --account U22462030 --timeout 35 --export-dir exports --symbol SIRI --primary-exchange NSDQ --depth-exchange NSDQ --depth-rows 5 --market-data-type 3 --capture-seconds 120`

- Combined capture (L1 + L2 + bars + L2 candles):
  - `dotnet run --project src/Harvester.App -- --mode market-data-all --host 127.0.0.1 --port 7496 --client-id 9315 --account U22462030 --timeout 35 --export-dir exports --symbol SIRI --primary-exchange NSDQ --depth-exchange NSDQ --depth-rows 5 --market-data-type 3 --capture-seconds 120 --rtb-what TRADES`

## Notes

- If depth subscriptions are unavailable, `depth_data_*.json` and `l2_candles_*.json` may be empty.
- For strategy decisions, always use completed candles (not currently forming bucket values).
