# Cryptocurrency — Step-by-Step Implementation

This implementation follows the IBKR cryptocurrency chapter and covers each requested subsection in order.

## 1) Cryptocurrency Trading Permissions

### Analysis
- A practical permissions probe needs both contract lookup and a short market data request.
- Runtime captures whether contract details resolve, whether ticks arrive, and request-scoped errors.

### Implementation
- Added mode: `crypto-permissions`
- Request flow:
  - `reqContractDetails` for crypto contract
  - `reqMktData` for short capture window
- Export: `crypto_permissions_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode crypto-permissions --host 127.0.0.1 --port 7496 --client-id 9601 --account U22462030 --timeout 45 --export-dir exports --market-data-type 3 --capture-seconds 6 --crypto-symbol BTC --crypto-exchange PAXOS --crypto-currency USD`

---

## 2) Contract Definition Example

### Analysis
- Contract-definition verification should confirm a resolvable `CRYPTO` contract and export normalized fields.

### Implementation
- Added mode: `crypto-contract`
- Request flow:
  - `reqContractDetails` for `ContractFactory.Crypto(...)`
- Export: `crypto_contract_details_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode crypto-contract --host 127.0.0.1 --port 7496 --client-id 9602 --account U22462030 --timeout 45 --export-dir exports --crypto-symbol BTC --crypto-exchange PAXOS --crypto-currency USD`

---

## 3) Streaming Market Data

### Analysis
- Streaming follows the same top-of-book capture pattern used in other modes, using a crypto contract.

### Implementation
- Added mode: `crypto-streaming`
- Request flow:
  - `reqMarketDataType`
  - `reqMktData`
  - capture for `--capture-seconds`
- Exports:
  - `crypto_top_data_*.json`
  - `crypto_top_data_type_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode crypto-streaming --host 127.0.0.1 --port 7496 --client-id 9603 --account U22462030 --timeout 45 --export-dir exports --market-data-type 3 --capture-seconds 12 --crypto-symbol BTC --crypto-exchange PAXOS --crypto-currency USD`

---

## 4) Historical Market Data

### Analysis
- Historical bar requests for crypto reuse the existing historical-bars infrastructure and limitation checks.
- In the current pinned API package, crypto requests can surface compatibility code `10285`.

### Implementation
- Added mode: `crypto-historical`
- Request flow:
  - `reqHistoricalData` against crypto contract
  - waits for `historicalDataEnd` when available
  - on `code=10285`, exports current rows with compatibility warning
- Export: `crypto_historical_bars_*.json`

### Command
- `dotnet run --project src/Harvester.App -- --mode crypto-historical --host 127.0.0.1 --port 7496 --client-id 9604 --account U22462030 --timeout 45 --export-dir exports --crypto-symbol BTC --crypto-exchange PAXOS --crypto-currency USD --hist-duration "1 D" --hist-barsize "1 hour" --hist-what TRADES --hist-use-rth 0 --hist-format-date 1`

---

## 5) Order Placement

### Analysis
- Crypto order placement is live-risk and must be explicitly gated.
- Runtime enforces action, quantity, price, and max-notional checks before transmission.

### Implementation
- Added mode: `crypto-order`
- Safety gate:
  - requires `--crypto-order-allow true`
- Guardrails:
  - `BUY|SELL` action only
  - quantity > 0
  - limit > 0
  - notional <= `--crypto-max-notional`
- Exports:
  - `crypto_order_request_*.json`
  - `crypto_order_status_*.json`

### Commands
- Guard validation (blocked by default):
  - `dotnet run --project src/Harvester.App -- --mode crypto-order --host 127.0.0.1 --port 7496 --client-id 9605 --account U22462030 --timeout 30 --export-dir exports --crypto-symbol BTC --crypto-exchange PAXOS --crypto-currency USD --crypto-order-action BUY --crypto-order-qty 0.001 --crypto-order-limit 30000`
- Enabled example:
  - `dotnet run --project src/Harvester.App -- --mode crypto-order --host 127.0.0.1 --port 7496 --client-id 9606 --account U22462030 --timeout 30 --export-dir exports --crypto-order-allow true --crypto-symbol BTC --crypto-exchange PAXOS --crypto-currency USD --crypto-order-action BUY --crypto-order-qty 0.001 --crypto-order-limit 30000 --crypto-max-notional 100`

---

## Added CLI arguments for crypto modes

- Common crypto contract args:
  - `--crypto-symbol`
  - `--crypto-exchange`
  - `--crypto-currency`

- Crypto order control args:
  - `--crypto-order-allow`
  - `--crypto-order-action`
  - `--crypto-order-qty`
  - `--crypto-order-limit`
  - `--crypto-max-notional`

## Package compatibility note

- Current pinned package: `IBApi 1.0.0-preview-975`
- Observed runtime compatibility signal in this workspace:
  - `code=10285` (fractional size rules support)
- Runtime behavior:
  - treats `10285` as non-blocking
  - exports current data and surfaces warning text in output/errors
