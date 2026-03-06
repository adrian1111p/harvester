namespace Harvester.App.Strategy;

public sealed record V3LiveConfig
{
    public string[] Symbols { get; init; } = ["NVDA", "META", "AMD", "AAPL"];
    public bool RequireL2Depth { get; init; } = true;
    public int DepthLevels { get; init; } = 5;
    public bool EmitOrderIntents { get; init; } = true;
    public bool UseScannerSelectionV2Gate { get; init; } = true;
    public double ScannerMinCompositeScore { get; init; } = 55.0;
    public bool RequireMtfConfirmation { get; init; } = true;
    public bool AllowMtfUnready { get; init; } = true;

    public double RiskPerTradeDollars { get; init; } = 22.0;
    public double MaxDailyLossDollars { get; init; } = 300.0;
    public double MaxOpenRiskDollars { get; init; } = 150.0;
    public double AccountSize { get; init; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.22;
    public int MaxShares { get; init; } = 8_000;
    public double MinRiskPerShare { get; init; } = 0.01;
    public double MaxSlippageBps { get; init; } = 15.0;

    public double MinPrice { get; init; } = 5.0;
    public double MaxPrice { get; init; } = 600.0;

    public double MaxSpreadPct { get; init; } = 0.015;
    public int MaxQuoteStalenessSeconds { get; init; } = 2;
    public double MinTopQuoteSize { get; init; } = 100.0;

    public double MinDepthPerSideShares { get; init; } = 1500.0;
    public double MinImbalanceLong { get; init; } = 1.10;
    public double MaxImbalanceShort { get; init; } = 0.90;
    public double RvolMin { get; init; } = 0.85;
    public double L2LiquidityMin { get; init; } = 20.0;
    public double VolAccelMin { get; init; } = -0.20;
    public double AdxMin { get; init; } = 12.0;
    public double AdxMax { get; init; } = 36.0;
    public int MinScore { get; init; } = 5;
    public bool AllowLong { get; init; } = true;
    public bool AllowShort { get; init; } = true;

    public int CooldownSeconds { get; init; } = 240;
    public int MaxEntriesPerSymbolPerDay { get; init; } = 3;

    public double VwapStretchAtr { get; init; } = 1.2;
    public double VwapDeviationAtr { get; init; } = 0.60;
    public double BbEntryPctbLow { get; init; } = 0.12;
    public double BbEntryPctbHigh { get; init; } = 0.88;
    public double RsiOversold { get; init; } = 38.0;
    public double RsiOverbought { get; init; } = 62.0;

    public double HardStopR { get; init; } = 0.90;
    public double BreakevenR { get; init; } = 0.55;
    public double TrailR { get; init; } = 0.35;
    public double GivebackPct { get; init; } = 0.30;
    public bool UseFixedGivebackUsdCap { get; init; } = true;
    public bool UseVariableGivebackUsdCap { get; init; } = true;
    public double GivebackUsdCap { get; init; } = 30.0;
    public double Tp1R { get; init; } = 0.8;
    public double Tp2R { get; init; } = 1.45;
    public int MaxHoldBars { get; init; } = 30;

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

        static bool ReadBoolAny(string[] keys, bool fallback)
        {
            foreach (var key in keys)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (bool.TryParse(value, out var parsed))
                    return parsed;
            }
            return fallback;
        }

        static int ReadIntAny(string[] keys, int fallback)
        {
            foreach (var key in keys)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (int.TryParse(value, out var parsed))
                    return parsed;
            }
            return fallback;
        }

        static double ReadDoubleAny(string[] keys, double fallback)
        {
            foreach (var key in keys)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (double.TryParse(value, out var parsed))
                    return parsed;
            }
            return fallback;
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
            UseScannerSelectionV2Gate = ReadBool("V3LIVE_USE_SCANNER_V2_GATE", true),
            ScannerMinCompositeScore = Math.Clamp(ReadDouble("V3LIVE_SCANNER_MIN_SCORE", 55.0), 0.0, 100.0),
            RequireMtfConfirmation = ReadBool("V3LIVE_REQUIRE_MTF_CONFIRMATION", true),
            AllowMtfUnready = ReadBool("V3LIVE_ALLOW_MTF_UNREADY", true),
            RiskPerTradeDollars = Math.Max(1.0, ReadDoubleAny(["V11LIVE_RISK_PER_TRADE", "V3LIVE_RISK_PER_TRADE"], 22.0)),
            MaxDailyLossDollars = Math.Max(1.0, ReadDouble("V3LIVE_MAX_DAILY_LOSS", 300.0)),
            MaxOpenRiskDollars = Math.Max(1.0, ReadDouble("V3LIVE_MAX_OPEN_RISK", 150.0)),
            AccountSize = Math.Max(1.0, ReadDoubleAny(["V11LIVE_ACCOUNT_SIZE", "V3LIVE_ACCOUNT_SIZE"], 25_000.0)),
            MaxPositionNotionalPctOfAccount = Math.Clamp(ReadDoubleAny(["V11LIVE_MAX_POSITION_NOTIONAL_PCT", "V3LIVE_MAX_POSITION_NOTIONAL_PCT"], 0.22), 0.01, 1.0),
            MaxShares = Math.Max(1, ReadIntAny(["V11LIVE_MAX_SHARES", "V3LIVE_MAX_SHARES"], 8_000)),
            MinRiskPerShare = Math.Max(0.0001, ReadDouble("V3LIVE_MIN_RISK_PER_SHARE", 0.01)),
            MaxSlippageBps = Math.Max(0.1, ReadDouble("V3LIVE_MAX_SLIPPAGE_BPS", 15.0)),
            MinPrice = Math.Max(0.01, ReadDoubleAny(["V11LIVE_MIN_PRICE", "V3LIVE_MIN_PRICE"], 5.0)),
            MaxPrice = Math.Max(0.02, ReadDoubleAny(["V11LIVE_MAX_PRICE", "V3LIVE_MAX_PRICE"], 600.0)),
            MaxSpreadPct = Math.Max(0.0001, ReadDouble("V3LIVE_MAX_SPREAD_PCT", 0.015)),
            MaxQuoteStalenessSeconds = Math.Max(1, ReadInt("V3LIVE_MAX_QUOTE_STALENESS_SECONDS", 2)),
            MinTopQuoteSize = Math.Max(1.0, ReadDouble("V3LIVE_MIN_TOP_QUOTE_SIZE", 100.0)),
            MinDepthPerSideShares = Math.Max(1.0, ReadDouble("V3LIVE_MIN_DEPTH_PER_SIDE", 1500.0)),
            MinImbalanceLong = Math.Max(0.01, ReadDouble("V3LIVE_MIN_IMBALANCE_LONG", 1.10)),
            MaxImbalanceShort = Math.Max(0.01, ReadDouble("V3LIVE_MAX_IMBALANCE_SHORT", 0.90)),
            RvolMin = Math.Max(0.1, ReadDoubleAny(["V11LIVE_RVOL_MIN", "V3LIVE_RVOL_MIN"], 0.85)),
            L2LiquidityMin = Math.Max(0.1, ReadDoubleAny(["V11LIVE_L2_LIQUIDITY_MIN", "V3LIVE_L2_LIQUIDITY_MIN"], 20.0)),
            VolAccelMin = ReadDoubleAny(["V11LIVE_VOL_ACCEL_MIN", "V3LIVE_VOL_ACCEL_MIN"], -0.20),
            AdxMin = Math.Max(0.0, ReadDoubleAny(["V11LIVE_ADX_MIN", "V3LIVE_ADX_MIN"], 12.0)),
            AdxMax = Math.Max(0.0, ReadDoubleAny(["V11LIVE_ADX_MAX", "V3LIVE_ADX_MAX"], 36.0)),
            MinScore = Math.Max(1, ReadIntAny(["V11LIVE_MIN_SCORE", "V3LIVE_MIN_SCORE"], 5)),
            AllowLong = ReadBoolAny(["V11LIVE_ALLOW_LONG", "V3LIVE_ALLOW_LONG"], true),
            AllowShort = ReadBoolAny(["V11LIVE_ALLOW_SHORT", "V3LIVE_ALLOW_SHORT"], true),
            CooldownSeconds = Math.Max(1, ReadIntAny(["V11LIVE_COOLDOWN_SECONDS", "V3LIVE_COOLDOWN_SECONDS"], 240)),
            MaxEntriesPerSymbolPerDay = Math.Max(1, ReadInt("V3LIVE_MAX_ENTRIES_PER_DAY", 3)),
            VwapStretchAtr = Math.Max(0.1, ReadDoubleAny(["V11LIVE_VWAP_STRETCH_ATR", "V3LIVE_VWAP_STRETCH_ATR"], 1.2)),
            VwapDeviationAtr = Math.Max(0.1, ReadDoubleAny(["V11LIVE_VWAP_DEVIATION_ATR", "V3LIVE_VWAP_DEVIATION_ATR"], 0.60)),
            BbEntryPctbLow = ReadDoubleAny(["V11LIVE_BB_PCTB_LOW", "V3LIVE_BB_PCTB_LOW"], 0.12),
            BbEntryPctbHigh = ReadDoubleAny(["V11LIVE_BB_PCTB_HIGH", "V3LIVE_BB_PCTB_HIGH"], 0.88),
            RsiOversold = ReadDoubleAny(["V11LIVE_RSI_LONG_MAX", "V3LIVE_RSI_OVERSOLD"], 38.0),
            RsiOverbought = ReadDoubleAny(["V11LIVE_RSI_SHORT_MIN", "V3LIVE_RSI_OVERBOUGHT"], 62.0),
            HardStopR = Math.Max(0.1, ReadDoubleAny(["V11LIVE_HARD_STOP_R", "V3LIVE_HARD_STOP_R"], 0.90)),
            BreakevenR = Math.Max(0.0, ReadDoubleAny(["V11LIVE_BREAKEVEN_R", "V3LIVE_BREAKEVEN_R"], 0.55)),
            TrailR = Math.Max(0.0, ReadDoubleAny(["V11LIVE_TRAIL_R", "V3LIVE_TRAIL_R"], 0.35)),
            GivebackPct = Math.Clamp(ReadDouble("V3LIVE_GIVEBACK_PCT", 0.30), 0.01, 1.0),
            UseFixedGivebackUsdCap = ReadBoolAny(["V11LIVE_USE_FIXED_GIVEBACK_USD_CAP", "V3LIVE_USE_FIXED_GIVEBACK_USD_CAP"], true),
            UseVariableGivebackUsdCap = ReadBoolAny(["V11LIVE_USE_VARIABLE_GIVEBACK_USD_CAP", "V3LIVE_USE_VARIABLE_GIVEBACK_USD_CAP"], true),
            GivebackUsdCap = Math.Max(0.01, ReadDoubleAny(["V11LIVE_GIVEBACK_USD_CAP", "V3LIVE_GIVEBACK_USD_CAP"], 30.0)),
            Tp1R = Math.Max(0.1, ReadDoubleAny(["V11LIVE_TP1_R", "V3LIVE_TP1_R"], 0.8)),
            Tp2R = Math.Max(0.2, ReadDoubleAny(["V11LIVE_TP2_R", "V3LIVE_TP2_R"], 1.45)),
            MaxHoldBars = Math.Max(1, ReadIntAny(["V11LIVE_MAX_HOLD_BARS", "V3LIVE_MAX_HOLD_BARS"], 30)),
            SessionStartUtc = Read("V3LIVE_SESSION_START_UTC", "13:35"),
            SessionEndUtc = Read("V3LIVE_SESSION_END_UTC", "20:00")
        };
    }
}
