# Image Strategy Implementation Matrix (2026-03-01)

This matrix maps each image in `docs/img` to current implementation status in Harvester.

## Evidence anchors

- Entry strategy surface: `ReplayScannerSingleShotEntryStrategy` in `src/Harvester.App/Strategy/ReplayStrategySystemLayout.cs`
- Buy setup analyzer: `AnalyzeBuySetupSignals` in `src/Harvester.App/Strategy/ReplayStrategySystemLayout.cs`
- Buy setup runtime gates:
  - `SCN_001_REQUIRE_BUY_SETUP_CONFIRMATION`
  - `SCN_001_REQUIRE_ENHANCED_BUY_SETUP_CONFIRMATION`
  in `src/Harvester.App/Strategy/ScannerCandidateReplayRuntime.cs`
- Scanner selection V2 engine: `src/Harvester.App/Strategy/ScannerSelectionEngineV2.cs`
- Real-time risk/exit monitor (conduct): `src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs`

## Status legend

- **Implemented**: directly supported by explicit strategy/gate logic.
- **Partial**: indirectly covered (e.g., risk/exit only or category-level overlap), but not as explicit entry setup matching the image family.
- **Missing**: no explicit strategy implementation found.

## Category summary

| Category | Count | Status |
|---|---:|---|
| buy-setup | 18 | Partial-to-Implemented (strong buy setup logic exists; per-image exactness not encoded) |
| sell-setup | 5 | Missing (no explicit sell-setup analyzer equivalent to buy setup) |
| breakout | 12 | Partial (some breakout-like checks in buy setup, but no dedicated breakout module family) |
| breakdown | 15 | Partial (strong breakdown/rejection exits, mostly trade-management side) |
| 123-pattern | 8 | Missing (no explicit 1-2-3 pattern module by name/logic contract) |
| exhaustion-trade | 15 | Partial (trend exhaustion exists in exits, not explicit entry family) |
| risk-management | 6 | Implemented (large real-time and replay risk/exit stack) |

## Image-by-image matrix

| Image | Category | Status | Notes |
|---|---|---|---|
| 1-buy-setup-1.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-2.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-3.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-4.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-5.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-6.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-7.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-8.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-9.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-10.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-11.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-12.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-13.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-14.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-15.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-16.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 1-buy-setup-17.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| buy-setup-18.png | buy-setup | Partial | Covered by buy setup analyzer and optional confirmations, not mapped as distinct image rule. |
| 2-sell-setup-1.png | sell-setup | Missing | No explicit sell setup analyzer equivalent found in entry strategy. |
| 2-sell-setup-2.png | sell-setup | Missing | No explicit sell setup analyzer equivalent found in entry strategy. |
| 2-sell-setup-3.png | sell-setup | Missing | No explicit sell setup analyzer equivalent found in entry strategy. |
| 2-sell-setup-4.png | sell-setup | Missing | No explicit sell setup analyzer equivalent found in entry strategy. |
| 2-sell-setup-5.png | sell-setup | Missing | No explicit sell setup analyzer equivalent found in entry strategy. |
| 3-breakout-1.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-2.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-3.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-4.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-5.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-6.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-7.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-8.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-9.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-10.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-11.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 3-breakout-12.png | breakout | Partial | Breakout-like elements exist in buy setup checks; no dedicated breakout strategy module. |
| 4-breakdown-1.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-2.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-3.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-4.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-5.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-6.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-7.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-8.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-9.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-10.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-11.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-12.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-13.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-14.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| breakdown-15.png | breakdown | Partial | Breakdown/rejection logic mostly represented in exit/risk trade-management family. |
| 123-pattern-1.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-2.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-3.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-4.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-5.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-6.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-7.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| 123-pattern-8.png | 123-pattern | Missing | No explicit 1-2-3 pattern strategy contract found. |
| exhaustion_trade-1.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-2.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-3.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-4.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-5.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-6.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-7.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-8.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-9.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-10.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-11.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-12.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-13.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-14.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| exhaustion_trade-15.png | exhaustion-trade | Partial | Trend exhaustion is implemented in trade management exits, not explicit entry family. |
| risk-management-1.png | risk-management | Implemented | Strong replay/live risk exits, conduct monitor, and safeguards are implemented. |
| risk-management-2.png | risk-management | Implemented | Strong replay/live risk exits, conduct monitor, and safeguards are implemented. |
| risk-management-3.png | risk-management | Implemented | Strong replay/live risk exits, conduct monitor, and safeguards are implemented. |
| risk-management-4.png | risk-management | Implemented | Strong replay/live risk exits, conduct monitor, and safeguards are implemented. |
| risk-management-5.png | risk-management | Implemented | Strong replay/live risk exits, conduct monitor, and safeguards are implemented. |
| risk-management-6.png | risk-management | Implemented | Strong replay/live risk exits, conduct monitor, and safeguards are implemented. |

## Notes

1. This matrix is filename/category-based because image OCR/text extraction is not currently available in this execution environment.
2. Status is evaluated against current code implementation contracts; not all category coverage implies exact visual-pattern parity for every image.
3. A follow-up implementation plan should prioritize:
   - Sell setup analyzer parity with buy setup.
   - Dedicated breakout and 1-2-3 entry gates.
   - Wiring those gates into both replay and live scanner paths.
