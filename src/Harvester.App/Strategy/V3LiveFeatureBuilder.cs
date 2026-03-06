using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class V3LiveFeatureBuilder
{
    public V3LiveFeatureSnapshot Build(StrategyDataSlice dataSlice, int depthLevels)
    {
        var l1 = BuildL1Snapshot(dataSlice);
        var l2 = BuildL2Snapshot(dataSlice, depthLevels);

        var bars = dataSlice.HistoricalBars
            .OrderBy(x => x.TimestampUtc)
            .Select(x => new BacktestBar(
                Timestamp: x.TimestampUtc,
                Open: x.Open,
                High: x.High,
                Low: x.Low,
                Close: x.Close,
                Volume: (double)x.Volume))
            .ToArray();

        if (bars.Length < 30)
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
                RejectReason: "insufficient-bars");
        }

        var closes = bars.Select(x => x.Close).ToArray();
        var volumes = bars.Select(x => x.Volume).ToArray();

        var atr = TechnicalIndicators.Atr(bars, 14);
        var rsi = TechnicalIndicators.Rsi(closes, 14);
        var vwap = TechnicalIndicators.Vwap(bars);
        var bb = TechnicalIndicators.BollingerBands(closes, 20, 2.0);
        var kc = TechnicalIndicators.KeltnerChannels(bars, 20, 14, 1.5);
        var stoch = TechnicalIndicators.Stochastic(bars, 14, 3, 3);
        var adx = TechnicalIndicators.Adx(bars, 14);
        var rvol = TechnicalIndicators.RelativeVolume(volumes, 20);

        var lastIdx = bars.Length - 1;
        var prevIdx = Math.Max(0, lastIdx - 1);

        var price = bars[lastIdx].Close;
        var atr14 = atr[lastIdx];
        var vwapNow = vwap[lastIdx];
        var distFromVwapAtr = (atr14 > 0 && !double.IsNaN(vwapNow)) ? (price - vwapNow) / atr14 : double.NaN;
        var volAccel = volumes[prevIdx] > 0 ? (volumes[lastIdx] - volumes[prevIdx]) / volumes[prevIdx] : 0.0;

        var bbNow = bb[lastIdx];
        var kcNow = kc[lastIdx];
        var squeezeOn =
            !double.IsNaN(bbNow.Upper) && !double.IsNaN(bbNow.Lower) &&
            !double.IsNaN(kcNow.Upper) && !double.IsNaN(kcNow.Lower) &&
            bbNow.Upper < kcNow.Upper && bbNow.Lower > kcNow.Lower;

        return new V3LiveFeatureSnapshot(
            TimestampUtc: dataSlice.TimestampUtc,
            L1: l1,
            L2: l2,
            IsReady: true,
            Price: price,
            Atr14: atr14,
            Rsi14: rsi[lastIdx],
            Vwap: vwapNow,
            DistFromVwapAtr: distFromVwapAtr,
            BbPctB: bbNow.PctB,
            KcMid: kcNow.Mid,
            StochK: stoch[lastIdx].K,
            StochD: stoch[lastIdx].D,
            Adx14: adx[lastIdx].Adx,
            Rvol: rvol[lastIdx],
            VolAccel: volAccel,
            OfiSignal: l2.OfiSignal,
            SqueezeOn: squeezeOn,
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
    string RejectReason);
