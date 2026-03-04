using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Harvester.App.Monitor;

/// <summary>
/// Kestrel-based Minimal API web server that serves:
///   GET  /                   → index.html dashboard
///   GET  /api/positions      → JSON array of current positions
///   GET  /api/account-summary→ JSON dict of account values
///   WS   /ws/positions       → push position snapshots on every change
/// </summary>
public sealed class PositionsWebServer
{
    private readonly PositionMonitorStore _store;
    private readonly int _port;
    private readonly List<WebSocket> _clients = [];
    private readonly Lock _clientsLock = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public PositionsWebServer(PositionMonitorStore store, int port = 5100)
    {
        _store = store;
        _port = port;
    }

    public async Task RunAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");
        builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);
        var app = builder.Build();

        app.UseWebSockets();

        // Serve index.html
        app.MapGet("/", async (HttpContext ctx) =>
        {
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Monitor", "wwwroot", "index.html");
            if (!File.Exists(htmlPath))
            {
                // Fallback: try relative to the source tree (dev mode)
                htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Monitor", "wwwroot", "index.html");
            }
            if (!File.Exists(htmlPath))
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("index.html not found");
                return;
            }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.SendFileAsync(htmlPath);
        });

        // REST: positions
        app.MapGet("/api/positions", () =>
        {
            var positions = _store.GetAll();
            return Results.Json(positions, JsonOpts);
        });

        // REST: account summary
        app.MapGet("/api/account-summary", () =>
        {
            var summary = _store.GetAccountSummary();
            return Results.Json(summary, JsonOpts);
        });

        // WebSocket endpoint
        app.Map("/ws/positions", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                return;
            }

            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            lock (_clientsLock)
            {
                _clients.Add(ws);
            }
            Console.WriteLine($"[Monitor] WebSocket client connected ({_clients.Count} total)");

            // Send initial snapshot
            await SendSnapshotAsync(ws);

            // Keep connection alive; read loop to detect close
            var buffer = new byte[256];
            try
            {
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                lock (_clientsLock)
                {
                    _clients.Remove(ws);
                }
                Console.WriteLine($"[Monitor] WebSocket client disconnected ({_clients.Count} total)");
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                    catch { /* ignore */ }
                }
            }
        });

        // Subscribe to store changes and broadcast
        _store.OnChanged += () => _ = BroadcastSnapshotAsync();

        Console.WriteLine($"[Monitor] Web server listening on http://localhost:{_port}");

        // Register cancellation to stop Kestrel gracefully
        token.Register(() => app.StopAsync().ConfigureAwait(false));
        await app.RunAsync();
    }

    private async Task BroadcastSnapshotAsync()
    {
        List<WebSocket> snapshot;
        lock (_clientsLock)
        {
            snapshot = [.. _clients];
        }

        foreach (var ws in snapshot)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await SendSnapshotAsync(ws);
                }
                catch
                {
                    // Will be cleaned up on next receive failure
                }
            }
        }
    }

    private async Task SendSnapshotAsync(WebSocket ws)
    {
        var payload = new
        {
            timestamp = DateTime.UtcNow,
            version = _store.Version,
            positions = _store.GetAll(),
            accountSummary = _store.GetAccountSummary()
        };
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
