# V3 Live Trading Integration (L1 + L2, IBKR) — Step-by-Step

## 1) Objective
Implement `StrategyV3` for live/paper execution using **both L1 and L2** in the existing C# runtime.

Target outcome:
- Entry quality improves via microstructure gates (spread, depth, OFI, imbalance).
- Losses are reduced via strict pre-trade + in-trade liquidity/risk guards.
- Integration reuses existing runtime framework (`IStrategyRuntime`, `SnapshotRuntime`) and IBKR adapters.

---

## 2) Subscription Mapping (from your screenshot)
Your Account Management page shows these relevant entitlements:

- **L1 (required):**
  - NASDAQ Network C/UTP
  - NYSE Network A/CTA
  - NYSE American/BATS/ARCA/IEX + regional (Network B)
- **L2 (required for NASDAQ names):**
  - NASDAQ TotalView-OpenView
- **API:** Market Data API acknowledgment enabled.
- **Account status:** Non-Commercial Professional.

### Practical implication
- For **NASDAQ symbols**: use full L1 + L2 logic.
- For **non-NASDAQ symbols** where depth may be missing: run L1-only fallback or reject symbol based on a strict mode flag.

---

## 3) Existing Components to Reuse in This Repo

- Runtime + broker plumbing:
  - `src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs`
  - `src/Harvester.App/IBKR/Runtime/SnapshotEWrapper.cs`
- Strategy runtime contracts:
  - `src/Harvester.App/Strategy/StrategyRuntimeContracts.cs`
- Existing L1/L2 qualification logic:
  - `src/Harvester.App/Strategy/ScannerSelectionEngineV2.cs`
- V3 signal logic source:
  - `src/Harvester.App/Backtest/Strategies/StrategyV3.cs`
  - `src/Harvester.App/Backtest/Strategies/StrategyV3_1.cs`

---

## 4) Target Live Architecture

```text
IBKR TWS/Gateway
  ├─ reqMktData (L1 ticks)
  ├─ reqMktDepth (L2 book)
  └─ reqHistoricalData(keepUpToDate) / reqRealTimeBars (bars)

SnapshotRuntime (stream loop)
  └─ StrategyRuntimeContext + StrategyDataSlice
        └─ V3LiveRuntime (new IStrategyRuntime)
             ├─ FeatureBuilder (L1/L2 + bar features)
             ├─ V3SignalAdapter (V3/V3_1 logic adapted to live slices)
             ├─ RiskGuard (spread/depth/slippage/daily limits)
             └─ OrderRouter (orders-place-sim / guarded live)
```

---

## 5) Implementation Steps (File-by-File)

## Step 1 — Add a dedicated live runtime for V3
Create:
- `src/Harvester.App/Strategy/V3LiveRuntime.cs`

Implement `IStrategyRuntime`:
- `InitializeAsync`: load config, warm state.
- `OnDataAsync`: consume each `StrategyDataSlice` and call signal/risk/order pipeline.
- `OnScheduledEventAsync`: pre-open reset, close-only mode, EOD flatten.
- `OnShutdownAsync`: flush state/artifacts.

## Step 2 — Add V3 live config
Create:
- `src/Harvester.App/Strategy/V3LiveConfig.cs`

Config fields:
- **V3 logic:** `VwapStretchAtr`, `BbEntryPctbLow/High`, `Rsi bounds`.
- **L1 gates:** max spread %, min top quote size, stale quote timeout.
- **L2 gates:** min top-N depth, imbalance ratio min, thin-book reject.
- **Execution controls:** max slippage bps, cooldown seconds, max entries/day.
- **Risk:** risk per trade $, daily max loss $, max open risk $, hard stop/tp/trail.

## Step 3 — Build a live feature adapter
Create:
- `src/Harvester.App/Strategy/V3LiveFeatureBuilder.cs`

Responsibilities:
- Convert `TopTickRow[]` to current L1 snapshot (bid/ask/last/size).
- Convert `DepthRow[]` to L2 aggregate:
  - bidDepthN, askDepthN, imbalance = bidDepthN / max(askDepthN, eps)
  - spread ticks / spread %
  - simple OFI proxy from depth/inside changes.
- Merge with bar state (`HistoricalBarRow[]`) to compute V3-compatible indicators (VWAP distance, RSI, ATR proxy).

## Step 4 — Add live V3 signal engine
Create:
- `src/Harvester.App/Strategy/V3LiveSignalEngine.cs`

Rule set (recommended):
- Start from `StrategyV3_1` behavior.
- Entry requires:
  - V3 setup condition (VWAP reversion OR BB bounce OR squeeze release), AND
  - L1 spread gate pass, AND
  - L2 depth gate pass, AND
  - OFI/imbalance alignment with direction.
- Reject signal if any critical metric is NaN/stale.

## Step 5 — Add risk and kill-switch layer
Create:
- `src/Harvester.App/Strategy/V3LiveRiskGuard.cs`

Checks before placing order:
- Daily loss not exceeded.
- Symbol not in cooldown.
- Spread not widening abnormally in last N seconds.
- L2 not collapsing (depth drops below threshold).
- Position sizing uses risk-per-share + account caps.

## Step 6 — Add order intent + routing bridge
Create:
- `src/Harvester.App/Strategy/V3LiveOrderBridge.cs`

Behavior:
- Produce explicit order intents from live signals.
- Default to `orders-place-sim` guarded pathway first.
- Supports bracket structure (entry + stop + TP) with your existing safety controls.

## Step 7 — Add new runtime mode
Modify:
- `src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs`
- `src/Harvester.App/Program.cs`

Add new mode (example):
- `strategy-live-v3`

Wire it to:
- Build `V3LiveRuntime`
- Start data subscriptions for chosen symbols
- Feed slices into `OnDataAsync`

## Step 8 — Add CLI args for live V3
In `AppOptions` parsing (`SnapshotRuntime.cs`), add flags such as:
- `--v3live-symbols`
- `--v3live-l2-required true|false`
- `--v3live-depth-levels 5`
- `--v3live-max-spread-pct 0.015`
- `--v3live-min-depth 1500`
- `--v3live-max-daily-loss 300`
- `--v3live-enable-live true|false`

## Step 9 — Artifact and observability outputs
Export per run to `exports/`:
- `v3live_features_*.json`
- `v3live_signals_*.json`
- `v3live_rejections_*.json` (with reason codes)
- `v3live_orders_*.json`
- `v3live_risk_events_*.json`

Minimum telemetry columns:
- symbol, timestamp, spreadPct, bidDepthN, askDepthN, imbalance, ofi, setupType, passed/failed reason.

## Step 10 — Rollout protocol
1. **Data qualification only** (no orders): verify L1/L2 quality and feature stability.
2. **Signal-only mode**: emit intents, no transmission.
3. **Paper execution (`orders-place-sim`)**: with strict max notional/shares.
4. **Small-capital live pilot**: 1 symbol, reduced risk.
5. **Scale-out** after 2–4 weeks of stable metrics.

---

## 6) L1/L2 Integration Rules (Recommended Baseline)

### L1 hard gates
- `spreadPct <= 1.5%`
- bid/ask both present and updated within `<= 2s`
- `last` within [bid - tolerance, ask + tolerance]

### L2 hard gates
- top-5 total depth each side `>= 1500 shares`
- `imbalance` for long `>= 1.10`, for short `<= 0.90`
- reject thin book if best level size too small (e.g., < 100 shares)

### Entry confirmation
- V3 setup + L1 pass + L2 pass + directional OFI support.

### Exit protection
- If spread spikes above fail threshold OR depth collapses for X seconds:
  - tighten stop or flatten.

---

## 7) Validation Commands (Before Live)
Use existing runtime modes to verify feed/quality first:

- L1 check:
  - `dotnet run --project src/Harvester.App -- --mode top-data --host 127.0.0.1 --port 7496 --client-id 9310 --account <ACCOUNT> --symbol NVDA --primary-exchange NSDQ --market-data-type 1 --capture-seconds 20`
- L2 check:
  - `dotnet run --project src/Harvester.App -- --mode market-depth --host 127.0.0.1 --port 7496 --client-id 9313 --account <ACCOUNT> --symbol NVDA --primary-exchange NSDQ --depth-exchange NSDQ --depth-rows 5 --market-data-type 1 --capture-seconds 20`
- Combined check:
  - `dotnet run --project src/Harvester.App -- --mode market-data-all --host 127.0.0.1 --port 7496 --client-id 9315 --account <ACCOUNT> --symbol NVDA --primary-exchange NSDQ --depth-exchange NSDQ --depth-rows 5 --market-data-type 1 --capture-seconds 20 --rtb-what TRADES`

If you get empty rows in depth for a symbol, use L1-only fallback for that symbol or skip it.

---

## 8) Suggested Acceptance Criteria
- Data quality:
  - >= 99% slices have fresh L1.
  - >= 95% of NASDAQ symbols have valid L2 depth snapshots.
- Risk:
  - no order placed when gates fail.
  - no breach of daily loss and max open risk limits.
- Strategy quality (paper):
  - PF improvement vs baseline V10 active.
  - lower max drawdown during spread-stress periods.

---

## 9) Recommended First Live Scope
- Universe: `NVDA, META, AMD, AAPL` (NASDAQ-heavy for L2 quality).
- Session window: 09:35–11:30 ET and 14:00–15:30 ET.
- Mode: `orders-place-sim` first.
- Size: fixed small risk-per-trade, strict kill switch.

---

## 10) What to implement first (priority)
1. `V3LiveFeatureBuilder` + telemetry export.
2. `V3LiveSignalEngine` with strict L1/L2 gates.
3. `V3LiveRuntime` in signal-only mode.
4. order bridge in sim mode.
5. live guarded pilot.
