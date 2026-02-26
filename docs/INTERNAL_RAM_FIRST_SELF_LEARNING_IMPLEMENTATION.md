# Internal RAM-First Self-Learning Implementation (No DB)

This implementation matches your operating model:

- No database.
- Intraday data kept mostly in RAM.
- Compact trade episodes captured to temporary files.
- End-of-day self-learning update.
- Delete temporary data only after successful memory write.

## 1) Architecture You Run

- Intraday orchestration: [ops/run_internal_batch20_cycle.ps1](ops/run_internal_batch20_cycle.ps1)
- End-of-day learning + cleanup: [ops/run_eod_internal_self_learning_cleanup.ps1](ops/run_eod_internal_self_learning_cleanup.ps1)
- Existing internal rehearsal baseline: [ops/run_monday_internal_rehearsal.ps1](ops/run_monday_internal_rehearsal.ps1)

## 2) Step-by-Step Intraday Flow

1. Build candidate universe from scanner workbench.
2. Build `Batch-20` and `Focus-5` files from ranked/eligible symbols.
3. For each Batch symbol, pull 1m / 1D historical bars (`useRTH=0`) and convert to replay input JSON.
4. For each Focus symbol, run `market-data-all` capture (L1/L2 + bars) to generate microstructure artifacts.
5. For each Focus symbol, run `strategy-replay` using historical replay input and scanner candidates.
6. Persist output artifacts in `exports/` and temporary replay inputs under `temp/replay_inputs/<date>/`.

## 3) Intraday Command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\run_internal_batch20_cycle.ps1" -GatewayHost 127.0.0.1 -Port 7496 -ClientId 9960 -Account U22462030 -PrimaryExchange NSDQ -ExportDir exports -TimeoutSeconds 90 -BatchSize 20 -FocusSize 5 -ScannerMinScore 0 -MarketDataType 1 -FocusCaptureSeconds 45 -DepthRows 10 -HistDuration "1 D" -HistBarSize "1 min" -HistWhat TRADES -RequireMtfAlignment
```

## 4) Files Produced During Session

### Batch/Focus state

- `exports/internal_batch_20_*.json`
- `exports/internal_focus_5_*.json`

### Historical inputs (per batch symbol)

- `exports/historical_bars_<symbol>_*.json`
- `temp/replay_inputs/<date>/replay_input_<symbol>_*.json`

### Live L1/L2 focus captures

- `exports/top_data_*.json`
- `exports/depth_data_*.json`
- `exports/l2_candles_*.json`
- `exports/l2_strategy_signals_*.json`

### Replay + self-learning artifacts

- `exports/strategy_replay_historical_candles_*.json`
- `exports/strategy_replay_scanner_historical_evaluation_*.json`
- `exports/strategy_replay_self_learning_samples_*.json`
- `exports/strategy_replay_self_learning_predictions_*.json`
- `exports/strategy_replay_self_learning_summary_*.json`

## 5) Trade Episodes (RAM-first + temp files)

Phase 2 runtime integration status: implemented in-app.

- `ScannerCandidateReplayRuntime` now updates in-memory RAM session state per replay slice.
- 1-second microstructure buckets are built from top/depth updates (spread, imbalance, top sizes, tape aggression, volatility proxy, gate codes).
- Closed trades are recorded as compact episode JSON files by `ReplayTradeEpisodeRecorder`.

Recommended temporary layout:

- `temp/episodes/<yyyy-MM-dd>/<symbol>/<trade_id>.json`
- optional per-second series sidecar: `temp/episodes/<yyyy-MM-dd>/<symbol>/<trade_id>.series.jsonl`

Episode payload should include:

- pre-trade context features,
- during-trade aggregated series,
- fills/execution,
- labels (`pnl_usd`, `mae`, `mfe`, `exit_reason`),
- labels (`pnl_usd`, `r_multiple`, `mae`, `mfe`, `exit_reason`, `win_loss`),
- decision trace (`entry_reason`, `exit_reason`, risk metadata).

Optional export toggles (environment variables):

- `HARVESTER_EPISODE_EPOCH_MS=true` → serialize timestamps as Unix epoch milliseconds.
- `HARVESTER_EPISODE_SERIES_JSONL=true` → emit per-trade `.series.jsonl` (one line per second bucket).

## 6) End-of-Day Learning + Cleanup

1. Load episode files from `temp/episodes/<date>/`.
2. Optionally convert episode JSONL to Parquet (if `python + pyarrow` exists).
3. Create/update:
   - `memory/memory_latest.json`
   - `memory/versions/memory_<date>_<timestamp>.json`
4. If memory write succeeded, delete temp directories.

### EOD command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\run_eod_internal_self_learning_cleanup.ps1" -Date 2026-02-26 -ConvertEpisodesToParquet -DeleteTempAfterSuccess
```

## 7) Parquet Improvement Strategy

- Preferred: write episode aggregates to Parquet for compact training scans.
- Current implementation: optional conversion in EOD script using `python + pyarrow`.
- Fallback: stay JSON/JSONL if Parquet dependencies are missing.

This keeps operational reliability high while enabling a low-friction performance upgrade.

## 8) Safety Model

- Intraday script is internal data collection + replay only.
- No paper/live transmission in this workflow.
- Deletion happens only when memory update has been written successfully.

## 9) Next Transition

After validating this internal cycle, activate paper trading with:

- [ops/run_paper_trading_activation.ps1](ops/run_paper_trading_activation.ps1)
- [docs/PAPER_TRADING_ACTIVATION.md](docs/PAPER_TRADING_ACTIVATION.md)
