# Paper Trading Activation (Next Step After Internal Rehearsal)

This runbook prepares and activates the paper environment with safety gates.

- Validates connectivity on the paper endpoint.
- Captures L1/L2 smoke data.
- Runs `orders-whatif` preview before any transmit.
- Optionally places one guarded paper order.
- Optionally cancels an order by id.

## Baseline Command (No Placement)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\run_paper_trading_activation.ps1" -GatewayHost 127.0.0.1 -Port 7497 -ClientId 9980 -Account DU1234567 -Symbol SIRI -PrimaryExchange NSDQ -ExportDir exports -TimeoutSeconds 45 -MarketDataType 1 -CaptureSeconds 12 -DepthRows 5 -OrderAction BUY -OrderQuantity 1 -OrderLimit 5.00
```

## Place One Guarded Paper Order

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\run_paper_trading_activation.ps1" -GatewayHost 127.0.0.1 -Port 7497 -ClientId 9980 -Account DU1234567 -Symbol SIRI -PrimaryExchange NSDQ -ExportDir exports -RunOrderPlacement -OrderAction BUY -OrderQuantity 1 -OrderLimit 5.00 -MaxNotional 100 -MaxShares 10 -MaxPrice 10 -AllowedSymbols "SIRI,SOFI,F,PLTR,PLTK"
```

## Optional Cancel

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\run_paper_trading_activation.ps1" -GatewayHost 127.0.0.1 -Port 7497 -ClientId 9980 -Account DU1234567 -RunCancel -CancelOrderId 12345
```

## Safety Gates

- Blocks port `7496` by default (to avoid accidental live endpoint).
- Expects a paper account starting with `DU` by default.
- Requires explicit `-RunOrderPlacement` to transmit a paper order.
- Uses existing order risk guards (`max-notional`, `max-shares`, `max-price`, allow-list).

## Expected Outputs

- Connectivity and smoke data exports under `exports/`.
- `whatif_*.json`, `whatif_status_*.json`, `whatif_errors_*.json`.
- If placement enabled: `sim_order_request_*.json`, `sim_order_status_*.json`.
- If cancel enabled: `sim_order_cancel_*.json`, `sim_order_cancel_status_*.json`.