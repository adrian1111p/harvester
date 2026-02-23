# Display Groups — Step-by-Step Implementation

Reference: https://interactivebrokers.github.io/tws-api/display_groups.html

This chapter is implemented with one mode per requested subsection.

## 1) Query Display Groups

### Analysis
- Calls `queryDisplayGroups(reqId)`.
- Captures callback rows from `displayGroupList(reqId, groups)`.

### Implementation
- Added mode: `display-groups-query`
- Export:
  - `display_groups_query_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode display-groups-query --host 127.0.0.1 --port 7496 --client-id 9931 --account U22462030 --timeout 35 --export-dir exports`

---

## 2) Subscribe To Group Events

### Analysis
- Calls `subscribeToGroupEvents(reqId, groupId)`.
- Captures updates from `displayGroupUpdated(reqId, contractInfo)`.

### Implementation
- Added mode: `display-groups-subscribe`
- Behavior:
  - subscribe
  - await first update callback (or timeout warning)
  - capture additional updates for `--display-group-capture-seconds`
  - unsubscribe
- Exports:
  - `display_groups_subscribe_updates_*.json`
  - `display_groups_subscribe_request_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode display-groups-subscribe --host 127.0.0.1 --port 7496 --client-id 9932 --account U22462030 --timeout 40 --export-dir exports --display-group-id 1 --display-group-capture-seconds 4`

---

## 3) Update Display Group

### Analysis
- Uses a live subscription request id and sends `updateDisplayGroup(reqId, contractInfo)`.
- Waits for update callback to confirm event flow.

### Implementation
- Added mode: `display-groups-update`
- Exports:
  - `display_groups_update_updates_*.json`
  - `display_groups_update_request_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode display-groups-update --host 127.0.0.1 --port 7496 --client-id 9933 --account U22462030 --timeout 40 --export-dir exports --display-group-id 1 --display-group-contract-info "265598@SMART"`

---

## 4) Unsubscribe From Group Events

### Analysis
- Issues subscribe then unsubscribe to validate unsubscribe workflow with deterministic request tracking.

### Implementation
- Added mode: `display-groups-unsubscribe`
- Export:
  - `display_groups_unsubscribe_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode display-groups-unsubscribe --host 127.0.0.1 --port 7496 --client-id 9934 --account U22462030 --timeout 30 --export-dir exports --display-group-id 1`

---

## Added display group CLI arguments

- `--display-group-id`
- `--display-group-contract-info`
- `--display-group-capture-seconds`

## Compatibility note

- On non-FA/non-display-group-enabled sessions, IBKR may return validation/permission responses (commonly `321` or `344`).
- Runtime treats these as non-blocking in Display Group chapter modes and still exports diagnostics.
