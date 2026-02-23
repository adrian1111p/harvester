# IBKR Class Implementation Map

This project now includes a dedicated class layer that maps directly to the IBKR TWS API documentation sections requested.

## Documentation to class mapping

## 1) Client wrapper (`client_wrapper.html`)

- Implemented in [src/Harvester.App/IBKR/Wrapper/HarvesterEWrapper.cs](src/Harvester.App/IBKR/Wrapper/HarvesterEWrapper.cs)
- Uses `DefaultEWrapper` and handles core callbacks:
  - `nextValidId`
  - `managedAccounts`
  - `contractDetails` / `contractDetailsEnd`
  - `openOrder`, `orderStatus`
  - `error` overloads

## 2) Connection lifecycle (`connection.html`)

- Implemented in [src/Harvester.App/IBKR/Connection/IbkrSession.cs](src/Harvester.App/IBKR/Connection/IbkrSession.cs)
- Covers documented flow:
  - `eConnect`
  - create `EReader` **after connect**
  - start reader loop using `EReaderSignal`
  - `startApi`
  - wait for handshake callbacks (`nextValidId`, `managedAccounts`)

## 3) Contracts (`contracts.html`)

- Implemented in [src/Harvester.App/IBKR/Contracts/ContractFactory.cs](src/Harvester.App/IBKR/Contracts/ContractFactory.cs)
- Includes builders for common contract types:
  - `Stock`
  - `Forex`
  - `Future`
  - `Option`
  - `Cfd`
  - `Index`
  - `Crypto`
  - `Bag` (combo)

## 4) Orders (`orders.html`)

- Implemented in [src/Harvester.App/IBKR/Orders/OrderFactory.cs](src/Harvester.App/IBKR/Orders/OrderFactory.cs)
- Includes common order templates:
  - `Market`
  - `Limit`
  - `Stop`
  - `StopLimit`
  - `Bracket` (parent + take-profit + stop-loss with transmit chaining)
  - `MarketOnClose`
  - `LimitOnClose`
  - `MarketIfTouched`
  - `PeggedToMarket`
  - `PeggedToMidpoint`
  - `Relative`
  - `TrailingStop`
  - `TrailingStopLimit`
  - `ScaleLimit`
  - `ApplyOcaGroup`
  - `Algo`
  - `Twap`
  - `Vwap`
  - `Adaptive`

## Important note

The low-level IBKR API classes themselves (`EClientSocket`, `EWrapper`, `Contract`, `Order`, etc.) are provided by the official `IBApi` package and are not re-implemented in user code. The classes above provide the application-side implementation and structure around those official classes.

## Runtime integration status

- Runtime modes (`connect`, `orders`, `positions`, `snapshot-all`) are now wired through this class layer:
 - Runtime modes (`connect`, `orders`, `positions`, `snapshot-all`, `contracts-validate`, `orders-dryrun`, `orders-place-sim`, `orders-whatif`) are now wired through this class layer:
 - Runtime modes (`connect`, `orders`, `positions`, `snapshot-all`, `contracts-validate`, `orders-dryrun`, `orders-place-sim`, `orders-whatif`, `top-data`, `market-depth`, `realtime-bars`, `market-data-all`) are now wired through this class layer:
  - [src/Harvester.App/IBKR/Connection/IbkrSession.cs](src/Harvester.App/IBKR/Connection/IbkrSession.cs)
  - [src/Harvester.App/IBKR/Runtime/SnapshotEWrapper.cs](src/Harvester.App/IBKR/Runtime/SnapshotEWrapper.cs)
  - [src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs](src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs)
  - [src/Harvester.App/Program.cs](src/Harvester.App/Program.cs)
