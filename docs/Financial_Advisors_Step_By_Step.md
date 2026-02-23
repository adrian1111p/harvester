# Financial Advisors — Step-by-Step Implementation

Reference: https://interactivebrokers.github.io/tws-api/financial_advisor.html

This implementation covers all requested FA subsections with dedicated runtime modes and safe order controls.

## 1) Allocation Methods and Groups

### Analysis
- Allocation methods are defined at group/profile level and applied through FA order fields.
- Runtime captures current group XML (`requestFA(Constants.FaGroups)`) and display-group list (`queryDisplayGroups`).

### Implementation
- Added mode: `fa-allocation-groups`
- Requests:
  - `requestFA(Constants.FaGroups)`
  - `queryDisplayGroups(...)`
- Exports:
  - `fa_allocation_methods_*.json` (method reference table)
  - `fa_groups_*.json` (raw FA XML payloads for groups)
  - `fa_display_groups_*.json` (display groups list callback rows)

### Command
- `dotnet run --project src/Harvester.App -- --mode fa-allocation-groups --host 127.0.0.1 --port 7496 --client-id 9701 --account U22462030 --timeout 40 --export-dir exports`

---

## 2) Groups and Profiles from the API

### Analysis
- API retrieval uses `requestFA` with type constants:
  - `Constants.FaAliases = 3`
  - `Constants.FaGroups = 1`
  - `Constants.FaProfiles = 2`
- Data arrives via `receiveFA(int faDataType, string faXmlData)`.

### Implementation
- Added mode: `fa-groups-profiles`
- Requests:
  - aliases, groups, profiles via `requestFA`
- Exports:
  - `fa_data_all_*.json`
  - `fa_aliases_*.json`
  - `fa_groups_api_*.json`
  - `fa_profiles_api_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode fa-groups-profiles --host 127.0.0.1 --port 7496 --client-id 9702 --account U22462030 --timeout 40 --export-dir exports`

---

## 3) Unification of Groups and Profiles

### Analysis
- In merged mode (TWS/IBGW 983+ setting), profile behavior may be unified under group semantics.
- Runtime probes by requesting both groups and profiles, then summarizes observed payload/error pattern.

### Implementation
- Added mode: `fa-unification`
- Requests:
  - `requestFA(Constants.FaGroups)`
  - `requestFA(Constants.FaProfiles)`
- Exports:
  - `fa_unification_summary_*.json`
  - `fa_unification_data_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode fa-unification --host 127.0.0.1 --port 7496 --client-id 9703 --account U22462030 --timeout 40 --export-dir exports`

---

## 4) Model Portfolios and the API

### Analysis
- Model portfolio access is validated through model-scoped account/position subscriptions.
- Runtime uses existing multi-account/model endpoints with FA-focused args.

### Implementation
- Added mode: `fa-model-portfolios`
- Requests:
  - `reqPositionsMulti`
  - `reqAccountUpdatesMulti`
- Inputs:
  - `--fa-model-code` (or fallback `--model-code`)
  - `--fa-account`
- Exports:
  - `fa_model_positions_<model>_*.json`
  - `fa_model_account_updates_<model>_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode fa-model-portfolios --host 127.0.0.1 --port 7496 --client-id 9704 --account U22462030 --timeout 40 --export-dir exports --fa-model-code Core --fa-account U22462030 --capture-seconds 4`

---

## 5) Placing Orders to a FA account

### Analysis
- FA orders are live-risk and now guarded by explicit allow flag.
- Runtime supports FA allocation fields on order object:
  - `FaGroup`, `FaMethod`, `FaPercentage`, `FaProfile`.

### Implementation
- Added mode: `fa-order`
- Safety gate:
  - requires `--fa-order-allow true`
- Guardrails:
  - action must be `BUY|SELL`
  - quantity and limit must be > 0
  - notional must be <= `--fa-max-notional`
  - requires either `--fa-order-group` or `--fa-order-profile`
- Exports:
  - `fa_order_request_*.json`
  - `fa_order_status_*.json`

### Commands
- Guard validation (blocked by default):
  - `dotnet run --project src/Harvester.App -- --mode fa-order --host 127.0.0.1 --port 7496 --client-id 9705 --account U22462030 --timeout 30 --export-dir exports --fa-order-symbol SIRI --fa-order-action BUY --fa-order-qty 1 --fa-order-limit 5 --fa-order-group TestGroup --fa-order-method EqualQuantity`
- Enabled request example:
  - `dotnet run --project src/Harvester.App -- --mode fa-order --host 127.0.0.1 --port 7496 --client-id 9706 --account U22462030 --timeout 30 --export-dir exports --fa-order-allow true --fa-order-account U22462030 --fa-order-symbol SIRI --fa-order-action BUY --fa-order-qty 1 --fa-order-limit 5 --fa-max-notional 100 --fa-order-group TestGroup --fa-order-method EqualQuantity`

---

## Added CLI arguments for FA modes

- Model / account inputs:
  - `--fa-account`
  - `--fa-model-code`

- FA order inputs:
  - `--fa-order-allow`
  - `--fa-order-account`
  - `--fa-order-symbol`
  - `--fa-order-action`
  - `--fa-order-qty`
  - `--fa-order-limit`
  - `--fa-max-notional`
  - `--fa-order-group`
  - `--fa-order-method`
  - `--fa-order-percentage`
  - `--fa-order-profile`
  - `--fa-order-exchange`
  - `--fa-order-primary-exchange`
  - `--fa-order-currency`

## Compatibility note in this workspace

- Current account appears to be non-FA for FA data requests (`code=321`, “FA data operations ignored for non FA customers”).
- Runtime handles this expected state gracefully for FA chapter probing modes and still exports artifacts for diagnostics.
