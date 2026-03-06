using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class V3LiveFeatureBuilder : ILiveFeatureBuilder
{
    // ── Phase 3: Per-symbol indicator cache ──────────────────────────────
    // Bars change every ~60s (1-min bars) but Build() is called every ~1s per symbol.
    // Caching indicator arrays per symbol eliminates ~98% of redundant heavy computations.
    private readonly Dictionary<string, IndicatorCache> _cacheBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public V3LiveFeatureSnapshot Build(StrategyDataSlice dataSlice, int depthLevels, string? symbol = null)
    {
        var l1 = BuildL1Snapshot(dataSlice);
        var l2 = BuildL2Snapshot(dataSlice, depthLevels);

        // Sort source rows and build bars + parallel arrays in a single pass
        // (eliminates 3 intermediate LINQ allocations per tick)
        var sortedRows = dataSlice.HistoricalBars.OrderBy(x => x.TimestampUtc).ToArray();
        var count = sortedRows.Length;

        if (count < 30)
        {
            return new V3LiveFeatureSnapshot(
                TimestampUtc: dataSlice.TimestampUtc,
                L1: l1,
                L2: l2,
                IsReady: false,
                Price: l1.Last > 0 ? l1.Last : (l1.Bid > 0 && l1.Ask > 0 ? (l1.Bid + l1.Ask) / 2.0 : 0.0),
                Atr14: double.NaN,
                Rsi14: double.NaN,
                Vwap: double.NaN,
                DistFromVwapAtr: double.NaN,
                BbPctB: double.NaN,
                KcMid: double.NaN,
                StochK: double.NaN,
                StochD: double.NaN,
                Adx14: double.NaN,
                Rvol: double.NaN,
                VolAccel: double.NaN,
                OfiSignal: l2.OfiSignal,
                SqueezeOn: false,
                BbBandwidth: double.NaN,
                AtrRatio: double.NaN,
                RejectReason: "insufficient-bars");
        }

        // ── Phase 3: Check indicator cache validity per symbol ──────────
        var symbolKey = symbol ?? "_default";
        var lastBarTs = sortedRows[^1].TimestampUtc;
        var cacheHit = _cacheBySymbol.TryGetValue(symbolKey, out var cache)
                       && cache.BarCount == count
                       && cache.LastBarTimestampUtc == lastBarTs;

        if (!cacheHit)
        {
            // Cache miss — recompute all indicators
            var bars = new BacktestBar[count];
            var closes = new double[count];
            var volumes = new double[count];

            for (var i = 0; i < count; i++)
            {
                var r = sortedRows[i];
                bars[i] = new BacktestBar(r.TimestampUtc, r.Open, r.High, r.Low, r.Close, (double)r.Volume);
                closes[i] = r.Close;
                volumes[i] = (double)r.Volume;
            }

            cache = new IndicatorCache
            {
                BarCount = count,
                LastBarTimestampUtc = lastBarTs,
                Bars = bars,
                Volumes = volumes,
                Atr = TechnicalIndicators.Atr(bars, 14),
                Rsi = TechnicalIndicators.Rsi(closes, 14),
                Vwap = TechnicalIndicators.Vwap(bars),
                Bb = TechnicalIndicators.BollingerBands(closes, 20, 2.0),
                Kc = TechnicalIndicators.KeltnerChannels(bars, 20, 14, 1.5),
                Stoch = TechnicalIndicators.Stochastic(bars, 14, 3, 3),
                Adx = TechnicalIndicators.Adx(bars, 14),
                Rvol = TechnicalIndicators.RelativeVolume(volumes, 20)
            };
            _cacheBySymbol[symbolKey] = cache;
        }

        // cache is guaranteed non-null after the block above
        var c = cache!;
        var lastIdx = count - 1;
        var prevIdx = Math.Max(0, lastIdx - 1);

        // Use L1 price for sub-bar precision (updates every tick, not just on bar close)
        var price = l1.Last > 0 ? l1.Last : c.Bars[lastIdx].Close;
        var atr14 = c.Atr[lastIdx];
        var vwapNow = c.Vwap[lastIdx];
        var distFromVwapAtr = (atr14 > 0 && !double.IsNaN(vwapNow)) ? (price - vwapNow) / atr14 : double.NaN;
        var volAccel = c.Volumes[prevIdx] > 0 ? (c.Volumes[lastIdx] - c.Volumes[prevIdx]) / c.Volumes[prevIdx] : 0.0;

        var bbNow = c.Bb[lastIdx];
        var kcNow = c.Kc[lastIdx];
        var squeezeOn =
            !double.IsNaN(bbNow.Upper) && !double.IsNaN(bbNow.Lower) &&
            !double.IsNaN(kcNow.Upper) && !double.IsNaN(kcNow.Lower) &&
            bbNow.Upper < kcNow.Upper && bbNow.Lower > kcNow.Lower;

        // ── Phase 2: Regime-detection features ──────────────────────────
        // BB Bandwidth = (upper − lower) / middle — wider bands → more volatile
        var bbBandwidth = (!double.IsNaN(bbNow.Upper) && !double.IsNaN(bbNow.Lower) && !double.IsNaN(bbNow.Mid) && bbNow.Mid > 0)
            ? (bbNow.Upper - bbNow.Lower) / bbNow.Mid
            : double.NaN;

        // ATR ratio = current ATR / SMA(ATR, 20) — >1 means above-average volatility
        var atrRatio = double.NaN;
        if (!double.IsNaN(atr14) && count >= 34) // need 14 + 20 bars for a meaningful SMA
        {
            var atrSum = 0.0;
            var atrCount = 0;
            var startLookback = Math.Max(0, lastIdx - 19);
            for (var i = startLookback; i <= lastIdx; i++)
            {
                if (!double.IsNaN(c.Atr[i]) && c.Atr[i] > 0)
                {
                    atrSum += c.Atr[i];
                    atrCount++;
                }
            }
            if (atrCount > 0)
                atrRatio = atr14 / (atrSum / atrCount);
        }

        return new V3LiveFeatureSnapshot(
            TimestampUtc: dataSlice.TimestampUtc,
            L1: l1,
            L2: l2,
            IsReady: true,
            Price: price,
            Atr14: atr14,
            Rsi14: c.Rsi[lastIdx],
            Vwap: vwapNow,
            DistFromVwapAtr: distFromVwapAtr,
            BbPctB: bbNow.PctB,
            KcMid: kcNow.Mid,
            StochK: c.Stoch[lastIdx].K,
            StochD: c.Stoch[lastIdx].D,
            Adx14: c.Adx[lastIdx].Adx,
            Rvol: c.Rvol[lastIdx],
            VolAccel: volAccel,
            OfiSignal: l2.OfiSignal,
            SqueezeOn: squeezeOn,
            BbBandwidth: bbBandwidth,
            AtrRatio: atrRatio,
            RejectReason: string.Empty);
    }

    private static V3LiveL1Snapshot BuildL1Snapshot(StrategyDataSlice dataSlice)
    {
        var ticks = dataSlice.TopTicks.OrderByDescending(x => x.TimestampUtc).ToArray();

        var bid = ResolveTopPrice(ticks, 1);
        var ask = ResolveTopPrice(ticks, 2);
        var last = ResolveTopPrice(ticks, 4);

        var bidSize = ResolveTopSize(ticks, 0);
        var askSize = ResolveTopSize(ticks, 3);

        var hasQuote = bid > 0 && ask > 0;
        var mid = hasQuote ? (bid + ask) / 2.0 : 0.0;
        var spreadPct = hasQuote && mid > 0 ? (ask - bid) / mid : 0.0;
        var ts = ticks.FirstOrDefault()?.TimestampUtc ?? dataSlice.TimestampUtc;

        return new V3LiveL1Snapshot(
            TimestampUtc: ts,
            Bid: bid,
            Ask: ask,
            Last: last,
            BidSize: bidSize,
            AskSize: askSize,
            SpreadPct: spreadPct,
            HasQuote: hasQuote);
    }

    private static V3LiveL2Snapshot BuildL2Snapshot(StrategyDataSlice dataSlice, int levels)
    {
        var latest = dataSlice.DepthRows
            .OrderByDescending(x => x.TimestampUtc)
            .Take(500)
            .ToArray();

        var bids = latest
            .Where(x => x.Side == 1)
            .OrderBy(x => x.Position)
            .Take(levels)
            .ToArray();

        var asks = latest
            .Where(x => x.Side == 0)
            .OrderBy(x => x.Position)
            .Take(levels)
            .ToArray();

        var bidDepth = bids.Sum(x => (double)Math.Max(0, x.Size));
        var askDepth = asks.Sum(x => (double)Math.Max(0, x.Size));
        var hasDepth = bids.Length > 0 && asks.Length > 0;
        var imbalance = askDepth > 0 ? bidDepth / askDepth : (bidDepth > 0 ? 10.0 : 0.0);

        var topBidSize = bids.FirstOrDefault()?.Size ?? 0;
        var topAskSize = asks.FirstOrDefault()?.Size ?? 0;
        var topSum = Math.Max(1.0, topBidSize + topAskSize);
        var ofiSignal = (topBidSize - topAskSize) / topSum;

        return new V3LiveL2Snapshot(
            BidDepthN: bidDepth,
            AskDepthN: askDepth,
            ImbalanceRatio: imbalance,
            OfiSignal: ofiSignal,
            HasDepth: hasDepth);
    }

    private static double ResolveTopPrice(IReadOnlyList<TopTickRow> ticks, int field)
    {
        var row = ticks.FirstOrDefault(x => x.Field == field && x.Price > 0);
        return row?.Price ?? 0.0;
    }

    private static double ResolveTopSize(IReadOnlyList<TopTickRow> ticks, int field)
    {
        var row = ticks.FirstOrDefault(x => x.Field == field && x.Size > 0);
        return row?.Size ?? 0.0;
    }

    /// <summary>Reset all cached indicator state (call on session/day reset).</summary>
    public void ResetCache() => _cacheBySymbol.Clear();

    /// <summary>Per-symbol cached indicator arrays. Invalidated when bar count or last bar timestamp changes.</summary>
    private sealed class IndicatorCache
    {
        public int BarCount { get; init; }
        public DateTime LastBarTimestampUtc { get; init; }
        public BacktestBar[] Bars { get; init; } = [];
        public double[] Volumes { get; init; } = [];
        public double[] Atr { get; init; } = [];
        public double[] Rsi { get; init; } = [];
        public double[] Vwap { get; init; } = [];
        public BollingerResult[] Bb { get; init; } = [];
        public KeltnerResult[] Kc { get; init; } = [];
        public StochasticResult[] Stoch { get; init; } = [];
        public AdxResult[] Adx { get; init; } = [];
        public double[] Rvol { get; init; } = [];
    }
}

public sealed record V3LiveL1Snapshot(
    DateTime TimestampUtc,
    double Bid,
    double Ask,
    double Last,
    double BidSize,
    double AskSize,
    double SpreadPct,
    bool HasQuote);

public sealed record V3LiveL2Snapshot(
    double BidDepthN,
    double AskDepthN,
    double ImbalanceRatio,
    double OfiSignal,
    bool HasDepth);

public sealed record V3LiveFeatureSnapshot(
    DateTime TimestampUtc,
    V3LiveL1Snapshot L1,
    V3LiveL2Snapshot L2,
    bool IsReady,
    double Price,
    double Atr14,
    double Rsi14,
    double Vwap,
    double DistFromVwapAtr,
    double BbPctB,
    double KcMid,
    double StochK,
    double StochD,
    double Adx14,
    double Rvol,
    double VolAccel,
    double OfiSignal,
    bool SqueezeOn,
    double BbBandwidth,
    double AtrRatio,
    string RejectReason) : IFeatureSnapshot;
