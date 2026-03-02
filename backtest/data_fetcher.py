"""
data_fetcher.py — Fetch multi-timeframe OHLCV bars from IBKR TWS for backtesting.
Connects on port 7497 (paper trading), fetches 30s/1m/5m/15m/1h/1D bars,
and saves them as CSV in backtest/data/.
"""
from __future__ import annotations

import asyncio
import os
import sys
import time
from datetime import datetime, timedelta
from pathlib import Path

import pandas as pd

# Python 3.14 ib_insync workaround
if sys.version_info >= (3, 14):
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        asyncio.set_event_loop(asyncio.new_event_loop())

from ib_insync import IB, Stock, util  # noqa: E402

DATA_DIR = Path(__file__).parent / "data"
DATA_DIR.mkdir(exist_ok=True)

# Mapping: our timeframe label → IBKR barSizeSetting, durationStr, whatToShow
TIMEFRAME_CONFIG = {
    "30s": {"barSize": "30 secs", "duration": "2 D", "whatToShow": "TRADES"},
    "1m":  {"barSize": "1 min",   "duration": "5 D", "whatToShow": "TRADES"},
    "5m":  {"barSize": "5 mins",  "duration": "20 D", "whatToShow": "TRADES"},
    "15m": {"barSize": "15 mins", "duration": "40 D", "whatToShow": "TRADES"},
    "1h":  {"barSize": "1 hour",  "duration": "90 D", "whatToShow": "TRADES"},
    "1D":  {"barSize": "1 day",   "duration": "365 D", "whatToShow": "TRADES"},
}


def _bars_to_df(bars) -> pd.DataFrame:
    """Convert ib_insync bars to a standard OHLCV DataFrame."""
    if not bars:
        return pd.DataFrame(columns=["Timestamp", "Open", "High", "Low", "Close", "Volume"])
    rows = []
    for b in bars:
        rows.append({
            "Timestamp": str(b.date),
            "Open": float(b.open),
            "High": float(b.high),
            "Low": float(b.low),
            "Close": float(b.close),
            "Volume": int(b.volume),
        })
    df = pd.DataFrame(rows)
    df["Timestamp"] = pd.to_datetime(df["Timestamp"], utc=True)
    df.set_index("Timestamp", inplace=True)
    df.sort_index(inplace=True)
    return df


def connect_ib(host: str = "127.0.0.1", port: int = 7497, client_id: int = 80) -> IB:
    """Connect to TWS / IB Gateway."""
    ib = IB()
    ib.connect(host, port, clientId=client_id, timeout=20)
    return ib


def fetch_historical_bars(
    ib: IB,
    symbol: str,
    timeframe: str,
    exchange: str = "SMART",
    currency: str = "USD",
    end_date: str = "",
) -> pd.DataFrame:
    """Fetch historical bars for one symbol / timeframe."""
    cfg = TIMEFRAME_CONFIG[timeframe]
    contract = Stock(symbol, exchange, currency)
    ib.qualifyContracts(contract)

    bars = ib.reqHistoricalData(
        contract,
        endDateTime=end_date,
        durationStr=cfg["duration"],
        barSizeSetting=cfg["barSize"],
        whatToShow=cfg["whatToShow"],
        useRTH=True,
        formatDate=1,
    )
    return _bars_to_df(bars)


def fetch_all_timeframes(
    ib: IB,
    symbol: str,
    timeframes: list[str] | None = None,
    exchange: str = "SMART",
    currency: str = "USD",
    pacing_delay: float = 2.0,
) -> dict[str, pd.DataFrame]:
    """Fetch all timeframes for a given symbol, respecting IBKR pacing rules."""
    if timeframes is None:
        timeframes = list(TIMEFRAME_CONFIG.keys())

    result: dict[str, pd.DataFrame] = {}
    for i, tf in enumerate(timeframes):
        print(f"  [{symbol}] Fetching {tf} bars...", end=" ", flush=True)
        df = fetch_historical_bars(ib, symbol, tf, exchange, currency)
        result[tf] = df
        print(f"{len(df)} bars")
        if i < len(timeframes) - 1:
            time.sleep(pacing_delay)  # IBKR pacing: max 60 req/10 min
    return result


def save_data(symbol: str, data: dict[str, pd.DataFrame]) -> Path:
    """Save fetched data as CSVs inside backtest/data/<SYMBOL>/."""
    sym_dir = DATA_DIR / symbol.upper()
    sym_dir.mkdir(exist_ok=True)
    for tf, df in data.items():
        path = sym_dir / f"{tf}.csv"
        df.to_csv(str(path))
        print(f"  Saved {path} ({len(df)} rows)")
    return sym_dir


def load_data(symbol: str, timeframe: str) -> pd.DataFrame:
    """Load previously saved CSV data."""
    path = DATA_DIR / symbol.upper() / f"{timeframe}.csv"
    if not path.exists():
        raise FileNotFoundError(f"No data at {path}")
    df = pd.read_csv(str(path), index_col="Timestamp", parse_dates=True)
    return df


def fetch_and_save(
    symbols: list[str],
    timeframes: list[str] | None = None,
    port: int = 7497,
    client_id: int = 80,
) -> dict[str, dict[str, pd.DataFrame]]:
    """Main convenience entry point: connect, fetch, save, disconnect."""
    ib = connect_ib(port=port, client_id=client_id)
    all_data: dict[str, dict[str, pd.DataFrame]] = {}
    try:
        for sym in symbols:
            print(f"\n=== Fetching data for {sym} ===")
            data = fetch_all_timeframes(ib, sym, timeframes)
            save_data(sym, data)
            all_data[sym] = data
    finally:
        ib.disconnect()
    return all_data


# ── CLI entry point ──────────────────────────────────────────────────────────

if __name__ == "__main__":
    # Quick test: fetch a few liquid stocks
    test_symbols = ["AAPL", "TSLA", "NVDA", "AMD", "META"]
    fetch_and_save(test_symbols)
