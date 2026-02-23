# Account & Portfolio Data — Step-by-Step Implementation

This implementation follows the IBKR Account & Portfolio documentation and implements each supported subsection in sequence.

## 1) Managed Accounts

Reference: `managed_accounts.html`

### Analysis
- `managedAccounts` is sent on connect and can be explicitly requested via `reqManagedAccts()`.

### Implementation
- Added mode: `managed-accounts`
- Request: `reqManagedAccts()`
- Callback: `managedAccounts`
- Export: `managed_accounts_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode managed-accounts --host 127.0.0.1 --port 7496 --client-id 9401 --account U22462030 --timeout 30 --export-dir exports`

---

## 2) Family Codes

Reference: `family_codes.html`

### Analysis
- Request family/account relationships via `reqFamilyCodes()`.

### Implementation
- Added mode: `family-codes`
- Request: `reqFamilyCodes()`
- Callback: `familyCodes(FamilyCode[])`
- Export: `family_codes_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode family-codes --host 127.0.0.1 --port 7496 --client-id 9402 --account U22462030 --timeout 30 --export-dir exports`

---

## 3) Account Updates

Reference: `account_updates.html`

### Analysis
- Single-account stream via `reqAccountUpdates(true, account)` and cancel with `reqAccountUpdates(false, account)`.
- Model/account stream via `reqAccountUpdatesMulti(...)` and `cancelAccountUpdatesMulti(reqId)`.

### Implementation
- Added mode: `account-updates`
  - callbacks captured: `updateAccountValue`, `updatePortfolio`, `updateAccountTime`, `accountDownloadEnd`
  - exports:
    - `account_updates_values_*.json`
    - `account_updates_portfolio_*.json`
    - `account_updates_time_*.json`
- Added mode: `account-updates-multi`
  - callbacks captured: `accountUpdateMulti`, `accountUpdateMultiEnd`
  - export: `account_updates_multi_*.json`

### Commands
- `dotnet run --project src/Harvester.App -- --mode account-updates --host 127.0.0.1 --port 7496 --client-id 9403 --account U22462030 --update-account U22462030 --timeout 40 --export-dir exports --capture-seconds 6`
- `dotnet run --project src/Harvester.App -- --mode account-updates-multi --host 127.0.0.1 --port 7496 --client-id 9404 --account U22462030 --updates-multi-account U22462030 --timeout 40 --export-dir exports --capture-seconds 6`

---

## 4) Account Summary

Reference: `account_summary.html`

### Analysis
- `reqAccountSummary(reqId, group, tags)` provides subscribed summary values and `accountSummaryEnd` marker.

### Implementation
- Added dedicated mode: `account-summary`
- Supports tags/group arguments:
  - `--summary-group`
  - `--summary-tags`
- Export: `account_summary_subscription_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode account-summary --host 127.0.0.1 --port 7496 --client-id 9405 --account U22462030 --summary-group All --summary-tags "AccountType,NetLiquidation,TotalCashValue,BuyingPower" --timeout 40 --export-dir exports`

---

## 5) Positions

Reference: `positions.html`

### Analysis
- `reqPositions()` for all available accounts + `positionEnd`.
- `reqPositionsMulti()` for account/model targeting.

### Implementation
- Existing mode retained: `positions`
  - export: `positions_*.json`
- Added mode: `positions-multi`
  - callbacks: `positionMulti`, `positionMultiEnd`
  - export: `positions_multi_*.json`

### Commands
- `dotnet run --project src/Harvester.App -- --mode positions --host 127.0.0.1 --port 7496 --client-id 9103 --account U22462030 --timeout 30 --export-dir exports`
- `dotnet run --project src/Harvester.App -- --mode positions-multi --host 127.0.0.1 --port 7496 --client-id 9406 --account U22462030 --positions-multi-account U22462030 --timeout 40 --export-dir exports --capture-seconds 6`

---

## 6) Profit And Loss (P&L)

Reference: `pnl.html`

### Analysis
- Account-level PnL via `reqPnL` / callback `pnl`.
- Position-level PnL via `reqPnLSingle` / callback `pnlSingle`.

### Implementation
- Added mode: `pnl-account`
  - request/cancel: `reqPnL` / `cancelPnL`
  - export: `pnl_account_*.json`
- Added mode: `pnl-single`
  - request/cancel: `reqPnLSingle` / `cancelPnLSingle`
  - requires `--pnl-conid`
  - export: `pnl_single_*_*.json`
  - if no callback arrives during timeout window, mode completes with warning and exports current rows (possibly empty)

### Commands
- `dotnet run --project src/Harvester.App -- --mode pnl-account --host 127.0.0.1 --port 7496 --client-id 9407 --account U22462030 --pnl-account U22462030 --timeout 40 --export-dir exports --capture-seconds 6`
- `dotnet run --project src/Harvester.App -- --mode pnl-single --host 127.0.0.1 --port 7496 --client-id 9409 --account U22462030 --pnl-account U22462030 --pnl-conid 756733 --timeout 40 --export-dir exports --capture-seconds 6`

---

## 7) White Branding User Info

Reference: `wb_user_info.html`

### Compatibility status
- Not implementable in current pinned package `IBApi 1.0.0-preview-975`.
- Reflection check confirms missing APIs in this assembly:
  - no `EClientSocket.reqUserInfo(...)`
  - no `EWrapper.userInfo(...)`

To implement this subsection, the project needs an IB API version exposing those methods.

---

## Live validation summary (current workspace)

Validated with live runs:
- `managed-accounts` ✅
- `family-codes` ✅
- `account-updates` ✅
- `account-updates-multi` ✅
- `account-summary` ✅
- `positions` ✅
- `positions-multi` ✅
- `pnl-account` ✅
- `pnl-single` ✅ (graceful no-update handling validated)
