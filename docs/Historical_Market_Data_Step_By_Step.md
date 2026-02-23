# Historical Market Data — Step-by-Step Implementation

This implementation follows the IBKR historical documentation in the same subsection order you requested.

## 0) Historical Market Data (overview)

Reference: `historical_data.html`

Implemented in runtime:
- New modes in `SnapshotRuntime`:
  - `historical-bars`
  - `historical-bars-live`
  - `histogram`
  - `historical-ticks`
  - `head-timestamp`
- New callbacks and row models in `SnapshotEWrapper`:
  - `historicalData`, `historicalDataEnd`, `historicalDataUpdate`
  - `histogramData`
  - `historicalTicks`, `historicalTicksBidAsk`, `historicalTicksLast`
  - `headTimestamp`

---

## 1) Historical Data Limitations

Reference: `historical_limitations.html`

### Analysis
Key constraints encoded into runtime behavior:
- Pacing awareness for small bars (`<= 30 secs`) with warning output.
- Duration/bar-size “step size” precheck with warning output.
- Design keeps only one historical request active per mode run.

### Implementation
- Added `ValidateHistoricalBarRequestLimitations(duration, barSize)` in `SnapshotRuntime`.
- Added parsers:
  - `TryParseDurationToSeconds`
  - `BarSizeToSeconds`

### Command
- `dotnet run --project src/Harvester.App -- --mode historical-bars --host 127.0.0.1 --port 7496 --client-id 9321 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --hist-duration "1 D" --hist-barsize "5 mins" --hist-what TRADES --hist-use-rth 1 --hist-format-date 1`

---

## 2) Historical Bar Data

Reference: `historical_bars.html`

### Analysis
- Request path: `reqHistoricalData`.
- End marker path: `historicalDataEnd`.
- Streaming-update path for unfinished bar: `historicalDataUpdate` when `keepUpToDate=true`.

### Implementation
- Added mode `historical-bars`:
  - one-shot bars request (`keepUpToDate=false`)
  - waits for `historicalDataEnd`
  - exports `historical_bars_*.json`
- Added mode `historical-bars-live`:
  - enforces empty `--hist-end`
  - enforces bar size >= `5 secs`
  - runs with `keepUpToDate=true`, captures updates for `--capture-seconds`, then cancels
  - exports:
    - `historical_bars_keepup_*.json`
    - `historical_bars_updates_*.json`

### Commands
- One-shot:
  - `dotnet run --project src/Harvester.App -- --mode historical-bars --host 127.0.0.1 --port 7496 --client-id 9321 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --hist-duration "1 D" --hist-barsize "5 mins" --hist-what TRADES --hist-use-rth 1 --hist-format-date 1`
- Keep-up-to-date:
  - `dotnet run --project src/Harvester.App -- --mode historical-bars-live --host 127.0.0.1 --port 7496 --client-id 9326 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --hist-duration "1 D" --hist-barsize "5 secs" --hist-what TRADES --hist-use-rth 1 --hist-format-date 1 --hist-end "" --capture-seconds 15`

### SCHEDULE subsection (from historical bars page)
- Current package compatibility note:
  - Installed `IBApi 1.0.0-preview-975` does not expose `historicalSchedule(...)` callback in `EWrapper`.
  - `SCHEDULE` subsection is therefore documented but not executable in this pinned assembly version.
  - Enabling this requires upgrading to an IB API build that includes `historicalSchedule` callback support.

---

## 3) Histograms

Reference: `histograms.html`

### Analysis
- Request path: `reqHistogramData`.
- Response path: `histogramData`.
- Optional cancel path: `cancelHistogramData`.

### Implementation
- Added mode `histogram`:
  - requests histogram by period (`--histogram-period`)
  - waits for first histogram callback completion
  - exports `histogram_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode histogram --host 127.0.0.1 --port 7496 --client-id 9323 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --hist-use-rth 1 --histogram-period "1 week"`

---

## 4) High Resolution Historical Data (Historical Time & Sales)

Reference: `historical_time_and_sales.html`

### Analysis
- Request path: `reqHistoricalTicks`.
- Exactly one of start or end must be provided.
- Max request count per call is bounded by API (`numberOfTicks`, typically <= 1000).
- Response paths vary by `whatToShow`:
  - `MIDPOINT` -> `historicalTicks`
  - `BID_ASK` -> `historicalTicksBidAsk`
  - `TRADES` -> `historicalTicksLast`

### Implementation
- Added mode `historical-ticks`:
  - validates exactly one of `--hist-tick-start` or `--hist-tick-end`
  - supports `--hist-ticks-what TRADES|BID_ASK|MIDPOINT`
  - supports `--hist-ticks-num`, `--hist-ignore-size`, `--hist-use-rth`
  - waits for done callback and exports type-specific rows

### Commands
- TRADES example:
  - `dotnet run --project src/Harvester.App -- --mode historical-ticks --host 127.0.0.1 --port 7496 --client-id 9324 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --hist-ticks-what TRADES --hist-ticks-num 200 --hist-tick-end "20260223-11:30:00" --hist-use-rth 1 --hist-ignore-size true`
- BID_ASK example:
  - `dotnet run --project src/Harvester.App -- --mode historical-ticks --host 127.0.0.1 --port 7496 --client-id 9327 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --hist-ticks-what BID_ASK --hist-ticks-num 200 --hist-tick-end "20260223-11:30:00" --hist-use-rth 1 --hist-ignore-size true`

---

## 5) Finding Earliest Data Point

Reference: `head_timestamp.html`

### Analysis
- Request path: `reqHeadTimestamp`.
- Response path: `headTimestamp`.
- Cancel path: `cancelHeadTimestamp`.

### Implementation
- Added mode `head-timestamp`:
  - requests earliest timestamp for symbol and data type (`--head-what`)
  - waits for callback and then cancels request
  - exports `head_timestamp_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode head-timestamp --host 127.0.0.1 --port 7496 --client-id 9322 --account U22462030 --timeout 40 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --head-what TRADES --hist-use-rth 1 --hist-format-date 1`

---

## Validation status in this workspace

Validated live today:
- `historical-bars` ✅
- `historical-bars-live` ✅
- `histogram` ✅
- `historical-ticks` (TRADES) ✅
- `head-timestamp` ✅

The remaining historical modes are implemented and ready to run with the commands above.
