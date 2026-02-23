# Error Handling — Step-by-Step Implementation

References:
- https://interactivebrokers.github.io/tws-api/error_handling.html
- https://interactivebrokers.github.io/tws-api/message_codes.html

This chapter is implemented with one runtime mode that exports all Message Codes subsections.

## Message Codes

### Implementation
- Added mode: `error-codes`
- Mode exports:
  - System Message Codes
  - Warning Message Codes
  - Client Error Codes
  - TWS Error Codes
- Also exports observed runtime errors captured in the current session and uncatalogued observed entries.

### Command
- `dotnet run --project src/Harvester.App -- --mode error-codes --host 127.0.0.1 --port 7496 --client-id 9901 --account U22462030 --timeout 30 --export-dir exports`

---

## System Message Codes

### Coverage
- Export file: `error_codes_system_*.json`
- Includes connectivity/system reset style codes (for example `1100`, `1101`, `1102`, `1300`).
- `ObservedCount` field shows whether each code appeared during the current run.

---

## Warning Message Codes

### Coverage
- Export file: `error_codes_warning_*.json`
- Includes warning/informational farm and subscription codes (for example `2100`–`2110`, `2158`).
- `ObservedCount` tracks runtime occurrence.

---

## Client Error Codes

### Coverage
- Export file: `error_codes_client_*.json`
- Built by reflecting `IBApi.EClientErrors` from the installed assembly.
- This gives the package-accurate client-side validation/error table for your pinned API version.

---

## TWS Error Codes

### Coverage
- Export file: `error_codes_tws_*.json`
- Includes common request/order/permission/server-side codes used in this project (for example `100`, `200`, `300`, `321`, `354`, `420`, `10358`, and WSH-related `10276`–`10284`).
- `ObservedCount` tracks runtime occurrence.

---

## Additional diagnostics exports

- `error_codes_observed_*.json`
  - Parsed code/message rows from callback errors observed during the run.
- `error_codes_uncatalogued_*.json`
  - Observed codes that were not present in the current curated system/warning/client/TWS catalogs.

This helps identify gaps as you expand catalog coverage over time.
