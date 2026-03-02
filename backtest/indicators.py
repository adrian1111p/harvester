"""
indicators.py — Technical indicators for the Harvester Backtest Engine V1.1.
Covers: EMA/SMA, ATR, RSI, MACD, VWAP, Bollinger Bands, ADX, Order-book pressure,
        Stochastic, Keltner Channels, L1/L2 proxy signals, volume profile, MFI,
        and cycle-detection oscillators.
All operate on pandas DataFrames with columns: Open, High, Low, Close, Volume.
"""
from __future__ import annotations
import numpy as np
import pandas as pd


# ── Moving Averages ──────────────────────────────────────────────────────────

def ema(series: pd.Series, period: int) -> pd.Series:
    """Exponential moving average."""
    return series.ewm(span=period, adjust=False).mean()


def sma(series: pd.Series, period: int) -> pd.Series:
    """Simple moving average."""
    return series.rolling(window=period, min_periods=period).mean()


# ── ATR ──────────────────────────────────────────────────────────────────────

def true_range(df: pd.DataFrame) -> pd.Series:
    """True Range = max(H-L, |H-Cprev|, |L-Cprev|)."""
    prev_close = df["Close"].shift(1)
    hl = df["High"] - df["Low"]
    hc = (df["High"] - prev_close).abs()
    lc = (df["Low"] - prev_close).abs()
    return pd.concat([hl, hc, lc], axis=1).max(axis=1)


def atr(df: pd.DataFrame, period: int = 14) -> pd.Series:
    """Average True Range (Wilder EMA)."""
    tr = true_range(df)
    return tr.ewm(alpha=1.0 / period, adjust=False).mean()


# ── RSI ──────────────────────────────────────────────────────────────────────

def rsi(series: pd.Series, period: int = 14) -> pd.Series:
    """Relative Strength Index (Wilder smoothing)."""
    delta = series.diff()
    gain = delta.clip(lower=0)
    loss = (-delta).clip(lower=0)
    avg_gain = gain.ewm(alpha=1.0 / period, adjust=False).mean()
    avg_loss = loss.ewm(alpha=1.0 / period, adjust=False).mean()
    rs = avg_gain / avg_loss.replace(0, np.nan)
    return 100.0 - (100.0 / (1.0 + rs))


# ── MACD ─────────────────────────────────────────────────────────────────────

def macd(series: pd.Series, fast: int = 12, slow: int = 26, signal: int = 9) -> pd.DataFrame:
    """MACD line, signal line, histogram."""
    ema_fast = ema(series, fast)
    ema_slow = ema(series, slow)
    macd_line = ema_fast - ema_slow
    signal_line = ema(macd_line, signal)
    histogram = macd_line - signal_line
    return pd.DataFrame({
        "MACD": macd_line,
        "Signal": signal_line,
        "Histogram": histogram,
    })


# ── Bollinger Bands ──────────────────────────────────────────────────────────

def bollinger_bands(series: pd.Series, period: int = 20, num_std: float = 2.0) -> pd.DataFrame:
    """Bollinger Bands: middle (SMA), upper, lower, %B, bandwidth."""
    middle = sma(series, period)
    std = series.rolling(window=period, min_periods=period).std()
    upper = middle + num_std * std
    lower = middle - num_std * std
    pct_b = (series - lower) / (upper - lower).replace(0, np.nan)
    bandwidth = ((upper - lower) / middle.replace(0, np.nan)) * 100.0
    return pd.DataFrame({
        "BB_Mid": middle,
        "BB_Upper": upper,
        "BB_Lower": lower,
        "BB_PctB": pct_b,
        "BB_Bandwidth": bandwidth,
    })


# ── ADX (Average Directional Index) ─────────────────────────────────────────

def adx(df: pd.DataFrame, period: int = 14) -> pd.DataFrame:
    """ADX with +DI / -DI."""
    high = df["High"]
    low = df["Low"]
    close = df["Close"]

    plus_dm = high.diff().clip(lower=0)
    minus_dm = (-low.diff()).clip(lower=0)

    # Zero out when the other is larger
    cond = plus_dm > minus_dm
    plus_dm = plus_dm.where(cond, 0.0)
    minus_dm = minus_dm.where(~cond, 0.0)

    tr = true_range(df)
    atr_val = tr.ewm(alpha=1.0 / period, adjust=False).mean()

    plus_di = 100.0 * (plus_dm.ewm(alpha=1.0 / period, adjust=False).mean() / atr_val.replace(0, np.nan))
    minus_di = 100.0 * (minus_dm.ewm(alpha=1.0 / period, adjust=False).mean() / atr_val.replace(0, np.nan))

    dx = 100.0 * ((plus_di - minus_di).abs() / (plus_di + minus_di).replace(0, np.nan))
    adx_val = dx.ewm(alpha=1.0 / period, adjust=False).mean()

    return pd.DataFrame({
        "ADX": adx_val,
        "Plus_DI": plus_di,
        "Minus_DI": minus_di,
    })


# ── VWAP (Session-based) ────────────────────────────────────────────────────

def vwap(df: pd.DataFrame) -> pd.Series:
    """Cumulative VWAP (assumes single session in df)."""
    typical = (df["High"] + df["Low"] + df["Close"]) / 3.0
    cum_tp_vol = (typical * df["Volume"]).cumsum()
    cum_vol = df["Volume"].cumsum()
    return cum_tp_vol / cum_vol.replace(0, np.nan)


# ── L2 Order-Book Imbalance (for backtest simulation) ────────────────────────

def bid_ask_imbalance(bid_depth: pd.Series, ask_depth: pd.Series) -> pd.Series:
    """Bid/ask depth imbalance ratio: >1 = buy pressure, <1 = sell pressure."""
    total = bid_depth + ask_depth
    return (bid_depth - ask_depth) / total.replace(0, np.nan)


# ── Supertrend ───────────────────────────────────────────────────────────────

def supertrend(df: pd.DataFrame, period: int = 10, multiplier: float = 3.0) -> pd.DataFrame:
    """Supertrend indicator for trend direction."""
    hl2 = (df["High"] + df["Low"]) / 2.0
    atr_val = atr(df, period)

    upper_band = hl2 + multiplier * atr_val
    lower_band = hl2 - multiplier * atr_val

    direction = pd.Series(1, index=df.index, dtype=int)
    st_val = pd.Series(np.nan, index=df.index)

    for i in range(1, len(df)):
        if df["Close"].iat[i] > upper_band.iat[i - 1]:
            direction.iat[i] = 1
        elif df["Close"].iat[i] < lower_band.iat[i - 1]:
            direction.iat[i] = -1
        else:
            direction.iat[i] = direction.iat[i - 1]

        if direction.iat[i] == 1:
            lower_band.iat[i] = max(lower_band.iat[i], lower_band.iat[i - 1]) if direction.iat[i - 1] == 1 else lower_band.iat[i]
            st_val.iat[i] = lower_band.iat[i]
        else:
            upper_band.iat[i] = min(upper_band.iat[i], upper_band.iat[i - 1]) if direction.iat[i - 1] == -1 else upper_band.iat[i]
            st_val.iat[i] = upper_band.iat[i]

    return pd.DataFrame({
        "Supertrend": st_val,
        "ST_Direction": direction,
    })


# ── Volume Profile / Relative Volume ────────────────────────────────────────

def relative_volume(volume: pd.Series, period: int = 20) -> pd.Series:
    """RVOL: current volume / average volume."""
    avg = volume.rolling(window=period, min_periods=1).mean()
    return volume / avg.replace(0, np.nan)


# ── Stochastic Oscillator ───────────────────────────────────────────────────

def stochastic(df: pd.DataFrame, k_period: int = 14, d_period: int = 3,
               smooth_k: int = 3) -> pd.DataFrame:
    """Stochastic %K and %D — classic cycle oscillator."""
    low_min = df["Low"].rolling(window=k_period, min_periods=k_period).min()
    high_max = df["High"].rolling(window=k_period, min_periods=k_period).max()
    raw_k = 100.0 * (df["Close"] - low_min) / (high_max - low_min).replace(0, np.nan)
    k = raw_k.rolling(window=smooth_k, min_periods=1).mean()
    d = k.rolling(window=d_period, min_periods=1).mean()
    return pd.DataFrame({"Stoch_K": k, "Stoch_D": d})


# ── Keltner Channels ────────────────────────────────────────────────────────

def keltner_channels(df: pd.DataFrame, ema_period: int = 20,
                     atr_period: int = 14, multiplier: float = 1.5) -> pd.DataFrame:
    """Keltner Channels: EMA ± multiplier × ATR. Great for squeeze/expansion."""
    mid = ema(df["Close"], ema_period)
    atr_val = atr(df, atr_period)
    upper = mid + multiplier * atr_val
    lower = mid - multiplier * atr_val
    return pd.DataFrame({"KC_Mid": mid, "KC_Upper": upper, "KC_Lower": lower})


# ── Money Flow Index (MFI) ──────────────────────────────────────────────────

def mfi(df: pd.DataFrame, period: int = 14) -> pd.Series:
    """MFI: RSI weighted by volume — a volume-confirmed oscillator."""
    typical = (df["High"] + df["Low"] + df["Close"]) / 3.0
    raw_money_flow = typical * df["Volume"]
    delta = typical.diff()
    pos_flow = raw_money_flow.where(delta > 0, 0.0)
    neg_flow = raw_money_flow.where(delta < 0, 0.0)
    pos_sum = pos_flow.rolling(window=period, min_periods=period).sum()
    neg_sum = neg_flow.rolling(window=period, min_periods=period).sum()
    money_ratio = pos_sum / neg_sum.replace(0, np.nan)
    return 100.0 - (100.0 / (1.0 + money_ratio))


# ── L1/L2 Proxy Indicators (synthesized from OHLCV) ─────────────────────────
# Real L1/L2 data isn't in historical bars, so we synthesize proxies that
# capture the same microstructure signals a Level-2 screen would reveal.

def spread_proxy(df: pd.DataFrame, period: int = 20) -> pd.DataFrame:
    """
    Spread proxy from bar range vs ATR.
    Narrow bars relative to ATR ≈ tight spread (good fills, institutional interest).
    Wide bars relative to ATR ≈ wide spread (thin book, avoid).

    Returns:
      Spread_Ratio:  bar_range / ATR  (< 0.5 = tight, > 1.5 = wide)
      Spread_Z:      z-score of spread ratio (negative = unusually tight = good L2)
    """
    bar_range = df["High"] - df["Low"]
    atr_val = atr(df, period)
    ratio = bar_range / atr_val.replace(0, np.nan)
    ratio_mean = ratio.rolling(window=period, min_periods=1).mean()
    ratio_std = ratio.rolling(window=period, min_periods=1).std().replace(0, np.nan)
    z_score = (ratio - ratio_mean) / ratio_std
    return pd.DataFrame({"Spread_Ratio": ratio, "Spread_Z": z_score})


def volume_delta_proxy(df: pd.DataFrame) -> pd.Series:
    """
    Volume delta proxy: estimates net buying/selling pressure.
    Uses (Close - Low) / (High - Low) × Volume as buy volume fraction.
    Range: -1.0 (all selling) to +1.0 (all buying).
    """
    bar_range = (df["High"] - df["Low"]).replace(0, np.nan)
    buy_frac = (df["Close"] - df["Low"]) / bar_range
    sell_frac = (df["High"] - df["Close"]) / bar_range
    delta = (buy_frac - sell_frac) * df["Volume"]
    # Normalize to [-1, 1]
    max_vol = df["Volume"].replace(0, np.nan)
    return delta / max_vol


def order_flow_imbalance(df: pd.DataFrame, period: int = 10) -> pd.DataFrame:
    """
    L2-proxy order flow imbalance using cumulative volume delta.
    Positive cumulative delta = net buying pressure (bid side strong).
    Negative = net selling pressure (ask side strong).

    Returns:
      OFI_Raw:    raw volume delta per bar
      OFI_Cum:    cumulative over period
      OFI_Signal: smoothed signal (EMA of cumulative)
    """
    raw = volume_delta_proxy(df)
    cum = raw.rolling(window=period, min_periods=1).sum()
    sig = ema(cum, period)
    return pd.DataFrame({"OFI_Raw": raw, "OFI_Cum": cum, "OFI_Signal": sig})


def volume_acceleration(df: pd.DataFrame, period: int = 5) -> pd.Series:
    """
    Volume acceleration: rate of change of volume.
    Positive = volume surging (L2 absorbing), good for entries.
    """
    vol_ma = df["Volume"].rolling(window=period, min_periods=1).mean()
    return (df["Volume"] - vol_ma) / vol_ma.replace(0, np.nan)


def l2_liquidity_score(df: pd.DataFrame, period: int = 20) -> pd.Series:
    """
    L2 liquidity proxy: combines spread tightness + volume consistency.
    Higher score = better L2 (tighter spread, more consistent volume).
    Range: roughly 0–100.
    """
    # Spread component: inverse spread ratio (tighter = higher score)
    bar_range = df["High"] - df["Low"]
    atr_val = atr(df, period)
    spread_ratio = bar_range / atr_val.replace(0, np.nan)
    spread_score = (1.0 / spread_ratio.replace(0, np.nan)).clip(0, 3) * 33.3

    # Volume consistency: lower CV = more consistent = better L2
    vol_mean = df["Volume"].rolling(window=period, min_periods=1).mean()
    vol_std = df["Volume"].rolling(window=period, min_periods=1).std()
    vol_cv = vol_std / vol_mean.replace(0, np.nan)
    vol_score = (1.0 / vol_cv.replace(0, np.nan)).clip(0, 3) * 33.3

    # RVOL component: higher relative volume = more participants
    rvol = relative_volume(df["Volume"], period)
    rvol_score = rvol.clip(0, 3) * 33.3 / 3.0

    return (spread_score + vol_score + rvol_score).clip(0, 100)


# ── Williams %R ──────────────────────────────────────────────────────────────

def williams_r(df: pd.DataFrame, period: int = 14) -> pd.Series:
    """Williams %R: -100 to 0. < -80 = oversold, > -20 = overbought."""
    high_max = df["High"].rolling(window=period, min_periods=period).max()
    low_min = df["Low"].rolling(window=period, min_periods=period).min()
    return -100.0 * (high_max - df["Close"]) / (high_max - low_min).replace(0, np.nan)


# ── Donchian Channels ───────────────────────────────────────────────────────

def donchian_channels(df: pd.DataFrame, period: int = 20) -> pd.DataFrame:
    """Donchian Channels: highest high / lowest low. Great for breakout/cycle."""
    upper = df["High"].rolling(window=period, min_periods=period).max()
    lower = df["Low"].rolling(window=period, min_periods=period).min()
    mid = (upper + lower) / 2.0
    pct = (df["Close"] - lower) / (upper - lower).replace(0, np.nan)
    return pd.DataFrame({"DC_Upper": upper, "DC_Lower": lower, "DC_Mid": mid, "DC_Pct": pct})


# ── Cycle Detection: Detrended Price Oscillator ─────────────────────────────

def dpo(series: pd.Series, period: int = 20) -> pd.Series:
    """Detrended Price Oscillator — isolates price cycles from trend."""
    shifted_sma = sma(series, period).shift(period // 2 + 1)
    return series - shifted_sma


# ── Helper: Enrich a DataFrame with all indicators ──────────────────────────

def enrich_with_indicators(df: pd.DataFrame) -> pd.DataFrame:
    """Add all standard indicators to an OHLCV DataFrame."""
    out = df.copy()

    # Moving Averages
    out["EMA_9"] = ema(out["Close"], 9)
    out["EMA_21"] = ema(out["Close"], 21)
    out["EMA_50"] = ema(out["Close"], 50)
    out["SMA_20"] = sma(out["Close"], 20)
    out["SMA_200"] = sma(out["Close"], 200)

    # ATR
    out["ATR_14"] = atr(out, 14)

    # RSI
    out["RSI_14"] = rsi(out["Close"], 14)

    # MACD
    macd_df = macd(out["Close"])
    out["MACD"] = macd_df["MACD"]
    out["MACD_Signal"] = macd_df["Signal"]
    out["MACD_Hist"] = macd_df["Histogram"]

    # Bollinger Bands
    bb = bollinger_bands(out["Close"])
    for col in bb.columns:
        out[col] = bb[col]

    # ADX
    adx_df = adx(out)
    for col in adx_df.columns:
        out[col] = adx_df[col]

    # Supertrend
    st = supertrend(out)
    out["Supertrend"] = st["Supertrend"]
    out["ST_Direction"] = st["ST_Direction"]

    # RVOL
    out["RVOL"] = relative_volume(out["Volume"])

    # VWAP
    out["VWAP"] = vwap(out)

    # ── V1.1 additions ───────────────────────────────────────────────────

    # Stochastic
    stoch = stochastic(out)
    out["Stoch_K"] = stoch["Stoch_K"]
    out["Stoch_D"] = stoch["Stoch_D"]

    # Keltner Channels
    kc = keltner_channels(out)
    out["KC_Mid"] = kc["KC_Mid"]
    out["KC_Upper"] = kc["KC_Upper"]
    out["KC_Lower"] = kc["KC_Lower"]

    # MFI
    out["MFI_14"] = mfi(out)

    # L2 proxy: Order Flow Imbalance
    ofi = order_flow_imbalance(out)
    out["OFI_Raw"] = ofi["OFI_Raw"]
    out["OFI_Cum"] = ofi["OFI_Cum"]
    out["OFI_Signal"] = ofi["OFI_Signal"]

    # L2 proxy: Spread analysis
    sp = spread_proxy(out)
    out["Spread_Ratio"] = sp["Spread_Ratio"]
    out["Spread_Z"] = sp["Spread_Z"]

    # L2 proxy: Volume acceleration
    out["Vol_Accel"] = volume_acceleration(out)

    # L2 proxy: Liquidity score
    out["L2_Liquidity"] = l2_liquidity_score(out)

    # Williams %R
    out["WillR_14"] = williams_r(out)

    # Donchian Channels
    dc = donchian_channels(out)
    out["DC_Upper"] = dc["DC_Upper"]
    out["DC_Lower"] = dc["DC_Lower"]
    out["DC_Mid"] = dc["DC_Mid"]
    out["DC_Pct"] = dc["DC_Pct"]

    # DPO (Detrended Price Oscillator)
    out["DPO_20"] = dpo(out["Close"], 20)

    return out
