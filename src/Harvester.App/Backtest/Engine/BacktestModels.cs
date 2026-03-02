namespace Harvester.App.Backtest.Engine;

// ── Enums ────────────────────────────────────────────────────────────────────

public enum TradeSide
{
    Long,
    Short,
}

public enum ExitReason
{
    HardStop,
    BreakEven,
    Trailing,
    Tp1,
    Tp2,
    Tp3,
    TimeStop,
    Eod,
    SignalReversal,
    MicroTrail,
    ReversalFlatten,
    Giveback,
    EmaTrail,
}

public enum HtfBias
{
    Bull,
    Bear,
    Neutral,
}

// ── Bar Data ─────────────────────────────────────────────────────────────────

/// <summary>OHLCV bar — replaces a single row of a pandas DataFrame.</summary>
public sealed record BacktestBar(
    DateTime Timestamp,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume
);

// ── Signal ───────────────────────────────────────────────────────────────────

/// <summary>Entry signal produced by a strategy's signal generator.</summary>
public sealed record BacktestSignal(
    int BarIndex,
    DateTime Timestamp,
    TradeSide Side,
    double EntryPrice,
    double StopPrice,
    double RiskPerShare,
    int PositionSize,
    double AtrValue,
    HtfBias HtfTrend,
    string MtfMomentum,
    string SubStrategy = ""
);

// ── Trade Result ─────────────────────────────────────────────────────────────

/// <summary>Outcome of a completed simulated trade.</summary>
public sealed record BacktestTradeResult(
    int EntryBar,
    DateTime EntryTime,
    int ExitBar,
    DateTime ExitTime,
    TradeSide Side,
    double EntryPrice,
    double ExitPrice,
    double StopPrice,
    int PositionSize,
    double Pnl,
    double PnlR,
    ExitReason ExitReason,
    double PeakR,
    int BarsHeld
);

// ── Statistics ───────────────────────────────────────────────────────────────

/// <summary>Performance statistics computed from a list of trades.</summary>
public sealed record BacktestStatistics(
    int TotalTrades,
    int Winners,
    int Losers,
    double WinRate,
    double AvgWin,
    double AvgLoss,
    double ProfitFactor,
    double ExpectancyR,
    double TotalPnl,
    double MaxDrawdown,
    double MaxDrawdownPct,
    double Sharpe,
    double AvgBarsHeld,
    int LongTrades,
    int ShortTrades,
    double LongWinRate,
    double ShortWinRate,
    IReadOnlyDictionary<ExitReason, int> ExitReasons
);

// ── Backtest Result ─────────────────────────────────────────────────────────

/// <summary>Complete result of a single backtest run.</summary>
public sealed record BacktestResult(
    string Symbol,
    string TriggerTf,
    IReadOnlyList<BacktestTradeResult> Trades,
    IReadOnlyList<(DateTime Time, double Equity)> EquityCurve,
    BacktestStatistics Stats
)
{
    public string SummaryTable()
    {
        var lines = new List<string>
        {
            $"{"Metric",-25} {"Value",15}",
            new string('-', 42),
            $"{"Symbol",-25} {Symbol,15}",
            $"{"Trigger TF",-25} {TriggerTf,15}",
            $"{"Total Trades",-25} {Stats.TotalTrades,15}",
            $"{"Winners",-25} {Stats.Winners,15}",
            $"{"Losers",-25} {Stats.Losers,15}",
            $"{"Win Rate",-25} {Stats.WinRate,14:P1}",
            $"{"Avg Win ($)",-25} {Stats.AvgWin,15:F2}",
            $"{"Avg Loss ($)",-25} {Stats.AvgLoss,15:F2}",
            $"{"Profit Factor",-25} {Stats.ProfitFactor,15:F2}",
            $"{"Expectancy (R)",-25} {Stats.ExpectancyR,14:F2}R",
            $"{"Total PnL ($)",-25} {Stats.TotalPnl,15:F2}",
            $"{"Max Drawdown ($)",-25} {Stats.MaxDrawdown,15:F2}",
            $"{"Max Drawdown (%)",-25} {Stats.MaxDrawdownPct,14:P1}",
            $"{"Sharpe Ratio",-25} {Stats.Sharpe,15:F2}",
            $"{"Avg Bars Held",-25} {Stats.AvgBarsHeld,15:F0}",
            $"{"Long Trades",-25} {Stats.LongTrades,15}",
            $"{"Short Trades",-25} {Stats.ShortTrades,15}",
            $"{"Long Win Rate",-25} {Stats.LongWinRate,14:P1}",
            $"{"Short Win Rate",-25} {Stats.ShortWinRate,14:P1}",
        };

        foreach (var (reason, count) in Stats.ExitReasons.OrderBy(x => x.Key.ToString()))
        {
            lines.Add($"{"  Exit: " + reason,-25} {count,15}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string TradesTable(int n = 20)
    {
        if (Trades.Count == 0) return "No trades.";

        var header = $"{"Entry Time",-18} {"Side",-6} {"Entry$",8} {"Exit$",8} {"PnL$",10} {"PnL(R)",8} {"Exit Reason",-18} {"Bars",5}";
        var lines = new List<string> { header, new string('-', header.Length) };

        foreach (var t in Trades.TakeLast(n))
        {
            lines.Add($"{t.EntryTime:yyyy-MM-dd HH:mm} {t.Side,-6} {t.EntryPrice,8:F2} {t.ExitPrice,8:F2} {t.Pnl,10:F2} {t.PnlR,7:F2}R {t.ExitReason,-18} {t.BarsHeld,5}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
