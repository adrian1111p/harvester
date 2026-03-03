namespace Harvester.App.Strategy;

public sealed record V3LiveConfig
{
    public string[] Symbols { get; init; } = ["NVDA", "META", "AMD", "AAPL"];
    public bool RequireL2Depth { get; init; } = true;
    public int DepthLevels { get; init; } = 5;
    public bool EmitOrderIntents { get; init; } = true;

    public double RiskPerTradeDollars { get; init; } = 30.0;
    public double MaxDailyLossDollars { get; init; } = 300.0;
    public double MaxOpenRiskDollars { get; init; } = 150.0;
    public double AccountSize { get; init; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.25;
    public int MaxShares { get; init; } = 10_000;
    public double MinRiskPerShare { get; init; } = 0.01;
    public double MaxSlippageBps { get; init; } = 15.0;

    public double MaxSpreadPct { get; init; } = 0.015;
    public int MaxQuoteStalenessSeconds { get; init; } = 2;
    public double MinTopQuoteSize { get; init; } = 100.0;

    public double MinDepthPerSideShares { get; init; } = 1500.0;
    public double MinImbalanceLong { get; init; } = 1.10;
    public double MaxImbalanceShort { get; init; } = 0.90;

    public int CooldownSeconds { get; init; } = 20;
    public int MaxEntriesPerSymbolPerDay { get; init; } = 3;

    public double VwapStretchAtr { get; init; } = 1.5;
    public double BbEntryPctbLow { get; init; } = 0.12;
    public double BbEntryPctbHigh { get; init; } = 0.88;
    public double RsiOversold { get; init; } = 35.0;
    public double RsiOverbought { get; init; } = 65.0;

    public double HardStopR { get; init; } = 1.0;
    public double BreakevenR { get; init; } = 0.5;
    public double TrailR { get; init; } = 0.4;
    public double GivebackPct { get; init; } = 0.30;
    public double Tp1R { get; init; } = 0.9;
    public double Tp2R { get; init; } = 1.8;
    public int MaxHoldBars { get; init; } = 45;

    public string SessionStartUtc { get; init; } = "13:35";
    public string SessionEndUtc { get; init; } = "20:00";

    public static V3LiveConfig FromEnvironment()
    {
        static string Read(string key, string fallback) =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))
                ? fallback
                : Environment.GetEnvironmentVariable(key)!;

        static bool ReadBool(string key, bool fallback)
        {
            var value = Environment.GetEnvironmentVariable(key);
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        static int ReadInt(string key, int fallback)
        {
            var value = Environment.GetEnvironmentVariable(key);
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        static double ReadDouble(string key, double fallback)
        {
            var value = Environment.GetEnvironmentVariable(key);
            return double.TryParse(value, out var parsed) ? parsed : fallback;
        }

        var symbolsRaw = Read("V3LIVE_SYMBOLS", "NVDA,META,AMD,AAPL");
        var symbols = symbolsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToArray();

        if (symbols.Length == 0)
        {
            symbols = ["NVDA", "META", "AMD", "AAPL"];
        }

        return new V3LiveConfig
        {
            Symbols = symbols,
            RequireL2Depth = ReadBool("V3LIVE_REQUIRE_L2", true),
            DepthLevels = Math.Max(1, ReadInt("V3LIVE_DEPTH_LEVELS", 5)),
            EmitOrderIntents = ReadBool("V3LIVE_EMIT_ORDER_INTENTS", true),
            RiskPerTradeDollars = Math.Max(1.0, ReadDouble("V3LIVE_RISK_PER_TRADE", 30.0)),
            MaxDailyLossDollars = Math.Max(1.0, ReadDouble("V3LIVE_MAX_DAILY_LOSS", 300.0)),
            MaxOpenRiskDollars = Math.Max(1.0, ReadDouble("V3LIVE_MAX_OPEN_RISK", 150.0)),
            AccountSize = Math.Max(1.0, ReadDouble("V3LIVE_ACCOUNT_SIZE", 25_000.0)),
            MaxPositionNotionalPctOfAccount = Math.Clamp(ReadDouble("V3LIVE_MAX_POSITION_NOTIONAL_PCT", 0.25), 0.01, 1.0),
            MaxShares = Math.Max(1, ReadInt("V3LIVE_MAX_SHARES", 10_000)),
            MinRiskPerShare = Math.Max(0.0001, ReadDouble("V3LIVE_MIN_RISK_PER_SHARE", 0.01)),
            MaxSlippageBps = Math.Max(0.1, ReadDouble("V3LIVE_MAX_SLIPPAGE_BPS", 15.0)),
            MaxSpreadPct = Math.Max(0.0001, ReadDouble("V3LIVE_MAX_SPREAD_PCT", 0.015)),
            MaxQuoteStalenessSeconds = Math.Max(1, ReadInt("V3LIVE_MAX_QUOTE_STALENESS_SECONDS", 2)),
            MinTopQuoteSize = Math.Max(1.0, ReadDouble("V3LIVE_MIN_TOP_QUOTE_SIZE", 100.0)),
            MinDepthPerSideShares = Math.Max(1.0, ReadDouble("V3LIVE_MIN_DEPTH_PER_SIDE", 1500.0)),
            MinImbalanceLong = Math.Max(0.01, ReadDouble("V3LIVE_MIN_IMBALANCE_LONG", 1.10)),
            MaxImbalanceShort = Math.Max(0.01, ReadDouble("V3LIVE_MAX_IMBALANCE_SHORT", 0.90)),
            CooldownSeconds = Math.Max(1, ReadInt("V3LIVE_COOLDOWN_SECONDS", 20)),
            MaxEntriesPerSymbolPerDay = Math.Max(1, ReadInt("V3LIVE_MAX_ENTRIES_PER_DAY", 3)),
            VwapStretchAtr = Math.Max(0.1, ReadDouble("V3LIVE_VWAP_STRETCH_ATR", 1.5)),
            BbEntryPctbLow = ReadDouble("V3LIVE_BB_PCTB_LOW", 0.12),
            BbEntryPctbHigh = ReadDouble("V3LIVE_BB_PCTB_HIGH", 0.88),
            RsiOversold = ReadDouble("V3LIVE_RSI_OVERSOLD", 35.0),
            RsiOverbought = ReadDouble("V3LIVE_RSI_OVERBOUGHT", 65.0),
            HardStopR = Math.Max(0.1, ReadDouble("V3LIVE_HARD_STOP_R", 1.0)),
            BreakevenR = Math.Max(0.0, ReadDouble("V3LIVE_BREAKEVEN_R", 0.5)),
            TrailR = Math.Max(0.0, ReadDouble("V3LIVE_TRAIL_R", 0.4)),
            GivebackPct = Math.Clamp(ReadDouble("V3LIVE_GIVEBACK_PCT", 0.30), 0.01, 1.0),
            Tp1R = Math.Max(0.1, ReadDouble("V3LIVE_TP1_R", 0.9)),
            Tp2R = Math.Max(0.2, ReadDouble("V3LIVE_TP2_R", 1.8)),
            MaxHoldBars = Math.Max(1, ReadInt("V3LIVE_MAX_HOLD_BARS", 45)),
            SessionStartUtc = Read("V3LIVE_SESSION_START_UTC", "13:35"),
            SessionEndUtc = Read("V3LIVE_SESSION_END_UTC", "20:00")
        };
    }
}
