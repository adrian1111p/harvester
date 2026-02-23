# IBApi Namespaces and Classes — Gap Analysis (2026-02-23)

Sources analyzed:
- https://interactivebrokers.github.io/tws-api/namespaceIBApi.html
- https://interactivebrokers.github.io/tws-api/annotated.html

Workspace compared:
- src/Harvester.App/IBKR
- docs/IBKR_Class_Implementation_Map.md

## 1) What is already covered well

### Core client-wrapper architecture
- Implemented:
  - EClientSocket
  - EReader
  - EReaderSignal / EReaderMonitorSignal
  - DefaultEWrapper / EWrapper callback override model
- Evidence:
  - src/Harvester.App/IBKR/Connection/IbkrSession.cs
  - src/Harvester.App/IBKR/Wrapper/HarvesterEWrapper.cs
  - src/Harvester.App/IBKR/Runtime/SnapshotEWrapper.cs

### Core market/account/order types
- Implemented and actively used:
  - Contract, ContractDetails
  - Order, OrderState
  - Execution, ExecutionFilter
  - TagValue
  - ScannerSubscription
  - TickAttrib, HistoricalTick, HistoricalTickBidAsk, HistoricalTickLast
  - EClientErrors, CodeMsgPair (error-codes mode reflection)
- Evidence:
  - src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs
  - src/Harvester.App/IBKR/Runtime/SnapshotEWrapper.cs
  - src/Harvester.App/IBKR/Orders/OrderFactory.cs

### Display Groups coverage
- Fully implemented per requested subsections:
  - queryDisplayGroups
  - subscribeToGroupEvents
  - updateDisplayGroup
  - unsubscribeFromGroupEvents
- Evidence:
  - src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs
  - src/Harvester.App/IBKR/Runtime/SnapshotEWrapper.cs

## 2) Partially covered classes from IBApi class list

### AccountSummaryTags
- Current state:
  - Account summary requests are implemented using raw tag string input.
- Gap:
  - No explicit class-level usage of AccountSummaryTags constants.
- Impact:
  - Lower discoverability and more typo risk in tag strings.

### ComboLeg
- Current state:
  - ContractFactory supports BAG with ComboLeg collection.
- Gap:
  - No dedicated runtime mode demonstrating combo contract resolution/order placement lifecycle.
- Impact:
  - Capability exists but is not chapter-operationalized.

### TickAttribBidAsk / TickAttribLast support path
- Current state:
  - Captured through historical tick callbacks in SnapshotEWrapper.
- Gap:
  - No dedicated chapter doc section that emphasizes these tick-attrib structures as first-class outputs.

## 3) Not yet implemented (or not explicitly used) from the analyzed class chapters

### Order-complexity classes
- OrderComboLeg
- DeltaNeutralContract
- SoftDollarTier

### Conditional-order classes
- PriceCondition
- TimeCondition
- VolumeCondition
- PercentChangeCondition
- ExecutionCondition

### Execution detail enhancement classes
- CommissionReport
- Liquidity

### News classes and APIs
- NewsProvider and related News request/callback flows are not currently implemented.
- This is aligned with your explicit instruction to skip News for now.

## 4) Extraction we should do next from these chapters

This is the practical extraction shortlist from Namespaces + Classes that gives highest value for this codebase.

### Extract A — Typed Account Summary Tag Profile
- Pull in AccountSummaryTags constants and define named tag profiles.
- Minimal deliverable:
  - utility mapping profile name -> validated tag list
  - keep raw override path for flexibility
- Why:
  - reduces string errors
  - improves chapter readability

### Extract B — Conditional Orders Pack
- Implement factory helpers for:
  - PriceCondition
  - TimeCondition
  - VolumeCondition
  - PercentChangeCondition
  - ExecutionCondition
- Add one new runtime mode for dry-run construction/export of conditional orders.
- Why:
  - direct coverage of major missing classes
  - high instructional value with low live-risk if dry-run first

### Extract C — Combo and Delta-Neutral Foundations
- Add focused modes/docs for:
  - BAG + OrderComboLeg flows
  - DeltaNeutralContract request/serialization path
- Why:
  - unlocks advanced order workflows
  - leverages existing ComboLeg support in ContractFactory

### Extract D — Commission and Liquidity callback enrichment
- Capture and export:
  - commissionReport callback payloads
  - liquidity-related execution metadata
- Why:
  - improves post-trade analytics quality
  - aligns with existing execution snapshot modes

### Extract E — Soft Dollar Tier support
- Add optional order fields and export for SoftDollarTier in advanced order templates.
- Why:
  - completes advanced order class coverage from annotated list

## 5) What we should not extract yet

### News classes/APIs
- Keep deferred, per your direction to skip News.
- Current recommendation:
  - maintain as backlog item only
  - do not add runtime modes now

## 6) Suggested implementation order (best ROI)

1. Conditional Orders Pack (highest class-gap closure, low risk in dry-run mode)
2. Commission + Liquidity enrichment (improves existing execution value)
3. Typed AccountSummaryTags profile utility (quick quality win)
4. Combo/Delta-Neutral chapter pack
5. SoftDollarTier support

## 7) Compatibility guardrail reminder

The analyzed IBKR reference pages are marked deprecated in favor of IBKR Campus. Your pinned assembly behavior remains the source of truth for callable signatures in this workspace. Continue the existing pattern you already use:
- reflect/check API surface when needed
- degrade gracefully with diagnostics when methods are absent
- keep exports even on entitlement or validation limits
