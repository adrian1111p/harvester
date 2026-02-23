# Market Scanners — Step-by-Step Implementation

Reference: https://interactivebrokers.github.io/tws-api/market_scanners.html

This chapter is implemented with three dedicated modes matching the requested subsections.

## 1) Market Scanner Examples

### Analysis
- Uses `reqScannerSubscription(...)` with a standard `ScannerSubscription`.
- Captures scanner rows via `scannerData(...)` and end marker via `scannerDataEnd(...)`.

### Implementation
- Added mode: `scanner-examples`
- Request path:
  - build subscription from scanner CLI args
  - subscribe, wait for `scannerDataEnd` (or timeout warning), cancel
- Exports:
  - `scanner_examples_*.json`
  - `scanner_examples_request_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode scanner-examples --host 127.0.0.1 --port 7496 --client-id 9921 --account U22462030 --timeout 40 --export-dir exports --scanner-instrument STK --scanner-location STK.US.MAJOR --scanner-code TOP_PERC_GAIN --scanner-rows 10 --scanner-above-price 1 --scanner-above-volume 100000`

---

## 2) Complex Orders and Trades Scanner

### Analysis
- Uses scanner misc options and tag-value lists to pass advanced scanner filters/options.
- Runtime supports:
  - scanner setting pairs (`ScannerSubscription.ScannerSettingPairs`)
  - filter tag-values list
  - scanner options tag-values list

### Implementation
- Added mode: `scanner-complex`
- Request path:
  - parse `--scanner-filter-tags` and `--scanner-options-tags` in `tag=value;tag2=value2` format
  - call `reqScannerSubscription(reqId, sub, filterOptions, scannerOptions)`
  - wait for end marker (or timeout warning), cancel, export
- Exports:
  - `scanner_complex_*.json`
  - `scanner_complex_request_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode scanner-complex --host 127.0.0.1 --port 7496 --client-id 9922 --account U22462030 --timeout 40 --export-dir exports --scanner-instrument STK --scanner-location STK.US.MAJOR --scanner-code HOT_BY_VOLUME --scanner-rows 15 --scanner-setting-pairs "manual=1" --scanner-filter-tags "manual=1" --scanner-options-tags "lang=en"`

### Note
- In this workspace/API combination, `manual` is accepted while many arbitrary keys can return validation error `10337`.
- Runtime treats this as non-blocking and still exports diagnostics to support iterative filter development.

---

## 3) Scanner Parameters

### Analysis
- Scanner parameter catalog comes from `reqScannerParameters()` callback `scannerParameters(xml)`.
- The payload is XML and can be large.

### Implementation
- Added mode: `scanner-parameters`
- Exports:
  - `scanner_parameters_*.json` (row metadata + XML string)
  - `scanner_parameters_*.xml` (raw XML payload)

### Command
- `dotnet run --project src/Harvester.App -- --mode scanner-parameters --host 127.0.0.1 --port 7496 --client-id 9923 --account U22462030 --timeout 40 --export-dir exports`

---

## 4) Scanner Workbench (Ranking)

### Analysis
- This mode is designed for your “develop later” phase: it executes multiple scanner codes repeatedly and ranks them.
- Ranking dimensions are weighted to prefer robust scanner setups, not just one-off high row counts.

### Implementation
- Added mode: `scanner-workbench`
- Inputs:
  - `--scanner-workbench-codes` (comma-separated scan codes)
  - `--scanner-workbench-runs`
  - `--scanner-workbench-capture-seconds`
  - `--scanner-workbench-min-rows`
- Exports:
  - `scanner_workbench_runs_*.json` (per run metrics)
  - `scanner_workbench_ranking_*.json` (ranked scoreboard)

### Score model
- Coverage: 40%
- Speed (time to first row): 20%
- Stability (successful runs ratio): 30%
- Cleanliness (error-light runs): 10%

Hard-fail rules are applied before weighted sort:
- average rows below minimum threshold
- invalid configuration signals (`321`, `10337`)

### Command
- `dotnet run --project src/Harvester.App -- --mode scanner-workbench --host 127.0.0.1 --port 7496 --client-id 9924 --account U22462030 --timeout 80 --export-dir exports --scanner-instrument STK --scanner-location STK.US.MAJOR --scanner-rows 15 --scanner-workbench-codes "TOP_PERC_GAIN,HOT_BY_VOLUME,MOST_ACTIVE" --scanner-workbench-runs 2 --scanner-workbench-capture-seconds 6 --scanner-workbench-min-rows 1 --scanner-setting-pairs "manual=1" --scanner-filter-tags "manual=1" --scanner-options-tags "lang=en"`

---

## Added scanner CLI arguments

- `--scanner-instrument`
- `--scanner-location`
- `--scanner-code`
- `--scanner-rows`
- `--scanner-above-price`
- `--scanner-below-price`
- `--scanner-above-volume`
- `--scanner-mcap-above`
- `--scanner-mcap-below`
- `--scanner-stock-type`
- `--scanner-setting-pairs`
- `--scanner-filter-tags`
- `--scanner-options-tags`
- `--scanner-workbench-codes`
- `--scanner-workbench-runs`
- `--scanner-workbench-capture-seconds`
- `--scanner-workbench-min-rows`

## Validation status (current workspace)

- `scanner-examples` ✅ (exported, timeout-safe completion)
- `scanner-complex` ✅ (exported, timeout-safe completion)
- `scanner-parameters` ✅ (exported JSON + XML; XML payload >2M chars)
- `scanner-workbench` ✅ (ranked export pipeline implemented)

This chapter is now in place for deeper scanner strategy development later.
