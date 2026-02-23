# Fundamental Data — Step-by-Step Implementation

References:
- https://interactivebrokers.github.io/tws-api/fundamentals.html
- https://interactivebrokers.github.io/tws-api/wshe_filters.html

This implementation begins the Fundamental Data chapter with two dedicated modes:
1) `fundamental-data`
2) `wsh-filters` (upgrade-readiness diagnostics for WSH corporate event filters)

## 1) Fundamental Data

### Analysis
- The pinned API exposes:
  - `reqFundamentalData(...)`
  - `cancelFundamentalData(...)`
  - callback `fundamentalData(...)`
- Runtime captures callback payloads and exports them as JSON rows.

### Implementation
- Added mode: `fundamental-data`
- Request flow:
  - build contract (stock)
  - call `reqFundamentalData` with `--fund-report-type`
  - wait for callback or warning timeout
  - cancel request and export rows
- Export:
  - `fundamental_data_<symbol>_<reportType>_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode fundamental-data --host 127.0.0.1 --port 7496 --client-id 9801 --account U22462030 --timeout 45 --export-dir exports --symbol SIRI --primary-exchange NASDAQ --fund-report-type ReportSnapshot`

### Current workspace validation
- Mode executed successfully and exported rows.
- Current account returned entitlement error `code=10358` (fundamentals not allowed), so output was empty with warning.

---

## 2) Wall Street Horizon Corporate Event filters

### Analysis
- The requested subsection uses WSH APIs (`reqWshMetaData`, `reqWshEventData`, related callbacks).
- The currently pinned package `IBApi 1.0.0-preview-975` does **not** expose those APIs in this workspace.

### Implementation
- Added mode: `wsh-filters`
- Runtime checks API surface support via reflection and exports readiness diagnostics.
- Export:
  - `wsh_filters_support_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode wsh-filters --host 127.0.0.1 --port 7496 --client-id 9802 --account U22462030 --timeout 30 --export-dir exports --wsh-filter-json "{\"watchlist\":true,\"country\":\"US\"}"`

### Current workspace validation
- Mode executed successfully.
- Export confirms WSH methods/callbacks are unavailable in the pinned assembly and includes requested filter JSON for upgrade planning.

---

## Added CLI arguments

- `--fund-report-type`
- `--wsh-filter-json`

## Compatibility note

- Fundamental data may require account entitlement (`code=10358` seen in this workspace).
- Live WSH filter execution requires upgrading to an IB API package that includes WSH request/callback methods.
