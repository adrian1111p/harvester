# Monday Internal Rehearsal (L1/L2 + Scanner + Strategy + Self-Learning)

This run is an internal simulation pipeline for Monday readiness.

- Captures full L1/L2 market data (`market-data-all`) for internal evidence artifacts.
- Uses scanner candidate discovery (`scanner-workbench`).
- Uses historical bars as replay feed input.
- Runs strategy execution in `strategy-replay` mode (simulated, no order transmission).
- Exports candlestick, scanner historical evaluation, and self-learning artifacts.

## Start Command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\run_monday_internal_rehearsal.ps1" -GatewayHost 127.0.0.1 -Port 7496 -ClientId 9970 -Account U22462030 -Symbol SIRI -PrimaryExchange NSDQ -ExportDir exports -TimeoutSeconds 90 -MarketDataType 1 -L1L2CaptureSeconds 120 -DepthRows 10 -ScannerTopN 5 -ScannerMinScore 60 -OrderQuantity 1 -OrderSide BUY -OrderType LMT -OrderTif DAY -LimitOffsetBps 10
```

## Useful Overrides

```powershell
.\ops\run_monday_internal_rehearsal.ps1 -Symbol AMD -PrimaryExchange NSDQ -ScannerTopN 8 -ScannerMinScore 55 -MarketDataType 1 -L1L2CaptureSeconds 180
```

Optional order-seed file in replay mode:

```powershell
.\ops\run_monday_internal_rehearsal.ps1 -ReplayOrdersInputPath .\exports\replay_orders_seed_long_for_auto_test.json
```

## Outputs to Check

The script prints full paths after completion. Key artifacts in `exports/`:

- `top_data_*.json`
- `depth_data_*.json`
- `l2_candles_*.json`
- `l2_strategy_signals_*.json`
- `strategy_replay_historical_candles_*.json`
- `strategy_replay_scanner_historical_evaluation_*.json`
- `strategy_replay_scanner_symbol_selection_*.json`
- `strategy_replay_self_learning_samples_*.json`
- `strategy_replay_self_learning_predictions_*.json`
- `strategy_replay_self_learning_summary_*.json`

## Safety

This flow does **not** place live or paper orders. It runs scanner and strategy logic in replay/simulation mode for internal validation and model-learning exports.