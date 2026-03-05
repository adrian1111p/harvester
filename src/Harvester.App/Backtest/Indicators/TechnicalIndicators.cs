using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Backtest.Indicators;

/// <summary>
/// Technical indicators for the Harvester Backtest Engine.
/// Direct C# port of indicators.py — all 24 functions.
/// All operate on double[] arrays (replacing pandas Series/DataFrame).
/// </summary>
public static class TechnicalIndicators
{
    // ── Moving Averages ──────────────────────────────────────────────────────

    /// <summary>Exponential Moving Average.</summary>
    public static double[] Ema(double[] series, int period)
    {
        var result = new double[series.Length];
        if (series.Length == 0) return result;

        double alpha = 2.0 / (period + 1);
        result[0] = series[0];
        for (int i = 1; i < series.Length; i++)
        {
            if (double.IsNaN(series[i]))
            {
                result[i] = result[i - 1];
                continue;
            }
            result[i] = alpha * series[i] + (1.0 - alpha) * result[i - 1];
        }
        return result;
    }

    /// <summary>Simple Moving Average.</summary>
    public static double[] Sma(double[] series, int period)
    {
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            if (i < period - 1)
            {
                result[i] = double.NaN;
                continue;
            }
            double sum = 0;
            int count = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                if (!double.IsNaN(series[j]))
                {
                    sum += series[j];
                    count++;
                }
            }
            result[i] = count == period ? sum / period : double.NaN;
        }
        return result;
    }

    // ── ATR ──────────────────────────────────────────────────────────────────

    /// <summary>True Range = max(H-L, |H-Cprev|, |L-Cprev|).</summary>
    public static double[] TrueRange(BacktestBar[] bars)
    {
        var tr = new double[bars.Length];
        tr[0] = bars[0].High - bars[0].Low;
        for (int i = 1; i < bars.Length; i++)
        {
            double hl = bars[i].High - bars[i].Low;
            double hc = Math.Abs(bars[i].High - bars[i - 1].Close);
            double lc = Math.Abs(bars[i].Low - bars[i - 1].Close);
            tr[i] = Math.Max(hl, Math.Max(hc, lc));
        }
        return tr;
    }

    /// <summary>Average True Range using Wilder EMA.</summary>
    public static double[] Atr(BacktestBar[] bars, int period = 14)
    {
        var tr = TrueRange(bars);
        return WilderEma(tr, period);
    }

    // ── RSI ──────────────────────────────────────────────────────────────────

    /// <summary>Relative Strength Index with Wilder smoothing.</summary>
    public static double[] Rsi(double[] series, int period = 14)
    {
        var result = new double[series.Length];
        result[0] = double.NaN;

        var gain = new double[series.Length];
        var loss = new double[series.Length];

        for (int i = 1; i < series.Length; i++)
        {
            double delta = series[i] - series[i - 1];
            gain[i] = Math.Max(0, delta);
            loss[i] = Math.Max(0, -delta);
        }

        var avgGain = WilderEma(gain, period);
        var avgLoss = WilderEma(loss, period);

        for (int i = 0; i < series.Length; i++)
        {
            if (avgLoss[i] == 0 || double.IsNaN(avgLoss[i]))
            {
                result[i] = 100.0;
                continue;
            }
            double rs = avgGain[i] / avgLoss[i];
            result[i] = 100.0 - (100.0 / (1.0 + rs));
        }
        return result;
    }

    // ── MACD ─────────────────────────────────────────────────────────────────

    /// <summary>MACD line, signal line, histogram.</summary>
    public static MacdResult[] Macd(double[] series, int fast = 12, int slow = 26, int signal = 9)
    {
        var emaFast = Ema(series, fast);
        var emaSlow = Ema(series, slow);

        var macdLine = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
            macdLine[i] = emaFast[i] - emaSlow[i];

        var signalLine = Ema(macdLine, signal);

        var result = new MacdResult[series.Length];
        for (int i = 0; i < series.Length; i++)
            result[i] = new MacdResult(macdLine[i], signalLine[i], macdLine[i] - signalLine[i]);

        return result;
    }

    // ── Bollinger Bands ──────────────────────────────────────────────────────

    /// <summary>Bollinger Bands: middle (SMA), upper, lower, %B, bandwidth.</summary>
    public static BollingerResult[] BollingerBands(double[] series, int period = 20, double numStd = 2.0)
    {
        var middle = Sma(series, period);
        var result = new BollingerResult[series.Length];

        for (int i = 0; i < series.Length; i++)
        {
            if (double.IsNaN(middle[i]) || i < period - 1)
            {
                result[i] = new BollingerResult(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
                continue;
            }

            // Standard deviation
            double sumSq = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                double diff = series[j] - middle[i];
                sumSq += diff * diff;
            }
            double std = Math.Sqrt(sumSq / period);

            double upper = middle[i] + numStd * std;
            double lower = middle[i] - numStd * std;
            double range = upper - lower;
            double pctB = range > 0 ? (series[i] - lower) / range : double.NaN;
            double bandwidth = middle[i] > 0 ? (range / middle[i]) * 100.0 : double.NaN;

            result[i] = new BollingerResult(middle[i], upper, lower, pctB, bandwidth);
        }
        return result;
    }

    // ── ADX ──────────────────────────────────────────────────────────────────

    /// <summary>ADX with +DI / -DI.</summary>
    public static AdxResult[] Adx(BacktestBar[] bars, int period = 14)
    {
        var length = bars.Length;
        var plusDm = new double[length];
        var minusDm = new double[length];

        for (int i = 1; i < length; i++)
        {
            double upMove = bars[i].High - bars[i - 1].High;
            double downMove = bars[i - 1].Low - bars[i].Low;
            plusDm[i] = (upMove > downMove && upMove > 0) ? upMove : 0;
            minusDm[i] = (downMove > upMove && downMove > 0) ? downMove : 0;
        }

        var tr = TrueRange(bars);
        var atrVal = WilderEma(tr, period);
        var smoothPlusDm = WilderEma(plusDm, period);
        var smoothMinusDm = WilderEma(minusDm, period);

        var result = new AdxResult[length];
        var dx = new double[length];

        for (int i = 0; i < length; i++)
        {
            double plusDi = atrVal[i] > 0 ? 100.0 * smoothPlusDm[i] / atrVal[i] : 0;
            double minusDi = atrVal[i] > 0 ? 100.0 * smoothMinusDm[i] / atrVal[i] : 0;
            double diSum = plusDi + minusDi;
            dx[i] = diSum > 0 ? 100.0 * Math.Abs(plusDi - minusDi) / diSum : 0;
            result[i] = new AdxResult(0, plusDi, minusDi); // ADX filled below
        }

        var adxVal = WilderEma(dx, period);
        for (int i = 0; i < length; i++)
            result[i] = result[i] with { Adx = adxVal[i] };

        return result;
    }

    // ── VWAP ─────────────────────────────────────────────────────────────────

    /// <summary>Session-anchored VWAP – resets each trading day.</summary>
    public static double[] Vwap(BacktestBar[] bars)
    {
        var result = new double[bars.Length];
        double cumTpVol = 0;
        double cumVol = 0;
        DateOnly prevDate = default;

        for (int i = 0; i < bars.Length; i++)
        {
            var curDate = DateOnly.FromDateTime(bars[i].Timestamp);
            if (curDate != prevDate)
            {
                cumTpVol = 0;
                cumVol = 0;
                prevDate = curDate;
            }

            double typical = (bars[i].High + bars[i].Low + bars[i].Close) / 3.0;
            cumTpVol += typical * bars[i].Volume;
            cumVol += bars[i].Volume;
            result[i] = cumVol > 0 ? cumTpVol / cumVol : double.NaN;
        }
        return result;
    }

    // ── Supertrend ───────────────────────────────────────────────────────────

    /// <summary>Supertrend indicator for trend direction.</summary>
    public static SupertrendResult[] Supertrend(BacktestBar[] bars, int period = 10, double multiplier = 3.0)
    {
        var length = bars.Length;
        var atrVal = Atr(bars, period);

        var upperBand = new double[length];
        var lowerBand = new double[length];
        var direction = new int[length];
        var stVal = new double[length];

        for (int i = 0; i < length; i++)
        {
            double hl2 = (bars[i].High + bars[i].Low) / 2.0;
            upperBand[i] = hl2 + multiplier * atrVal[i];
            lowerBand[i] = hl2 - multiplier * atrVal[i];
            direction[i] = 1;
            stVal[i] = double.NaN;
        }

        for (int i = 1; i < length; i++)
        {
            if (bars[i].Close > upperBand[i - 1])
                direction[i] = 1;
            else if (bars[i].Close < lowerBand[i - 1])
                direction[i] = -1;
            else
                direction[i] = direction[i - 1];

            if (direction[i] == 1)
            {
                if (direction[i - 1] == 1)
                    lowerBand[i] = Math.Max(lowerBand[i], lowerBand[i - 1]);
                stVal[i] = lowerBand[i];
            }
            else
            {
                if (direction[i - 1] == -1)
                    upperBand[i] = Math.Min(upperBand[i], upperBand[i - 1]);
                stVal[i] = upperBand[i];
            }
        }

        var result = new SupertrendResult[length];
        for (int i = 0; i < length; i++)
            result[i] = new SupertrendResult(stVal[i], direction[i]);

        return result;
    }

    // ── Relative Volume ──────────────────────────────────────────────────────

    /// <summary>RVOL: current volume / average volume.</summary>
    public static double[] RelativeVolume(double[] volume, int period = 20)
    {
        var result = new double[volume.Length];
        for (int i = 0; i < volume.Length; i++)
        {
            double sum = 0;
            int count = 0;
            int start = Math.Max(0, i - period + 1);
            for (int j = start; j <= i; j++)
            {
                sum += volume[j];
                count++;
            }
            double avg = count > 0 ? sum / count : 0;
            result[i] = avg > 0 ? volume[i] / avg : double.NaN;
        }
        return result;
    }

    // ── Stochastic Oscillator ────────────────────────────────────────────────

    /// <summary>Stochastic %K and %D.</summary>
    public static StochasticResult[] Stochastic(BacktestBar[] bars, int kPeriod = 14, int dPeriod = 3, int smoothK = 3)
    {
        var length = bars.Length;
        var rawK = new double[length];

        for (int i = 0; i < length; i++)
        {
            if (i < kPeriod - 1) { rawK[i] = double.NaN; continue; }
            double lowMin = double.MaxValue;
            double highMax = double.MinValue;
            for (int j = i - kPeriod + 1; j <= i; j++)
            {
                lowMin = Math.Min(lowMin, bars[j].Low);
                highMax = Math.Max(highMax, bars[j].High);
            }
            double range = highMax - lowMin;
            rawK[i] = range > 0 ? 100.0 * (bars[i].Close - lowMin) / range : double.NaN;
        }

        // Smooth %K with SMA-like rolling
        var k = RollingMean(rawK, smoothK);
        var d = RollingMean(k, dPeriod);

        var result = new StochasticResult[length];
        for (int i = 0; i < length; i++)
            result[i] = new StochasticResult(k[i], d[i]);
        return result;
    }

    // ── Keltner Channels ─────────────────────────────────────────────────────

    /// <summary>Keltner Channels: EMA ± multiplier × ATR.</summary>
    public static KeltnerResult[] KeltnerChannels(BacktestBar[] bars, int emaPeriod = 20, int atrPeriod = 14, double multiplier = 1.5)
    {
        var closes = ExtractCloses(bars);
        var mid = Ema(closes, emaPeriod);
        var atrVal = Atr(bars, atrPeriod);

        var result = new KeltnerResult[bars.Length];
        for (int i = 0; i < bars.Length; i++)
            result[i] = new KeltnerResult(mid[i], mid[i] + multiplier * atrVal[i], mid[i] - multiplier * atrVal[i]);
        return result;
    }

    // ── MFI ──────────────────────────────────────────────────────────────────

    /// <summary>Money Flow Index: RSI weighted by volume.</summary>
    public static double[] Mfi(BacktestBar[] bars, int period = 14)
    {
        var length = bars.Length;
        var result = new double[length];
        var typical = new double[length];
        var rawMf = new double[length];

        for (int i = 0; i < length; i++)
            typical[i] = (bars[i].High + bars[i].Low + bars[i].Close) / 3.0;

        for (int i = 0; i < length; i++)
            rawMf[i] = typical[i] * bars[i].Volume;

        for (int i = 0; i < length; i++)
        {
            if (i < period)
            {
                result[i] = double.NaN;
                continue;
            }

            double posSum = 0, negSum = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                if (j == 0) continue;
                if (typical[j] > typical[j - 1])
                    posSum += rawMf[j];
                else if (typical[j] < typical[j - 1])
                    negSum += rawMf[j];
            }

            result[i] = negSum > 0 ? 100.0 - (100.0 / (1.0 + posSum / negSum)) : 100.0;
        }
        return result;
    }

    // ── Spread Proxy ─────────────────────────────────────────────────────────

    /// <summary>Spread proxy from bar range vs ATR.</summary>
    public static SpreadResult[] SpreadProxy(BacktestBar[] bars, int period = 20)
    {
        var atrVal = Atr(bars, period);
        var ratio = new double[bars.Length];

        for (int i = 0; i < bars.Length; i++)
        {
            double barRange = bars[i].High - bars[i].Low;
            ratio[i] = atrVal[i] > 0 ? barRange / atrVal[i] : double.NaN;
        }

        var ratioMean = RollingMean(ratio, period);
        var ratioStd = RollingStd(ratio, period);

        var result = new SpreadResult[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            double z = ratioStd[i] > 0 ? (ratio[i] - ratioMean[i]) / ratioStd[i] : double.NaN;
            result[i] = new SpreadResult(ratio[i], z);
        }
        return result;
    }

    // ── Volume Delta Proxy ───────────────────────────────────────────────────

    /// <summary>Volume delta proxy: net buying/selling pressure [-1, +1].</summary>
    public static double[] VolumeDeltaProxy(BacktestBar[] bars)
    {
        var result = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            double barRange = bars[i].High - bars[i].Low;
            if (barRange <= 0 || bars[i].Volume <= 0)
            {
                result[i] = 0;
                continue;
            }
            double buyFrac = (bars[i].Close - bars[i].Low) / barRange;
            double sellFrac = (bars[i].High - bars[i].Close) / barRange;
            result[i] = buyFrac - sellFrac;
        }
        return result;
    }

    // ── Order Flow Imbalance ─────────────────────────────────────────────────

    /// <summary>L2-proxy order flow imbalance using cumulative volume delta.</summary>
    public static OrderFlowResult[] OrderFlowImbalance(BacktestBar[] bars, int period = 10)
    {
        var raw = VolumeDeltaProxy(bars);
        var cum = RollingSum(raw, period);
        var sig = Ema(cum, period);

        var result = new OrderFlowResult[bars.Length];
        for (int i = 0; i < bars.Length; i++)
            result[i] = new OrderFlowResult(raw[i], cum[i], sig[i]);
        return result;
    }

    // ── Volume Acceleration ──────────────────────────────────────────────────

    /// <summary>Rate of change of volume.</summary>
    public static double[] VolumeAcceleration(BacktestBar[] bars, int period = 5)
    {
        var volumes = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++) volumes[i] = bars[i].Volume;
        var volMa = RollingMean(volumes, period);

        var result = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++)
            result[i] = volMa[i] > 0 ? (volumes[i] - volMa[i]) / volMa[i] : double.NaN;
        return result;
    }

    // ── L2 Liquidity Score ───────────────────────────────────────────────────

    /// <summary>L2 liquidity proxy: spread + volume consistency + RVOL.</summary>
    public static double[] L2LiquidityScore(BacktestBar[] bars, int period = 20)
    {
        var atrVal = Atr(bars, period);
        var volumes = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++) volumes[i] = bars[i].Volume;

        var rvol = RelativeVolume(volumes, period);
        var volMean = RollingMean(volumes, period);
        var volStd = RollingStd(volumes, period);

        var result = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            double barRange = bars[i].High - bars[i].Low;
            double spreadRatio = atrVal[i] > 0 ? barRange / atrVal[i] : 1.0;
            double spreadScore = Math.Min(3.0, spreadRatio > 0 ? 1.0 / spreadRatio : 0) * 33.3;

            double volCv = volMean[i] > 0 ? volStd[i] / volMean[i] : 1.0;
            double volScore = Math.Min(3.0, volCv > 0 ? 1.0 / volCv : 0) * 33.3;

            double rvolScore = Math.Min(3.0, double.IsNaN(rvol[i]) ? 0 : rvol[i]) * 33.3 / 3.0;

            result[i] = Math.Min(100.0, spreadScore + volScore + rvolScore);
        }
        return result;
    }

    // ── Williams %R ──────────────────────────────────────────────────────────

    /// <summary>Williams %R: -100 to 0.</summary>
    public static double[] WilliamsR(BacktestBar[] bars, int period = 14)
    {
        var result = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            if (i < period - 1) { result[i] = double.NaN; continue; }
            double highMax = double.MinValue;
            double lowMin = double.MaxValue;
            for (int j = i - period + 1; j <= i; j++)
            {
                highMax = Math.Max(highMax, bars[j].High);
                lowMin = Math.Min(lowMin, bars[j].Low);
            }
            double range = highMax - lowMin;
            result[i] = range > 0 ? -100.0 * (highMax - bars[i].Close) / range : double.NaN;
        }
        return result;
    }

    // ── Donchian Channels ────────────────────────────────────────────────────

    /// <summary>Donchian Channels: highest high / lowest low.</summary>
    public static DonchianResult[] DonchianChannels(BacktestBar[] bars, int period = 20)
    {
        var result = new DonchianResult[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            if (i < period - 1)
            {
                result[i] = new DonchianResult(double.NaN, double.NaN, double.NaN, double.NaN);
                continue;
            }
            double highMax = double.MinValue;
            double lowMin = double.MaxValue;
            for (int j = i - period + 1; j <= i; j++)
            {
                highMax = Math.Max(highMax, bars[j].High);
                lowMin = Math.Min(lowMin, bars[j].Low);
            }
            double mid = (highMax + lowMin) / 2.0;
            double range = highMax - lowMin;
            double pct = range > 0 ? (bars[i].Close - lowMin) / range : double.NaN;
            result[i] = new DonchianResult(highMax, lowMin, mid, pct);
        }
        return result;
    }

    // ── DPO (Detrended Price Oscillator) ─────────────────────────────────────

    /// <summary>Detrended Price Oscillator — isolates cycles from trend.</summary>
    public static double[] Dpo(double[] series, int period = 20)
    {
        var smaVal = Sma(series, period);
        int shift = period / 2 + 1;
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            int shiftedIdx = i - shift;
            result[i] = shiftedIdx >= 0 && !double.IsNaN(smaVal[shiftedIdx])
                ? series[i] - smaVal[shiftedIdx]
                : double.NaN;
        }
        return result;
    }

    // ── Master Enrichment ────────────────────────────────────────────────────

    /// <summary>Enrich bars with ALL indicators — matches Python enrich_with_indicators().</summary>
    public static EnrichedBar[] EnrichWithIndicators(BacktestBar[] bars)
    {
        if (bars.Length == 0) return [];

        // Extract price arrays
        var closes = ExtractCloses(bars);
        var volumes = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++) volumes[i] = bars[i].Volume;

        // Compute all indicators
        var ema9 = Ema(closes, 9);
        var ema21 = Ema(closes, 21);
        var ema50 = Ema(closes, 50);
        var sma20 = Sma(closes, 20);
        var sma200 = Sma(closes, 200);
        var atr14 = Atr(bars, 14);
        var rsi14 = Rsi(closes, 14);
        var macdArr = Macd(closes);
        var bbArr = BollingerBands(closes);
        var adxArr = Adx(bars);
        var stArr = Supertrend(bars);
        var rvol = RelativeVolume(volumes);
        var vwapArr = Vwap(bars);
        var stochArr = Stochastic(bars);
        var kcArr = KeltnerChannels(bars);
        var mfiArr = Mfi(bars);
        var ofiArr = OrderFlowImbalance(bars);
        var spArr = SpreadProxy(bars);
        var volAccel = VolumeAcceleration(bars);
        var l2Liq = L2LiquidityScore(bars);
        var willR = WilliamsR(bars);
        var dcArr = DonchianChannels(bars);
        var dpoArr = Dpo(closes, 20);

        // Build enriched bars
        var result = new EnrichedBar[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            var eb = new EnrichedBar(bars[i])
            {
                Ema9 = ema9[i],
                Ema21 = ema21[i],
                Ema50 = ema50[i],
                Sma20 = sma20[i],
                Sma200 = sma200[i],
                Atr14 = atr14[i],
                Rsi14 = rsi14[i],
                Macd = macdArr[i].Macd,
                MacdSignal = macdArr[i].Signal,
                MacdHist = macdArr[i].Histogram,
                BbMid = bbArr[i].Mid,
                BbUpper = bbArr[i].Upper,
                BbLower = bbArr[i].Lower,
                BbPctB = bbArr[i].PctB,
                BbBandwidth = bbArr[i].Bandwidth,
                Adx = adxArr[i].Adx,
                PlusDi = adxArr[i].PlusDi,
                MinusDi = adxArr[i].MinusDi,
                Supertrend = stArr[i].Value,
                StDirection = stArr[i].Direction,
                Rvol = rvol[i],
                Vwap = vwapArr[i],
                StochK = stochArr[i].K,
                StochD = stochArr[i].D,
                KcMid = kcArr[i].Mid,
                KcUpper = kcArr[i].Upper,
                KcLower = kcArr[i].Lower,
                Mfi14 = mfiArr[i],
                OfiRaw = ofiArr[i].Raw,
                OfiCum = ofiArr[i].Cumulative,
                OfiSignal = ofiArr[i].Signal,
                SpreadRatio = spArr[i].Ratio,
                SpreadZ = spArr[i].ZScore,
                VolAccel = volAccel[i],
                L2Liquidity = l2Liq[i],
                WillR14 = willR[i],
                DcUpper = dcArr[i].Upper,
                DcLower = dcArr[i].Lower,
                DcMid = dcArr[i].Mid,
                DcPct = dcArr[i].Pct,
                Dpo20 = dpoArr[i],
            };
            result[i] = eb;
        }
        return result;
    }

    // ── Helper Methods ───────────────────────────────────────────────────────

    /// <summary>Wilder EMA (alpha = 1/period).</summary>
    private static double[] WilderEma(double[] series, int period)
    {
        var result = new double[series.Length];
        if (series.Length == 0) return result;

        double alpha = 1.0 / period;
        result[0] = series[0];
        for (int i = 1; i < series.Length; i++)
        {
            if (double.IsNaN(series[i]))
            {
                result[i] = result[i - 1];
                continue;
            }
            result[i] = alpha * series[i] + (1.0 - alpha) * result[i - 1];
        }
        return result;
    }

    /// <summary>Extract Close prices from bar array.</summary>
    private static double[] ExtractCloses(BacktestBar[] bars)
    {
        var closes = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++) closes[i] = bars[i].Close;
        return closes;
    }

    /// <summary>Rolling mean with min_periods=1 behavior.</summary>
    private static double[] RollingMean(double[] series, int period)
    {
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            double sum = 0;
            int count = 0;
            int start = Math.Max(0, i - period + 1);
            for (int j = start; j <= i; j++)
            {
                if (!double.IsNaN(series[j]))
                {
                    sum += series[j];
                    count++;
                }
            }
            result[i] = count > 0 ? sum / count : double.NaN;
        }
        return result;
    }

    /// <summary>Rolling standard deviation with min_periods=1 behavior.</summary>
    private static double[] RollingStd(double[] series, int period)
    {
        var mean = RollingMean(series, period);
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            if (double.IsNaN(mean[i])) { result[i] = double.NaN; continue; }
            double sumSq = 0;
            int count = 0;
            int start = Math.Max(0, i - period + 1);
            for (int j = start; j <= i; j++)
            {
                if (!double.IsNaN(series[j]))
                {
                    double diff = series[j] - mean[i];
                    sumSq += diff * diff;
                    count++;
                }
            }
            result[i] = count > 1 ? Math.Sqrt(sumSq / (count - 1)) : 0;
        }
        return result;
    }

    /// <summary>Rolling sum with min_periods=1 behavior.</summary>
    private static double[] RollingSum(double[] series, int period)
    {
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            double sum = 0;
            int start = Math.Max(0, i - period + 1);
            for (int j = start; j <= i; j++)
            {
                if (!double.IsNaN(series[j]))
                    sum += series[j];
            }
            result[i] = sum;
        }
        return result;
    }
}
