# Positions Monitor UI — Implementation Plan

> **Goal**: A real-time localhost web dashboard that displays the IBKR paper-account
> positions table (Symbol, Side, Shares, Avg Cost, Market Price, Market Value,
> Unrealized PnL, Realized PnL) with auto-refresh, colour-coded P&L, and totals row.

---

## Architecture Overview

```
┌─────────────────────────────┐
│  IBKR TWS (127.0.0.1:7497) │
└────────────┬────────────────┘
             │ TWS API (EClientSocket)
             ▼
┌─────────────────────────────────────────────────────────┐
│  Harvester.App  (new mode: positions-monitor-ui)        │
│                                                         │
│  ┌───────────────┐    ┌──────────────────────────────┐  │
│  │ IBKR Poller   │───▶│ In-Memory Position Store     │  │
│  │ (every 2s)    │    │  ConcurrentDictionary<sym,P> │  │
│  └───────────────┘    └──────────┬───────────────────┘  │
│                                  │                       │
│  ┌───────────────────────────────▼───────────────────┐  │
│  │ ASP.NET Core Minimal API  (Kestrel, localhost)    │  │
│  │                                                    │  │
│  │  GET  /                     → index.html (SPA)     │  │
│  │  GET  /api/positions        → JSON snapshot        │  │
│  │  GET  /api/account-summary  → JSON account info    │  │
│  │  WS   /ws/positions         → WebSocket push       │  │
│  └────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│  Browser  http://localhost:5100 │
│  Single-page HTML + vanilla JS │
│  Auto-updates via WebSocket    │
└─────────────────────────────────┘
```

**Key design decisions:**

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Web framework | ASP.NET Core Minimal API (in-process) | Already .NET 9 project; zero new dependencies |
| Real-time push | Native WebSocket middleware | Lighter than SignalR for a single-table feed |
| Frontend | Single `index.html` with vanilla JS + CSS | No build step, no npm, embedded as resource |
| Data source | Reuse existing `IBrokerAdapter` + `reqAccountUpdates` | Same IBKR API call as `account-updates` mode |
| Refresh rate | 2-second poll from IBKR, instant WS push to browser | Fast enough for monitoring, stays within TWS rate limits |

---

## Step-by-Step Implementation

### Phase 1: Backend — New `positions-monitor-ui` Mode (4 files)

---

#### Step 1.1 — Add `RunMode.PositionsMonitorUi` Enum Value

**File:** `src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs`

- Add `PositionsMonitorUi` to the `RunMode` enum (near existing `PositionsMonitor1Pct` entries).
- Add case `"positions-monitor-ui" => RunMode.PositionsMonitorUi` in `ParseRunMode()`.
- Add dispatch case in the main `switch` to call `RunPositionsMonitorUiMode(...)`.

```csharp
case RunMode.PositionsMonitorUi:
    await RunPositionsMonitorUiMode(client, brokerAdapter, runtimeCts.Token);
    break;
```

---

#### Step 1.2 — Create `PositionMonitorStore.cs` (In-Memory State)

**File:** `src/Harvester.App/Monitor/PositionMonitorStore.cs`

Thread-safe store that the IBKR poller writes to and the web layer reads from.

```csharp
namespace Harvester.App.Monitor;

public sealed class PositionMonitorStore
{
    // ── Position data ──────────────────────────────────────────
    public record PositionRow(
        string Symbol,
        string Side,          // "LONG" | "SHORT" | "FLAT"
        double Position,
        double AverageCost,
        double MarketPrice,
        double MarketValue,
        double UnrealizedPnl,
        double RealizedPnl,
        DateTime TimestampUtc
    );

    public record AccountSummaryRow(
        double NetLiquidation,
        double AvailableFunds,
        double BuyingPower,
        double MaintMarginReq,
        double TotalCashValue,
        DateTime TimestampUtc
    );

    // ── State ──────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, PositionRow> _positions = new();
    private AccountSummaryRow _accountSummary = new(0, 0, 0, 0, 0, DateTime.UtcNow);
    private long _version;   // monotonic counter for change detection

    // ── Write (called by IBKR poller) ──────────────────────────
    public void Update(string symbol, PositionRow row) { ... }
    public void UpdateAccountSummary(AccountSummaryRow summary) { ... }

    // ── Read (called by web layer) ─────────────────────────────
    public (long Version, IReadOnlyList<PositionRow> Positions) GetSnapshot() { ... }
    public AccountSummaryRow GetAccountSummary() => _accountSummary;

    // ── Change notification (for WebSocket push) ───────────────
    public event Action? OnChanged;
}
```

---

#### Step 1.3 — Create `IbkrPositionPoller.cs` (Background Poller)

**File:** `src/Harvester.App/Monitor/IbkrPositionPoller.cs`

Runs a loop that calls `reqAccountUpdates` every 2 seconds and writes results into the store.

```
LOOP (every 2 seconds):
  1. Call reqAccountUpdates(true, account)
  2. Wait for updatePortfolioValue callbacks (up to 3s timeout)
  3. For each portfolio row:
     - Compute side = position > 0 ? "LONG" : position < 0 ? "SHORT" : "FLAT"
     - Write PositionRow into PositionMonitorStore
  4. Also capture account summary values
  5. Fire store.OnChanged event
  6. Call reqAccountUpdates(false, account) to unsubscribe
```

**Key details:**
- Reuses the existing `IBrokerAdapter` and `EClientSocket` from the runtime.
- Uses the same callback pattern already proven in `RunAccountUpdatesMode()`.
- Only writes rows where `Position != 0` (skip FLAT symbols).
- Thread-safe: poller is the only writer.

---

#### Step 1.4 — Create `PositionsWebServer.cs` (Kestrel + WebSocket)

**File:** `src/Harvester.App/Monitor/PositionsWebServer.cs`

Minimal ASP.NET Core web server that serves the UI and provides API/WebSocket endpoints.

```csharp
public static class PositionsWebServer
{
    public static async Task RunAsync(
        PositionMonitorStore store,
        int port,
        CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");

        var app = builder.Build();

        // ── Static HTML ────────────────────────────────────────
        app.MapGet("/", () => Results.Content(
            EmbeddedHtml.IndexHtml, "text/html"));

        // ── REST API ───────────────────────────────────────────
        app.MapGet("/api/positions", () =>
        {
            var (version, positions) = store.GetSnapshot();
            return Results.Json(new { version, positions });
        });

        app.MapGet("/api/account-summary", () =>
            Results.Json(store.GetAccountSummary()));

        // ── WebSocket ──────────────────────────────────────────
        app.UseWebSockets();
        app.Map("/ws/positions", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await HandleWebSocket(ws, store, token);
        });

        await app.RunAsync(token);
    }

    private static async Task HandleWebSocket(
        WebSocket ws,
        PositionMonitorStore store,
        CancellationToken token)
    {
        long lastVersion = -1;
        var tcs = new TaskCompletionSource();

        void OnChanged() => tcs.TrySetResult();
        store.OnChanged += OnChanged;

        try
        {
            while (!token.IsCancellationRequested
                   && ws.State == WebSocketState.Open)
            {
                var (version, positions) = store.GetSnapshot();
                if (version != lastVersion)
                {
                    lastVersion = version;
                    var json = JsonSerializer.SerializeToUtf8Bytes(
                        new { version, positions,
                              account = store.GetAccountSummary() });
                    await ws.SendAsync(json,
                        WebSocketMessageType.Text, true, token);
                }
                tcs = new TaskCompletionSource();
                await Task.WhenAny(tcs.Task, Task.Delay(2000, token));
            }
        }
        finally { store.OnChanged -= OnChanged; }
    }
}
```

**NuGet dependency needed:**
```xml
<PackageReference Include="Microsoft.AspNetCore.App" />
```
Actually, since .NET 9 console apps can reference the shared framework, add to `.csproj`:
```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

---

### Phase 2: Frontend — Single-File HTML Dashboard (1 file)

---

#### Step 2.1 — Create `index.html` (Embedded SPA)

**File:** `src/Harvester.App/Monitor/wwwroot/index.html`

Single HTML file with inline CSS and JS — no build tools required.

**Layout:**

```
┌──────────────────────────────────────────────────────────────┐
│  HARVESTER POSITIONS MONITOR          DUN559573    18:22 UTC │
├──────────────────────────────────────────────────────────────┤
│  Net Liq: $1,003,549  │  Avail Funds: $1,003,442  │  Margin │
├──────────┬──────┬──────┬──────┬──────┬───────┬───────┬───────┤
│  Symbol  │ Side │  Qty │ AvgC │  Mkt │ MktVal│  UPnL │  RPnL │
├──────────┼──────┼──────┼──────┼──────┼───────┼───────┼───────┤
│  ROLR    │ LONG │    4 │ 4.44 │ 4.31 │ 17.24 │ -0.50 │  0.00 │
│  AVXL    │ LONG │    4 │ 5.10 │ 5.00 │ 19.99 │ -0.42 │  0.00 │
│  NFE     │SHORT │   -4 │ 1.11 │ 1.12 │ -4.50 │ -0.06 │  0.00 │
│  ORKT    │SHORT │   -4 │ 1.05 │ 1.06 │ -4.26 │ -0.07 │  0.00 │
├──────────┴──────┴──────┴──────┴──────┼───────┼───────┼───────┤
│                              TOTALS  │ 28.47 │ -1.05 │  0.00 │
└──────────────────────────────────────┴───────┴───────┴───────┘
│  Status: ● Connected   Last update: 0.3s ago                │
└──────────────────────────────────────────────────────────────┘
```

**Frontend implementation details:**

| Feature | Implementation |
|---------|---------------|
| WebSocket connection | `new WebSocket("ws://localhost:5100/ws/positions")` with auto-reconnect |
| Table rendering | `<table>` rebuilt via `innerHTML` on each WS message |
| P&L colouring | Green (`#00c853`) for positive, Red (`#ff1744`) for negative, Grey for zero |
| Side badge | Green pill for LONG, Red pill for SHORT |
| Totals row | Computed client-side from position array: `SUM(marketValue)`, `SUM(unrealizedPnl)`, `SUM(realizedPnl)` |
| Staleness indicator | JS timer comparing `lastUpdateTs` to `Date.now()`, shows amber if >5s, red if >15s |
| Sorting | Click column headers to sort ascending/descending |
| Dark theme | Dark background (#1a1a2e), high-contrast text, matches terminal aesthetic |
| Responsive | CSS grid/flexbox, works on any screen size |
| Flash animation | Brief CSS flash on cells whose values changed since last update |

**JavaScript pseudo-code:**

```javascript
let ws;
let lastData = {};

function connect() {
    ws = new WebSocket(`ws://${location.host}/ws/positions`);
    ws.onmessage = (e) => {
        const data = JSON.parse(e.data);
        renderAccountBar(data.account);
        renderTable(data.positions);
        updateStatus('connected', new Date());
        lastData = data;
    };
    ws.onclose = () => {
        updateStatus('disconnected');
        setTimeout(connect, 2000);  // auto-reconnect
    };
}

function renderTable(positions) {
    // Filter out Position == 0
    // Sort by current sortColumn/sortDirection
    // Build <tr> rows with color-coded PnL cells
    // Compute totals row
    // Detect changed cells vs lastData → add flash class
    // Set table.innerHTML
}

function updateStatus(state, timestamp) {
    // Update status bar: green dot + "Connected" or red dot + "Disconnected"
    // Start interval timer showing "Last update: Xs ago"
}

connect();
```

---

### Phase 3: Wire It All Together

---

#### Step 3.1 — Implement `RunPositionsMonitorUiMode()` in `SnapshotRuntime.cs`

**File:** `src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs`

New method that orchestrates the poller + web server:

```csharp
private async Task RunPositionsMonitorUiMode(
    EClientSocket client,
    IBrokerAdapter brokerAdapter,
    CancellationToken token)
{
    const int webPort = 5100;
    var store = new PositionMonitorStore();

    // Start IBKR poller in background
    var pollerTask = Task.Run(() =>
        IbkrPositionPoller.RunAsync(client, brokerAdapter,
            _options.Account, store, token), token);

    Console.WriteLine($"[OK] Positions Monitor UI: http://localhost:{webPort}");

    // Start Kestrel web server (blocks until cancellation)
    var webTask = PositionsWebServer.RunAsync(store, webPort, token);

    await Task.WhenAny(pollerTask, webTask);
}
```

---

#### Step 3.2 — Add `FrameworkReference` to `.csproj`

**File:** `src/Harvester.App/Harvester.App.csproj`

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

This enables `WebApplication`, `WebSocket`, `Kestrel` — all built into .NET 9, no NuGet download needed.

---

#### Step 3.3 — Embed `index.html` as Resource

**File:** `src/Harvester.App/Harvester.App.csproj`

```xml
<ItemGroup>
  <EmbeddedResource Include="Monitor\wwwroot\index.html" />
</ItemGroup>
```

Or alternatively serve it via `UseStaticFiles()` with a physical path.

---

### Phase 4: CLI & Launch

---

#### Step 4.1 — Usage

```bash
dotnet run --project src/Harvester.App -- \
  --mode positions-monitor-ui \
  --host 127.0.0.1 --port 7497 \
  --client-id 9300 \
  --account DUN559573 \
  --timeout 0 \
  --export-dir exports
```

Then open `http://localhost:5100` in any browser.

---

### File Summary

| # | File | Type | Description |
|---|------|------|-------------|
| 1 | `src/Harvester.App/Monitor/PositionMonitorStore.cs` | NEW | Thread-safe in-memory position + account store |
| 2 | `src/Harvester.App/Monitor/IbkrPositionPoller.cs` | NEW | Background loop polling IBKR `reqAccountUpdates` every 2s |
| 3 | `src/Harvester.App/Monitor/PositionsWebServer.cs` | NEW | Kestrel minimal API + WebSocket server |
| 4 | `src/Harvester.App/Monitor/wwwroot/index.html` | NEW | Single-file HTML/CSS/JS dashboard |
| 5 | `src/Harvester.App/IBKR/Runtime/SnapshotRuntime.cs` | EDIT | Add RunMode + dispatch + orchestration method |
| 6 | `src/Harvester.App/Harvester.App.csproj` | EDIT | Add `FrameworkReference` for ASP.NET Core |

---

### Estimated Effort

| Phase | Steps | Estimated Time |
|-------|-------|----------------|
| Phase 1: Backend | Steps 1.1–1.4 | ~45 min |
| Phase 2: Frontend | Step 2.1 | ~30 min |
| Phase 3: Wiring | Steps 3.1–3.3 | ~15 min |
| Phase 4: Test & Polish | Launch, verify, fix | ~15 min |
| **Total** | | **~2 hours** |

---

### Future Enhancements (Not in v1)

- **Historical P&L chart**: Small sparkline per symbol showing intraday P&L trajectory
- **Order placement from UI**: "Close" button per position that calls `orders-place-sim`
- **Multi-account support**: Dropdown to switch between paper/live accounts
- **Alerts**: Browser notification when unrealized P&L exceeds threshold
- **Position history**: Log each snapshot to SQLite for post-session analysis
