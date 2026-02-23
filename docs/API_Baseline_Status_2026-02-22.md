# API Baseline Status — 2026-02-22

## Environment

- TWS process: running
- Account detected by API: `U22462030`
- API host/port tested: `127.0.0.1:7496`
- Diagnostic script: `ops/ibkr_api_baseline_check.py`

## Step-by-step results

1. **Connection handshake** — ✅ PASS
   - `nextValidId` received
   - `managedAccounts=U22462030`
   - `currentTime` response received

2. **Portfolio/account checks** — ✅ PASS
   - `reqAccountSummary` returned values (AccountType, BuyingPower, NetLiquidation, TotalCashValue)
   - `reqPositions` completed (`positionEnd`)

3. **Orders checks** — ✅ PASS (read access)
   - `reqOpenOrders` completed (`openOrderEnd`)
   - No write action was performed

4. **L1 market data** — ✅ PASS (delayed)
   - Received delayed ticks for `SIRI`
   - Script uses `reqMarketDataType(3)` for delayed mode fallback

5. **NASDAQ TotalView-OpenView (L2 depth)** — ❌ BLOCKED
   - `reqMktDepth` returns error `10089` (additional subscription/entitlement required for API)

## Immediate action required to unlock L2

In TWS, verify API + market data permissions:

- `Global Configuration > API > Settings`
  - Enable ActiveX and Socket Clients
  - Include market data in API
  - Allow localhost only (recommended)

Then verify account entitlement for API deep book (NASDAQ TotalView/OpenView) is active for this live account and not only for GUI display.

## Re-test command

```powershell
D:/Site/harvester/.venv/Scripts/python.exe .\ops\ibkr_api_baseline_check.py --port 7496 --client-id 9005 --symbol SIRI --depth-rows 5
```
