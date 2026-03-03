# Strategy Flowcharts: V3, V6, V9

This document visualizes the current C# strategy execution flow for:
- `StrategyV3` (VWAP Reversion + BB + Keltner)
- `StrategyV6` (Opening Range Breakout)
- `StrategyV9` (L1/L2 score-based)

## V3 Flow (`StrategyV3`)

```mermaid
flowchart TD
    A[Start GenerateSignals] --> B[Compute HTF Guard from 1h/1d]
    B --> C[Loop bars i=50..N]
    C --> D{ATR valid?}
    D -- No --> C
    D -- Yes --> E{Price in range MinPrice..MaxPrice?}
    E -- No --> C
    E -- Yes --> F{L2/RVOL filters pass?\nL2Liquidity, SpreadZ, RVOL}
    F -- No --> C
    F -- Yes --> G[Read OFI, VWAP, BB%, RSI, Stoch]

    G --> H{In squeeze?\nBB inside KC}
    H -- Yes --> I[Increment squeezeCount]
    I --> C
    H -- No --> J{Squeeze just ended and enabled?}
    J -- Yes --> K{Price vs KC mid + side allowed + HTF guard}
    K -- Yes --> L[MakeSignal SQUEEZE]
    K -- No --> M[Continue checks]
    L --> C
    J -- No --> M

    M --> N{VWAP Reversion enabled?}
    N -- Yes --> O{Dist from VWAP beyond threshold?\n+ RSI/OFI confirm + side allowed + HTF guard}
    O -- Yes --> P[MakeSignal VWAP]
    O -- No --> Q[Continue checks]
    P --> C
    N -- No --> Q

    Q --> R{BB Bounce enabled?}
    R -- Yes --> S{BB% extreme + candle/stoch confirm + side allowed + HTF guard}
    S -- Yes --> T[MakeSignal BB]
    S -- No --> C
    T --> C
    R -- No --> C

    C --> U[Return signals]
```

## V6 Flow (`StrategyV6`)

```mermaid
flowchart TD
    A[Start GenerateSignals] --> B[Compute HTF bias]
    B --> C[Group bars by trading day]
    C --> D[For each day: compute Opening Range OR]
    D --> E{OR valid and OR range in ATR bounds?}
    E -- No --> D
    E -- Yes --> F[Init day counters longEntries/shortEntries]
    F --> G[Loop intraday bars from OR end]

    G --> H{ATR valid?}
    H -- No --> G
    H -- Yes --> I{Inside configured entry window?}
    I -- No --> G
    I -- Yes --> J{20MA distance <= MaxMaDistAtr?}
    J -- No --> G
    J -- Yes --> K{RVOL filter pass?}
    K -- No --> G
    K -- Yes --> L[Evaluate breakout/breakdown]

    L --> M{LONG break condition met?\n(cross/inside rule + OR high)}
    M -- Yes --> N{HTF allows long?\n(or IgnoreHtfBias)}
    N -- Yes --> O{VWAP align required and pass?}
    O -- Yes --> P[Build LONG signal\n(stop from OR/opposite/midpoint)]
    O -- No --> G
    N -- No --> G
    P --> G
    M -- No --> Q

    Q{SHORT break condition met?\n(cross/inside rule + OR low)} --> R{HTF allows short?\n(or IgnoreHtfBias)}
    R -- Yes --> S{VWAP align required and pass?}
    S -- Yes --> T[Build SHORT signal\n(stop from OR/opposite/midpoint)]
    S -- No --> G
    R -- No --> G
    T --> G

    G --> U[Return signals]
```

## V9 Flow (`StrategyV9`)

```mermaid
flowchart TD
    A[Start GenerateSignals] --> B{Enough bars >= 80?}
    B -- No --> Z[Return empty]
    B -- Yes --> C[Compute HTF bias from 1h/1d]
    C --> D[Loop bars i=60..N with cooldown]

    D --> E{Cooldown passed?}
    E -- No --> D
    E -- Yes --> F{ATR valid?}
    F -- No --> D
    F -- Yes --> G{Time filters pass?\nSkipFirstNMinutes + EntryWindows}
    G -- No --> D
    G -- Yes --> H{EMA9/EMA21 valid + VWAP distance <= max?}
    H -- No --> D
    H -- Yes --> I[Compute feature booleans:\nRVOL/L2/Spread/VolAccel/OFI/Candle/Trend/Pullback/VWAP side]

    I --> J[Build longScore and shortScore]
    J --> K{Require MTF align?}
    K -- Yes --> L{5m/15m alignment pass?}
    L -- No --> D
    L -- Yes --> M[Check entries]
    K -- No --> M

    M --> N{Long allowed + trendUp + longScore>=min\n+ RSI range + HTF guard}
    N -- Yes --> O[Compute swing-low stop\ncap with HardStopR*ATR]
    O --> P{riskPerShare>0?}
    P -- Yes --> Q[Emit LONG signal\nupdate lastSignalBar]
    P -- No --> R
    Q --> D

    N -- No --> R
    R{Short allowed + trendDown + shortScore>=min\n+ RSI range + HTF guard} --> S[Compute swing-high stop\ncap with HardStopR*ATR]
    S --> T{riskPerShare>0?}
    T -- Yes --> U[Emit SHORT signal\nupdate lastSignalBar]
    T -- No --> D
    U --> D

    D --> V[Return signals]
```

## Exit Simulation (all three)

All three strategies delegate trade simulation to `ExitEngine.SimulateTrade(...)` with strategy-specific exit configuration.
