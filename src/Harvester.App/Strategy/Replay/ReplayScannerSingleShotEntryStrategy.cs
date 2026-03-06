using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class ReplayScannerSingleShotEntryStrategy : IReplayEntryStrategy
{
    private const int SetupMinBars = 25;
    private const int SetupLookbackBars = 30;
    private const int SetupPullbackBars = 3;
    private const int SetupSmaLength = 20;
    private const int EnhancedSignalThreshold = 4;

    private sealed record SetupBar(DateTime TimestampUtc, double Open, double High, double Low, double Close, double Volume);

    private readonly HashSet<string> _submittedSymbols;
    private readonly Dictionary<string, List<SetupBar>> _barsBySymbol;
    private readonly double _orderQuantity;
    private readonly string _orderSide;
    private readonly string _orderType;
    private readonly string _timeInForce;
    private readonly double _limitOffsetBps;
    private readonly IReplayMtfSignalSource? _mtfSignalSource;
    private readonly bool _requireMtfAlignment;
    private readonly bool _requireBuySetupConfirmation;
    private readonly bool _requireEnhancedBuySetupConfirmation;
    private readonly bool _requireSellSetupConfirmation;
    private readonly bool _requireBreakoutConfirmation;
    private readonly bool _requireOneTwoThreeConfirmation;

    private sealed record BuySetupSignalState(
        bool BaseReady,
        bool Criteria4Ready,
        bool EntryBarReady,
        bool CopBarReady,
        bool PullbackQualityReady,
        bool RewardRiskReady,
        bool MultipleEntryBarsReady,
        bool VolumeSpikeReady,
        bool MinorSupportRetestReady,
        bool TrendlineRetestReady,
        bool PracticePatternReady,
        bool BreakoutReady,
        bool OneTwoThreeReady,
        double RewardRiskRatio,
        int EnhancedScore
    );

    private sealed record SellSetupSignalState(
        bool BaseReady,
        bool BreakdownReady,
        bool PullupQualityReady,
        bool RewardRiskReady,
        int EnhancedScore
    );

    public ReplayScannerSingleShotEntryStrategy(
        double orderQuantity,
        string orderSide,
        string orderType,
        string timeInForce,
        double limitOffsetBps,
        IReplayMtfSignalSource? mtfSignalSource = null,
        bool requireMtfAlignment = false,
        bool requireBuySetupConfirmation = false,
        bool requireEnhancedBuySetupConfirmation = false,
        bool requireSellSetupConfirmation = false,
        bool requireBreakoutConfirmation = false,
        bool requireOneTwoThreeConfirmation = false)
    {
        _submittedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _barsBySymbol = new Dictionary<string, List<SetupBar>>(StringComparer.OrdinalIgnoreCase);
        _orderQuantity = Math.Max(0, orderQuantity);
        _orderSide = NormalizeOrderSide(orderSide);
        _orderType = NormalizeOrderType(orderType);
        _timeInForce = NormalizeTimeInForce(timeInForce);
        _limitOffsetBps = Math.Max(0, limitOffsetBps);
        _mtfSignalSource = mtfSignalSource;
        _requireMtfAlignment = requireMtfAlignment;
        _requireBuySetupConfirmation = requireBuySetupConfirmation;
        _requireEnhancedBuySetupConfirmation = requireEnhancedBuySetupConfirmation;
        _requireSellSetupConfirmation = requireSellSetupConfirmation;
        _requireBreakoutConfirmation = requireBreakoutConfirmation;
        _requireOneTwoThreeConfirmation = requireOneTwoThreeConfirmation;
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context, ReplayScannerSymbolSelectionSnapshotRow selection)
    {
        UpdateBars(context);

        if (_orderQuantity <= 0)
        {
            return [];
        }

        var symbol = context.Symbol.Trim().ToUpperInvariant();
        if (!selection.SelectedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        if (_submittedSymbols.Contains(symbol))
        {
            return [];
        }

        var side = ResolveOrderSide(context.PositionQuantity);
        if (string.IsNullOrWhiteSpace(side))
        {
            _submittedSymbols.Add(symbol);
            return [];
        }

        var buySetupState = string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? AnalyzeBuySetupSignals(symbol)
            : null;
        var sellSetupState = string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase)
            ? AnalyzeSellSetupSignals(symbol)
            : null;

        var buySetupReady = buySetupState?.BaseReady ?? false;
        var buySetupEnhancedReady = (buySetupState?.EnhancedScore ?? 0) >= EnhancedSignalThreshold;
        var sellSetupReady = sellSetupState?.BaseReady ?? false;
        var breakoutReady = buySetupState?.BreakoutReady ?? false;
        var oneTwoThreeReady = buySetupState?.OneTwoThreeReady ?? false;

        if (string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
            && _requireBuySetupConfirmation
            && !buySetupReady)
        {
            return [];
        }

        if (string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
            && _requireEnhancedBuySetupConfirmation
            && !buySetupEnhancedReady)
        {
            return [];
        }

        if (string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase)
            && _requireSellSetupConfirmation
            && !sellSetupReady)
        {
            return [];
        }

        if (string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
            && _requireBreakoutConfirmation
            && !breakoutReady)
        {
            return [];
        }

        if (string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
            && _requireOneTwoThreeConfirmation
            && !oneTwoThreeReady)
        {
            return [];
        }

        if (_requireMtfAlignment && _mtfSignalSource is not null)
        {
            if (!_mtfSignalSource.TryGetSnapshot(symbol, out var mtfSnapshot)
                || !mtfSnapshot.HasAllTimeframes)
            {
                return [];
            }

            if (string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
                && !mtfSnapshot.BullishEntryReady)
            {
                return [];
            }

            if (string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase)
                && !mtfSnapshot.BearishEntryReady)
            {
                return [];
            }
        }

        double? limitPrice = null;
        if (string.Equals(_orderType, "LMT", StringComparison.OrdinalIgnoreCase))
        {
            var referenceBid = context.BidPrice > 0 ? context.BidPrice : context.MarkPrice;
            var referenceAsk = context.AskPrice > 0 ? context.AskPrice : context.MarkPrice;
            var reference = string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
                ? referenceBid
                : referenceAsk;
            if (reference <= 0)
            {
                return [];
            }

            var offset = reference * (_limitOffsetBps / 10000.0);
            limitPrice = string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0.0001, reference - offset)
                : reference + offset;
        }

        _submittedSymbols.Add(symbol);

        return
        [
            new ReplayOrderIntent(
                context.TimestampUtc,
                symbol,
                side,
                _orderQuantity,
                _orderType,
                limitPrice,
                null,
                null,
                null,
                _timeInForce,
                null,
                BuildEntrySourceTag(side, buySetupState, sellSetupState))
        ];
    }

    private string BuildEntrySourceTag(string side, BuySetupSignalState? buySetupState, SellSetupSignalState? sellSetupState)
    {
        var source = _mtfSignalSource is null
            ? "entry:scanner-candidate"
            : "entry:scanner-candidate:mtf-aligned";

        if (string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase))
        {
            var sellReady = sellSetupState?.BaseReady ?? false;
            source += sellReady ? ":sell-setup-confirmed" : ":sell-setup-signal";
            if (_requireSellSetupConfirmation && !sellReady)
            {
                source += ":sell-setup-required";
            }

            if (sellSetupState is not null)
            {
                source += sellSetupState.BreakdownReady ? ":breakdown" : ":breakdown-miss";
                source += sellSetupState.PullupQualityReady ? ":pullup-quality" : ":pullup-quality-miss";
                source += sellSetupState.RewardRiskReady ? ":rr" : ":rr-miss";
            }

            return source;
        }

        var buySetupReady = buySetupState?.BaseReady ?? false;
        var buySetupEnhancedReady = (buySetupState?.EnhancedScore ?? 0) >= EnhancedSignalThreshold;

        if (buySetupReady)
        {
            source += ":buy-setup-confirmed";
        }
        else if (_requireBuySetupConfirmation)
        {
            source += ":buy-setup-required";
        }
        else
        {
            source += ":buy-setup-signal";
        }

        if (buySetupEnhancedReady)
        {
            source += ":buy-setup-plus-confirmed";
        }
        else if (_requireEnhancedBuySetupConfirmation)
        {
            source += ":buy-setup-plus-required";
        }
        else
        {
            source += ":buy-setup-plus-signal";
        }

        if (buySetupState is not null)
        {
            source += buySetupState.Criteria4Ready ? ":c4" : ":c4-miss";
            source += buySetupState.EntryBarReady ? ":entrybar" : ":entrybar-miss";
            source += buySetupState.CopBarReady ? ":cop" : ":cop-miss";
            source += buySetupState.PullbackQualityReady ? ":pullback-quality" : ":pullback-quality-miss";
            source += buySetupState.RewardRiskReady ? ":rr" : ":rr-miss";
            source += buySetupState.MultipleEntryBarsReady ? ":multi-entry" : ":multi-entry-miss";
            source += buySetupState.VolumeSpikeReady ? ":vol-spike" : ":vol-spike-miss";
            source += buySetupState.MinorSupportRetestReady ? ":minor-support" : ":minor-support-miss";
            source += buySetupState.TrendlineRetestReady ? ":trendline" : ":trendline-miss";
            source += buySetupState.PracticePatternReady ? ":practice-pattern" : ":practice-pattern-miss";
            source += buySetupState.BreakoutReady ? ":breakout" : ":breakout-miss";
            source += buySetupState.OneTwoThreeReady ? ":123" : ":123-miss";
        }

        return source;
    }

    private void UpdateBars(ReplayDayTradingContext context)
    {
        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        var open = context.BarOpen > 0 ? context.BarOpen : context.MarkPrice;
        var high = context.BarHigh > 0 ? context.BarHigh : context.MarkPrice;
        var low = context.BarLow > 0 ? context.BarLow : context.MarkPrice;
        var close = context.BarClose > 0 ? context.BarClose : context.MarkPrice;
        var volume = Math.Max(0.0, context.BarVolume);
        if (open <= 0 || high <= 0 || low <= 0 || close <= 0)
        {
            return;
        }

        if (!_barsBySymbol.TryGetValue(symbol, out var bars))
        {
            bars = [];
            _barsBySymbol[symbol] = bars;
        }

        var nextBar = new SetupBar(context.TimestampUtc, open, high, low, close, volume);
        if (bars.Count > 0 && bars[^1].TimestampUtc == context.TimestampUtc)
        {
            bars[^1] = nextBar;
        }
        else
        {
            bars.Add(nextBar);
        }

        var maxBars = Math.Max(SetupLookbackBars + SetupSmaLength + 5, 64);
        if (bars.Count > maxBars)
        {
            bars.RemoveRange(0, bars.Count - maxBars);
        }
    }

    private BuySetupSignalState AnalyzeBuySetupSignals(string symbol)
    {
        if (!_barsBySymbol.TryGetValue(symbol, out var bars)
            || bars.Count < SetupMinBars)
        {
            return EmptyBuySetupSignalState();
        }

        var current = bars[^1];
        var prior = bars.Take(bars.Count - 1).ToList();
        if (prior.Count < SetupSmaLength + SetupPullbackBars)
        {
            return EmptyBuySetupSignalState();
        }

        var lookbackStart = Math.Max(0, prior.Count - SetupLookbackBars);
        var lookback = prior.Skip(lookbackStart).ToList();
        if (lookback.Count < SetupPullbackBars + 2)
        {
            return EmptyBuySetupSignalState();
        }

        var peakIndexInLookback = 0;
        var peakHigh = lookback[0].High;
        for (var i = 1; i < lookback.Count; i++)
        {
            if (lookback[i].High >= peakHigh)
            {
                peakHigh = lookback[i].High;
                peakIndexInLookback = i;
            }
        }

        if (peakIndexInLookback <= 0 || peakIndexInLookback >= lookback.Count - SetupPullbackBars)
        {
            return EmptyBuySetupSignalState();
        }

        var rallySegment = lookback.Take(peakIndexInLookback + 1).ToList();
        var pullbackSegment = lookback.Skip(peakIndexInLookback + 1).ToList();
        if (rallySegment.Count < 2 || pullbackSegment.Count < SetupPullbackBars)
        {
            return EmptyBuySetupSignalState();
        }

        var latestPullback = pullbackSegment.Skip(Math.Max(0, pullbackSegment.Count - SetupPullbackBars)).ToList();

        var hasThreeRedBars = latestPullback.All(bar => bar.Close < bar.Open);
        var hasThreeLowerHighs = latestPullback.Count >= 3
            && latestPullback[0].High > latestPullback[1].High
            && latestPullback[1].High > latestPullback[2].High;
        var hasPullbackStructure = hasThreeRedBars || hasThreeLowerHighs;

        var rallyLow = rallySegment.Min(bar => bar.Low);
        var pullbackLow = pullbackSegment.Min(bar => bar.Low);
        var rallyRange = peakHigh - rallyLow;
        if (rallyRange <= 0)
        {
            return EmptyBuySetupSignalState();
        }

        var retracementPct = (peakHigh - pullbackLow) / rallyRange;
        var retracementInRange = retracementPct >= 0.40 && retracementPct <= 0.60;

        var closes = bars.Select(bar => bar.Close).ToList();
        if (closes.Count < SetupSmaLength + 1)
        {
            return EmptyBuySetupSignalState();
        }

        var currentSma20 = closes.Skip(closes.Count - SetupSmaLength).Average();
        var previousSma20 = closes.Skip(closes.Count - SetupSmaLength - 1).Take(SetupSmaLength).Average();
        var smaRising = currentSma20 > previousSma20;
        var aboveSma = current.Close >= currentSma20;

        var priorBar = prior[^1];
        var pullbackCompleted = current.Close > current.Open && current.Close > priorBar.High;
        var baseReady = hasPullbackStructure
            && retracementInRange
            && smaRising
            && aboveSma
            && pullbackCompleted;

        var pullbackRanges = latestPullback
            .Select(bar => Math.Max(0.0, bar.High - bar.Low))
            .ToList();
        var pullbackRangesContracting = pullbackRanges.Count >= 3
            && pullbackRanges[0] >= pullbackRanges[1]
            && pullbackRanges[1] >= pullbackRanges[2];
        var pullbackQualityReady = hasPullbackStructure && pullbackRangesContracting;

        var entryRange = Math.Max(0.0, current.High - current.Low);
        var avgPullbackRange = pullbackRanges.Count == 0 ? 0.0 : pullbackRanges.Average();
        var body = Math.Abs(current.Close - current.Open);
        var lowerWick = Math.Max(0.0, Math.Min(current.Open, current.Close) - current.Low);
        var entryNarrowRange = avgPullbackRange > 0 && entryRange <= avgPullbackRange * 0.85;
        var entryBottomingTail = entryRange > 0 && (lowerWick / entryRange) >= 0.35;
        var entryDoji = entryRange > 0 && (body / entryRange) <= 0.30;
        var entryBarReady = pullbackCompleted && (entryNarrowRange || entryBottomingTail || entryDoji);

        var copBarReady = priorBar.Close < priorBar.Open
            && current.Close > current.Open
            && (current.Close > priorBar.High
                || (current.Open <= priorBar.Close && current.Close >= priorBar.Open));

        var criteria4Ready = pullbackCompleted && pullbackLow > 0 && peakHigh > current.Close;

        var entryPrice = Math.Max(current.Close, priorBar.High * 1.0005);
        var stopPrice = pullbackLow * 0.9995;
        var targetPrice = peakHigh;
        var risk = entryPrice - stopPrice;
        var reward = targetPrice - entryPrice;
        var rewardRiskRatio = (risk > 0 && reward > 0) ? (reward / risk) : 0.0;
        var rewardRiskReady = rewardRiskRatio >= 1.5;

        var entryBars = pullbackSegment.Skip(Math.Max(0, pullbackSegment.Count - 3)).ToList();
        var maxEntryHigh = entryBars.Count == 0 ? 0.0 : entryBars.Max(bar => bar.High);
        var minEntryLow = entryBars.Count == 0 ? 0.0 : entryBars.Min(bar => bar.Low);
        var entryBarsRangeAvg = entryBars.Count == 0 ? 0.0 : entryBars.Average(bar => Math.Max(0.0, bar.High - bar.Low));
        var multipleEntryBarsReady = entryBars.Count >= 2
            && maxEntryHigh > 0
            && current.Close > maxEntryHigh
            && minEntryLow > 0
            && stopPrice < minEntryLow
            && (entryBarsRangeAvg <= Math.Max(entryRange, avgPullbackRange) * 1.1);

        var priorVolumes = prior
            .Skip(Math.Max(0, prior.Count - 8))
            .Select(bar => bar.Volume)
            .Where(volumeValue => volumeValue > 0)
            .ToList();
        var avgPriorVolume = priorVolumes.Count == 0 ? 0.0 : priorVolumes.Average();
        var volumeSpikeReady = hasPullbackStructure
            && current.Volume > 0
            && avgPriorVolume > 0
            && current.Volume >= avgPriorVolume * 1.5
            && current.Close > current.Open;

        var priorResistance = rallySegment
            .Take(Math.Max(1, rallySegment.Count - 1))
            .Max(bar => bar.High);
        var supportTolerancePct = 0.01;
        var minorSupportRetestReady = priorResistance > 0
            && pullbackLow <= priorResistance * (1.0 + supportTolerancePct)
            && pullbackLow >= priorResistance * (1.0 - supportTolerancePct)
            && current.Close >= priorResistance;

        var rallyFirstLow = rallySegment.First().Low;
        var rallyLastLow = rallySegment.Last().Low;
        var trendlineRetestReady = false;
        if (rallySegment.Count >= 2 && rallyLastLow > rallyFirstLow)
        {
            var slope = (rallyLastLow - rallyFirstLow) / (rallySegment.Count - 1);
            var projectedTrendlineAtPullbackEnd = rallyLastLow + slope * pullbackSegment.Count;
            if (projectedTrendlineAtPullbackEnd > 0)
            {
                trendlineRetestReady = pullbackLow >= projectedTrendlineAtPullbackEnd * 0.985
                    && pullbackLow <= projectedTrendlineAtPullbackEnd * 1.015
                    && current.Close >= projectedTrendlineAtPullbackEnd;
            }
        }

        var practicePatternReady = (multipleEntryBarsReady || entryBarReady)
            && (minorSupportRetestReady || trendlineRetestReady)
            && (volumeSpikeReady || rewardRiskReady);
        var breakoutReady = current.Close > peakHigh && (avgPriorVolume <= 0 || current.Volume >= avgPriorVolume * 1.2);
        var oneTwoThreeReady = hasPullbackStructure
            && pullbackLow > rallyLow
            && priorBar.High > 0
            && current.Close > priorBar.High;

        var enhancedScore = 0;
        if (criteria4Ready)
        {
            enhancedScore++;
        }

        if (entryBarReady)
        {
            enhancedScore++;
        }

        if (copBarReady)
        {
            enhancedScore++;
        }

        if (pullbackQualityReady)
        {
            enhancedScore++;
        }

        if (rewardRiskReady)
        {
            enhancedScore++;
        }

        if (multipleEntryBarsReady)
        {
            enhancedScore++;
        }

        if (volumeSpikeReady)
        {
            enhancedScore++;
        }

        if (minorSupportRetestReady)
        {
            enhancedScore++;
        }

        if (trendlineRetestReady)
        {
            enhancedScore++;
        }

        if (practicePatternReady)
        {
            enhancedScore++;
        }

        if (breakoutReady)
        {
            enhancedScore++;
        }

        if (oneTwoThreeReady)
        {
            enhancedScore++;
        }

        return new BuySetupSignalState(
            BaseReady: baseReady,
            Criteria4Ready: criteria4Ready,
            EntryBarReady: entryBarReady,
            CopBarReady: copBarReady,
            PullbackQualityReady: pullbackQualityReady,
            RewardRiskReady: rewardRiskReady,
            MultipleEntryBarsReady: multipleEntryBarsReady,
            VolumeSpikeReady: volumeSpikeReady,
            MinorSupportRetestReady: minorSupportRetestReady,
            TrendlineRetestReady: trendlineRetestReady,
            PracticePatternReady: practicePatternReady,
            BreakoutReady: breakoutReady,
            OneTwoThreeReady: oneTwoThreeReady,
            RewardRiskRatio: rewardRiskRatio,
            EnhancedScore: enhancedScore);
    }

    private static BuySetupSignalState EmptyBuySetupSignalState()
    {
        return new BuySetupSignalState(
            BaseReady: false,
            Criteria4Ready: false,
            EntryBarReady: false,
            CopBarReady: false,
            PullbackQualityReady: false,
            RewardRiskReady: false,
            MultipleEntryBarsReady: false,
            VolumeSpikeReady: false,
            MinorSupportRetestReady: false,
            TrendlineRetestReady: false,
            PracticePatternReady: false,
            BreakoutReady: false,
            OneTwoThreeReady: false,
            RewardRiskRatio: 0.0,
            EnhancedScore: 0);
    }

    private SellSetupSignalState AnalyzeSellSetupSignals(string symbol)
    {
        if (!_barsBySymbol.TryGetValue(symbol, out var bars)
            || bars.Count < SetupMinBars)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var current = bars[^1];
        var prior = bars.Take(bars.Count - 1).ToList();
        if (prior.Count < SetupSmaLength + SetupPullbackBars)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var lookbackStart = Math.Max(0, prior.Count - SetupLookbackBars);
        var lookback = prior.Skip(lookbackStart).ToList();
        if (lookback.Count < SetupPullbackBars + 2)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var troughIndexInLookback = 0;
        var troughLow = lookback[0].Low;
        for (var i = 1; i < lookback.Count; i++)
        {
            if (lookback[i].Low <= troughLow)
            {
                troughLow = lookback[i].Low;
                troughIndexInLookback = i;
            }
        }

        if (troughIndexInLookback <= 0 || troughIndexInLookback >= lookback.Count - SetupPullbackBars)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var dropSegment = lookback.Take(troughIndexInLookback + 1).ToList();
        var pullupSegment = lookback.Skip(troughIndexInLookback + 1).ToList();
        if (dropSegment.Count < 2 || pullupSegment.Count < SetupPullbackBars)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var latestPullup = pullupSegment.Skip(Math.Max(0, pullupSegment.Count - SetupPullbackBars)).ToList();
        var hasThreeGreenBars = latestPullup.All(bar => bar.Close > bar.Open);
        var hasThreeHigherLows = latestPullup.Count >= 3
            && latestPullup[0].Low < latestPullup[1].Low
            && latestPullup[1].Low < latestPullup[2].Low;
        var hasPullupStructure = hasThreeGreenBars || hasThreeHigherLows;

        var dropHigh = dropSegment.Max(bar => bar.High);
        var pullupHigh = pullupSegment.Max(bar => bar.High);
        var dropRange = dropHigh - troughLow;
        if (dropRange <= 0)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var retracementPct = (pullupHigh - troughLow) / dropRange;
        var retracementInRange = retracementPct >= 0.40 && retracementPct <= 0.60;
        var breakdownReady = current.Close < current.Open && current.Close < prior[^1].Low;

        var closes = bars.Select(bar => bar.Close).ToList();
        if (closes.Count < SetupSmaLength + 1)
        {
            return new SellSetupSignalState(false, false, false, false, 0);
        }

        var currentSma20 = closes.Skip(closes.Count - SetupSmaLength).Average();
        var previousSma20 = closes.Skip(closes.Count - SetupSmaLength - 1).Take(SetupSmaLength).Average();
        var smaFalling = currentSma20 < previousSma20;
        var belowSma = current.Close <= currentSma20;

        var stopPrice = pullupHigh * 1.0005;
        var targetPrice = troughLow;
        var entryPrice = Math.Min(current.Close, prior[^1].Low * 0.9995);
        var risk = stopPrice - entryPrice;
        var reward = entryPrice - targetPrice;
        var rewardRiskRatio = (risk > 0 && reward > 0) ? (reward / risk) : 0.0;
        var rewardRiskReady = rewardRiskRatio >= 1.5;

        var pullupRanges = latestPullup.Select(bar => Math.Max(0.0, bar.High - bar.Low)).ToList();
        var pullupQualityReady = pullupRanges.Count >= 3
            && pullupRanges[0] >= pullupRanges[1]
            && pullupRanges[1] >= pullupRanges[2];

        var baseReady = hasPullupStructure
            && retracementInRange
            && smaFalling
            && belowSma
            && breakdownReady;

        var enhancedScore = 0;
        if (breakdownReady)
        {
            enhancedScore++;
        }

        if (pullupQualityReady)
        {
            enhancedScore++;
        }

        if (rewardRiskReady)
        {
            enhancedScore++;
        }

        return new SellSetupSignalState(baseReady, breakdownReady, pullupQualityReady, rewardRiskReady, enhancedScore);
    }

    private string ResolveOrderSide(double positionQuantity)
    {
        if (string.Equals(_orderSide, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return positionQuantity > 1e-9 ? "SELL" : "BUY";
        }

        return _orderSide;
    }

    private static string NormalizeOrderSide(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "BUY" : value.Trim().ToUpperInvariant();
        return normalized is "BUY" or "SELL" or "AUTO"
            ? normalized
            : "BUY";
    }

    private static string NormalizeOrderType(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "MKT" : value.Trim().ToUpperInvariant();
        return normalized is "MKT" or "LMT"
            ? normalized
            : "MKT";
    }

    private static string NormalizeTimeInForce(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "DAY" : value.Trim().ToUpperInvariant();
        return normalized is "DAY" or "DAY+" or "GTC" or "IOC" or "FOK"
            ? normalized
            : "DAY";
    }
}
