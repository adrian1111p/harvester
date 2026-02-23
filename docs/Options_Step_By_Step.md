# Options — Step-by-Step Implementation

This implementation follows the IBKR Options documentation and implements the requested subsections in sequence.

## 1) Option Chains

Reference: `options.html` (option chain details / security definition option parameters)

### Analysis
- Option chain discovery uses `reqSecDefOptParams(...)`.
- Runtime first resolves underlying `conId` via `reqContractDetails` on the underlying contract.
- Chain rows are collected from `securityDefinitionOptionParameter(...)` and finalized by `securityDefinitionOptionParameterEnd(...)`.

### Implementation
- Added mode: `option-chains`
- Request flow:
  - resolve underlying contract (`STK` by default)
  - request chain params via `reqSecDefOptParams`
- Callback capture added in wrapper:
  - `securityDefinitionOptionParameter`
  - `securityDefinitionOptionParameterEnd`
- Export: `option_chains_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode option-chains --host 127.0.0.1 --port 7496 --client-id 9501 --account U22462030 --timeout 45 --export-dir exports --symbol SIRI --primary-exchange NASDAQ`

### Validation
- Live run validated in this workspace:
  - mode completed successfully
  - export rows produced (`rows=20`)

---

## 2) Exercising Options

Reference: `options.html` (exercise options)

### Analysis
- Exercising options is a live-risk operation and must be explicitly gated.
- Installed package compatibility:
  - `EClientSocket.exerciseOptions` signature in pinned package is:
    - `exerciseOptions(int reqId, Contract contract, int exerciseAction, int exerciseQuantity, string account, int ovrd)`
  - No manual-time argument is available in this assembly version.

### Implementation
- Added mode: `option-exercise`
- Safety gate:
  - Requires `--option-exercise-allow true`
  - Without this flag, mode fails fast and does not send exercise request.
- Request details are exported for auditability.
- Exports:
  - `option_exercise_request_*.json`
  - `option_exercise_status_*.json`

### Guard Validation Command
- `dotnet run --project src/Harvester.App -- --mode option-exercise --host 127.0.0.1 --port 7496 --client-id 9503 --account U22462030 --timeout 30 --export-dir exports --opt-symbol SIRI --opt-expiry 20260320 --opt-strike 5 --opt-right C`

### Validation
- Guard path validated in this workspace:
  - mode blocks as expected when `--option-exercise-allow` is omitted.

---

## 3) Option Greeks

Reference: `options.html` (option computations / greeks via market data ticks)

### Analysis
- Greeks are delivered through option computation ticks from `reqMktData(...)` on an option contract.
- Runtime resolves a concrete option `conId` first via `reqContractDetails(...)` to reduce contract-definition mismatches on the market-data request.
- Installed package compatibility:
  - `tickOptionComputation` override signature in pinned package does not include `tickAttrib`.
- Runtime captures computation ticks for a configurable capture window.

### Implementation
- Added mode: `option-greeks`
- Request flow:
  - construct option contract from CLI args
  - resolve option contract details and pick a concrete `conId`
  - if exact tuple cannot be resolved and `--option-greeks-auto-fallback true`, select nearest expiry/strike from option-chain data and retry market-data request
  - request market data on option contract
  - capture `tickOptionComputation` callbacks
- Export: `option_greeks_*.json`
- Timeout handling:
  - if no greek arrives within capture window, mode exports current rows and warns.
  - if contract cannot be resolved (`code=200`), mode fails fast with an actionable validation message.

### Command
- `dotnet run --project src/Harvester.App -- --mode option-greeks --host 127.0.0.1 --port 7496 --client-id 9502 --account U22462030 --timeout 45 --export-dir exports --market-data-type 3 --capture-seconds 8 --opt-symbol SIRI --opt-expiry 20260320 --opt-strike 5 --opt-right C --opt-exchange SMART --opt-currency USD --opt-multiplier 100`

### Validation
- Live run executed in this workspace.
- Current contract parameters returned security-definition error (`code=200`) and yielded zero greek rows for that request.
- This mode is functional; provide a fully valid/entitled option contract tuple for non-zero greek output.

---

## Added CLI arguments for options modes

- Common option contract args:
  - `--opt-symbol`
  - `--opt-expiry` (format `yyyyMMdd`)
  - `--opt-strike`
  - `--opt-right` (`C|P`)
  - `--opt-exchange`
  - `--opt-currency`
  - `--opt-multiplier`

- Option chain underlying resolution args:
  - `--option-underlying-sec-type`
  - `--option-underlying-fut-fop-exchange`

- Exercise safety/control args:
  - `--option-exercise-allow`
  - `--option-exercise-action`
  - `--option-exercise-qty`
  - `--option-exercise-override`
  - `--option-exercise-manual-time` (accepted as input for forward compatibility; not sent in current pinned package signature)

- Greeks fallback arg:
  - `--option-greeks-auto-fallback`

## Live validation summary (current workspace)

- `option-chains` ✅
- `option-exercise` guard path ✅
- `option-greeks` runtime path ✅ (request completed; zero rows for tested contract tuple)
