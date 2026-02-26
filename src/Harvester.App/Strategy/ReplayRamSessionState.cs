using System.Text.Json;
using System.Text.Json.Serialization;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record ReplayMicrostructureBucketRow(
    DateTime TimestampUtc,
    double MarkPrice,
    double Spread,
    double L2ImbalanceTopN,
    double BidTopSize,
    double AskTopSize,
    double TapeBuyVolume,
    double TapeSellVolume,
    double VolatilityProxy,
    IReadOnlyList<string> StrategyGateCodes
);

public sealed record ReplayTradeEpisodeRow(
    string TradeId,
    string Symbol,
    string Side,
    double Quantity,
    ReplayTradePoint Entry,
    ReplayTradePoint Exit,
    IReadOnlyList<ReplayTradeFillPoint> Fills,
    IReadOnlyList<ReplayMicrostructureBucketRow> FeaturesPre,
    IReadOnlyList<ReplayMicrostructureBucketRow> Series,
    ReplayTradeLabels Labels,
    ReplayTradeDecisionTrace DecisionTrace
);

public sealed record ReplayTradePoint(
    DateTime TimestampUtc,
    double Price
);

public sealed record ReplayTradeFillPoint(
    DateTime TimestampUtc,
    double Price,
    double Quantity,
    string Side,
    string Source
);

public sealed record ReplayTradeLabels(
    double PnlUsd,
    double RMultiple,
    double MaeUsd,
    double MfeUsd,
    string ExitReason,
    string WinLoss
);

public sealed record ReplayTradeDecisionTrace(
    string EntryReason,
    string ExitReason,
    IReadOnlyDictionary<string, double> RiskModel,
    IReadOnlyList<string> GateCodes
);

public sealed class ReplayRamSessionState
{
    private readonly int _maxBarsPerSymbol;
    private readonly int _maxBucketSeconds;
    private readonly int _imbalanceTopN;
    private readonly Dictionary<string, Queue<HistoricalBarRow>> _bars1mBySymbol;
    private readonly Dictionary<string, OrderBookState> _bookBySymbol;
    private readonly Dictionary<string, Queue<ReplayMicrostructureBucketRow>> _bucketsBySymbol;
    private readonly Dictionary<string, MutableBucket> _currentBucketBySymbol;
    private readonly Dictionary<string, double> _lastTradePriceBySymbol;

    public ReplayRamSessionState(int maxBarsPerSymbol = 2000, int maxBucketMinutes = 60, int imbalanceTopN = 5)
    {
        _maxBarsPerSymbol = Math.Max(100, maxBarsPerSymbol);
        _maxBucketSeconds = Math.Max(300, maxBucketMinutes * 60);
        _imbalanceTopN = Math.Max(1, imbalanceTopN);
        _bars1mBySymbol = new(StringComparer.OrdinalIgnoreCase);
        _bookBySymbol = new(StringComparer.OrdinalIgnoreCase);
        _bucketsBySymbol = new(StringComparer.OrdinalIgnoreCase);
        _currentBucketBySymbol = new(StringComparer.OrdinalIgnoreCase);
        _lastTradePriceBySymbol = new(StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateSlice(string symbol, StrategyDataSlice slice, IReadOnlyList<string> gateCodes)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        foreach (var bar in slice.HistoricalBars)
        {
            if (bar.TimestampUtc == default || bar.Close <= 0)
            {
                continue;
            }

            if (!_bars1mBySymbol.TryGetValue(normalized, out var bars))
            {
                bars = new Queue<HistoricalBarRow>();
                _bars1mBySymbol[normalized] = bars;
            }

            bars.Enqueue(bar);
            while (bars.Count > _maxBarsPerSymbol)
            {
                bars.Dequeue();
            }
        }

        if (!_bookBySymbol.TryGetValue(normalized, out var book))
        {
            book = new OrderBookState();
            _bookBySymbol[normalized] = book;
        }

        foreach (var depth in slice.DepthRows)
        {
            ApplyBookUpdate(book, depth);
        }

        var timestampUtc = DateTime.SpecifyKind(slice.TimestampUtc, DateTimeKind.Utc);
        var bucketTs = new DateTime(timestampUtc.Year, timestampUtc.Month, timestampUtc.Day, timestampUtc.Hour, timestampUtc.Minute, timestampUtc.Second, DateTimeKind.Utc);

        if (!_currentBucketBySymbol.TryGetValue(normalized, out var current) || current.TimestampUtc != bucketTs)
        {
            FlushCurrentBucket(normalized);
            current = new MutableBucket(bucketTs);
            _currentBucketBySymbol[normalized] = current;
        }

        var mark = ResolveMarkPrice(slice, book);
        if (mark > 0)
        {
            if (current.LastMarkPrice > 0)
            {
                var ret = Math.Abs((mark - current.LastMarkPrice) / current.LastMarkPrice);
                current.VolatilityProxy = Math.Max(current.VolatilityProxy, ret);
            }

            current.MarkPrice = mark;
            current.LastMarkPrice = mark;
        }

        var bestBid = GetBestBidPrice(book, slice);
        var bestAsk = GetBestAskPrice(book, slice);
        if (bestBid > 0 && bestAsk > 0 && bestAsk >= bestBid)
        {
            current.Spread = bestAsk - bestBid;
        }

        current.BidTopSize = GetTopSideSize(book.Bids, _imbalanceTopN);
        current.AskTopSize = GetTopSideSize(book.Asks, _imbalanceTopN);
        var denom = current.BidTopSize + current.AskTopSize;
        current.L2ImbalanceTopN = denom > 0 ? (current.BidTopSize - current.AskTopSize) / denom : 0;

        foreach (var tick in slice.TopTicks.Where(x => x.Field == 4 && x.Price > 0))
        {
            var lastTrade = tick.Price;
            var tradeSize = Math.Max(0, tick.Size);

            if (!_lastTradePriceBySymbol.TryGetValue(normalized, out var prevTrade) || prevTrade <= 0)
            {
                _lastTradePriceBySymbol[normalized] = lastTrade;
                continue;
            }

            if (lastTrade >= prevTrade)
            {
                current.TapeBuyVolume += tradeSize;
            }
            else
            {
                current.TapeSellVolume += tradeSize;
            }

            _lastTradePriceBySymbol[normalized] = lastTrade;
        }

        foreach (var code in gateCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            current.GateCodes.Add(code);
        }
    }

    public IReadOnlyList<HistoricalBarRow> GetBars1m(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (!_bars1mBySymbol.TryGetValue(normalized, out var bars))
        {
            return [];
        }

        return bars.ToArray();
    }

    public IReadOnlyList<ReplayMicrostructureBucketRow> GetBuckets(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        FlushCurrentBucket(normalized);
        if (!_bucketsBySymbol.TryGetValue(normalized, out var buckets))
        {
            return [];
        }

        return buckets.ToArray();
    }

    private void FlushCurrentBucket(string symbol)
    {
        if (!_currentBucketBySymbol.TryGetValue(symbol, out var current))
        {
            return;
        }

        if (!_bucketsBySymbol.TryGetValue(symbol, out var buckets))
        {
            buckets = new Queue<ReplayMicrostructureBucketRow>();
            _bucketsBySymbol[symbol] = buckets;
        }

        buckets.Enqueue(new ReplayMicrostructureBucketRow(
            current.TimestampUtc,
            current.MarkPrice,
            current.Spread,
            current.L2ImbalanceTopN,
            current.BidTopSize,
            current.AskTopSize,
            current.TapeBuyVolume,
            current.TapeSellVolume,
            current.VolatilityProxy,
            current.GateCodes.ToArray()));

        while (buckets.Count > _maxBucketSeconds)
        {
            buckets.Dequeue();
        }

        _currentBucketBySymbol.Remove(symbol);
    }

    private static string NormalizeSymbol(string symbol)
    {
        return (symbol ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static double ResolveMarkPrice(StrategyDataSlice slice, OrderBookState book)
    {
        var last = slice.TopTicks
            .Where(x => x.Field == 4 && x.Price > 0)
            .Select(x => x.Price)
            .LastOrDefault();
        if (last > 0)
        {
            return last;
        }

        var bid = GetBestBidPrice(book, slice);
        var ask = GetBestAskPrice(book, slice);
        if (bid > 0 && ask > 0)
        {
            return (bid + ask) / 2.0;
        }

        return slice.HistoricalBars.LastOrDefault()?.Close ?? 0;
    }

    private static double GetBestBidPrice(OrderBookState book, StrategyDataSlice slice)
    {
        var bookBid = book.Bids.Where(x => x.Price > 0 && x.Size > 0).Select(x => x.Price).FirstOrDefault();
        if (bookBid > 0)
        {
            return bookBid;
        }

        return slice.TopTicks.Where(x => x.Field == 1 && x.Price > 0).Select(x => x.Price).LastOrDefault();
    }

    private static double GetBestAskPrice(OrderBookState book, StrategyDataSlice slice)
    {
        var bookAsk = book.Asks.Where(x => x.Price > 0 && x.Size > 0).Select(x => x.Price).FirstOrDefault();
        if (bookAsk > 0)
        {
            return bookAsk;
        }

        return slice.TopTicks.Where(x => x.Field == 2 && x.Price > 0).Select(x => x.Price).LastOrDefault();
    }

    private static double GetTopSideSize(List<BookLevel> levels, int topN)
    {
        return levels
            .Where(x => x.Price > 0 && x.Size > 0)
            .Take(topN)
            .Sum(x => Math.Max(0, x.Size));
    }

    private static void ApplyBookUpdate(OrderBookState book, DepthRow row)
    {
        var levels = row.Side == 1 ? book.Bids : book.Asks;
        var position = Math.Max(0, row.Position);

        if (row.Operation == 2)
        {
            if (position < levels.Count)
            {
                levels.RemoveAt(position);
            }

            return;
        }

        var level = new BookLevel(row.Price, row.Size);

        if (row.Operation == 0)
        {
            if (position <= levels.Count)
            {
                levels.Insert(position, level);
            }
            else
            {
                while (levels.Count < position)
                {
                    levels.Add(new BookLevel(0, 0));
                }

                levels.Add(level);
            }

            return;
        }

        if (position < levels.Count)
        {
            levels[position] = level;
            return;
        }

        while (levels.Count <= position)
        {
            levels.Add(new BookLevel(0, 0));
        }

        levels[position] = level;
    }

    private sealed class MutableBucket
    {
        public MutableBucket(DateTime timestampUtc)
        {
            TimestampUtc = timestampUtc;
            GateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public DateTime TimestampUtc { get; }
        public double MarkPrice { get; set; }
        public double LastMarkPrice { get; set; }
        public double Spread { get; set; }
        public double L2ImbalanceTopN { get; set; }
        public double BidTopSize { get; set; }
        public double AskTopSize { get; set; }
        public double TapeBuyVolume { get; set; }
        public double TapeSellVolume { get; set; }
        public double VolatilityProxy { get; set; }
        public HashSet<string> GateCodes { get; }
    }

    private sealed class OrderBookState
    {
        public List<BookLevel> Bids { get; } = [];
        public List<BookLevel> Asks { get; } = [];
    }

    private sealed record BookLevel(double Price, int Size);
}

public sealed class ReplayTradeEpisodeRecorder
{
    private readonly string _episodeRootDirectory;
    private readonly Dictionary<string, ActiveTradeState> _activeTradeBySymbol;
    private readonly bool _emitEpochMilliseconds;
    private readonly bool _emitSeriesJsonl;
    private int _tradeSequence;

    public ReplayTradeEpisodeRecorder(string episodeRootDirectory)
    {
        _episodeRootDirectory = episodeRootDirectory;
        _activeTradeBySymbol = new Dictionary<string, ActiveTradeState>(StringComparer.OrdinalIgnoreCase);
        _emitEpochMilliseconds = ReadBoolEnvironmentVariable("HARVESTER_EPISODE_EPOCH_MS");
        _emitSeriesJsonl = ReadBoolEnvironmentVariable("HARVESTER_EPISODE_SERIES_JSONL");
        _tradeSequence = 0;
    }

    public void ProcessSlice(
        string symbol,
        ReplaySliceSimulationResult result,
        IReadOnlyList<ReplayMicrostructureBucketRow> buckets)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var fillsForSymbol = result.Fills
            .Where(x => string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.TimestampUtc)
            .ToArray();

        var prevPosition = _activeTradeBySymbol.TryGetValue(normalized, out var state)
            ? state.PositionQuantity
            : 0.0;
        var nextPosition = result.Portfolio.PositionQuantity;

        if (Math.Abs(prevPosition) <= 1e-9 && Math.Abs(nextPosition) > 1e-9)
        {
            var entryFill = fillsForSymbol.FirstOrDefault();
            if (entryFill is not null)
            {
                var entryPrice = entryFill.FillPrice > 0 ? entryFill.FillPrice : result.Portfolio.MarketPrice;
                var entryTs = entryFill.TimestampUtc == default ? result.Portfolio.TimestampUtc : entryFill.TimestampUtc;
                var side = nextPosition > 0 ? "LONG" : "SHORT";
                var quantity = Math.Abs(nextPosition);

                var featuresPreWindowStart = entryTs.AddSeconds(-120);
                var featuresPreWindowEnd = entryTs.AddSeconds(-30);
                var featuresPre = buckets
                    .Where(x => x.TimestampUtc >= featuresPreWindowStart && x.TimestampUtc <= featuresPreWindowEnd)
                    .OrderBy(x => x.TimestampUtc)
                    .ToArray();

                if (featuresPre.Length == 0)
                {
                    featuresPre = buckets
                        .Where(x => x.TimestampUtc <= entryTs)
                        .OrderByDescending(x => x.TimestampUtc)
                        .Take(120)
                        .OrderBy(x => x.TimestampUtc)
                        .ToArray();
                }

                _activeTradeBySymbol[normalized] = new ActiveTradeState(
                    EntryTimestampUtc: entryTs,
                    EntryPrice: entryPrice,
                    Side: side,
                    Quantity: quantity,
                    PositionQuantity: nextPosition,
                    EntryReason: entryFill.Source,
                    FeaturesPre: featuresPre,
                    RecordedFills: fillsForSymbol
                        .Select(x => new ReplayTradeFillPoint(x.TimestampUtc, x.FillPrice, x.Quantity, x.Side, x.Source))
                        .ToList());
            }

            return;
        }

        if (Math.Abs(nextPosition) > 1e-9)
        {
            if (_activeTradeBySymbol.TryGetValue(normalized, out var activeOpen))
            {
                var mergedFills = activeOpen.RecordedFills.ToList();
                mergedFills.AddRange(fillsForSymbol
                    .Select(x => new ReplayTradeFillPoint(x.TimestampUtc, x.FillPrice, x.Quantity, x.Side, x.Source)));
                _activeTradeBySymbol[normalized] = activeOpen with
                {
                    PositionQuantity = nextPosition,
                    RecordedFills = mergedFills
                };
            }

            return;
        }

        if (Math.Abs(prevPosition) > 1e-9 && Math.Abs(nextPosition) <= 1e-9 && _activeTradeBySymbol.TryGetValue(normalized, out var active))
        {
            var closeFill = fillsForSymbol.LastOrDefault();
            var exitTs = closeFill?.TimestampUtc ?? result.Portfolio.TimestampUtc;
            var exitPrice = closeFill?.FillPrice ?? result.Portfolio.MarketPrice;

            var mergedFills = active.RecordedFills.ToList();
            mergedFills.AddRange(fillsForSymbol
                .Select(x => new ReplayTradeFillPoint(x.TimestampUtc, x.FillPrice, x.Quantity, x.Side, x.Source)));

            var during = buckets
                .Where(x => x.TimestampUtc >= active.EntryTimestampUtc && x.TimestampUtc <= exitTs)
                .OrderBy(x => x.TimestampUtc)
                .ToArray();

            var pnl = fillsForSymbol.Sum(x => x.RealizedPnlDelta);
            var sideSign = string.Equals(active.Side, "LONG", StringComparison.OrdinalIgnoreCase) ? 1.0 : -1.0;
            var qty = Math.Max(1e-9, active.Quantity);
            var mfe = 0.0;
            var mae = 0.0;

            foreach (var bucket in during)
            {
                if (bucket.MarkPrice <= 0 || active.EntryPrice <= 0)
                {
                    continue;
                }

                var move = (bucket.MarkPrice - active.EntryPrice) * sideSign * qty;
                if (move > mfe)
                {
                    mfe = move;
                }
                if (move < mae)
                {
                    mae = move;
                }
            }

            var riskUnit = Math.Max(1.0, Math.Abs(active.EntryPrice) * qty * 0.0025);
            var rMultiple = pnl / riskUnit;
            var exitReason = closeFill?.Source ?? "POSITION_CLOSED";
            var winLoss = pnl switch
            {
                > 0.005 => "WIN",
                < -0.005 => "LOSS",
                _ => "BREAKEVEN"
            };
            var gateCodes = during
                .SelectMany(x => x.StrategyGateCodes)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var tradeId = BuildTradeId(exitTs);
            var episode = new ReplayTradeEpisodeRow(
                TradeId: tradeId,
                Symbol: normalized,
                Side: active.Side,
                Quantity: active.Quantity,
                Entry: new ReplayTradePoint(active.EntryTimestampUtc, active.EntryPrice),
                Exit: new ReplayTradePoint(exitTs, exitPrice),
                Fills: mergedFills,
                FeaturesPre: active.FeaturesPre,
                Series: during,
                Labels: new ReplayTradeLabels(
                    PnlUsd: pnl,
                    RMultiple: rMultiple,
                    MaeUsd: mae,
                    MfeUsd: mfe,
                    ExitReason: exitReason,
                    WinLoss: winLoss),
                DecisionTrace: new ReplayTradeDecisionTrace(
                    EntryReason: string.IsNullOrWhiteSpace(active.EntryReason) ? "strategy" : active.EntryReason,
                    ExitReason: exitReason,
                    RiskModel: new Dictionary<string, double>
                    {
                        ["risk_per_trade_usd"] = riskUnit,
                        ["entry_price"] = active.EntryPrice,
                        ["quantity"] = active.Quantity
                    },
                    GateCodes: gateCodes));

            WriteEpisode(episode, exitTs);
            _activeTradeBySymbol.Remove(normalized);
        }
    }

    private string BuildTradeId(DateTime timestampUtc)
    {
        _tradeSequence++;
        var ts = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc).ToString("HHmmss");
        return $"T{ts}_{_tradeSequence:000}";
    }

    private void WriteEpisode(ReplayTradeEpisodeRow episode, DateTime timestampUtc)
    {
        var day = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd");
        var symbolDir = Path.Combine(_episodeRootDirectory, day, episode.Symbol);
        Directory.CreateDirectory(symbolDir);

        var filePath = Path.Combine(symbolDir, $"{episode.TradeId}.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        if (_emitEpochMilliseconds)
        {
            options.Converters.Add(new UnixEpochMillisecondsDateTimeConverter());
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(episode, options));

        if (_emitSeriesJsonl)
        {
            WriteSeriesJsonl(episode, symbolDir);
        }
    }

    private void WriteSeriesJsonl(ReplayTradeEpisodeRow episode, string symbolDir)
    {
        var jsonlPath = Path.Combine(symbolDir, $"{episode.TradeId}.series.jsonl");
        var options = new JsonSerializerOptions();
        if (_emitEpochMilliseconds)
        {
            options.Converters.Add(new UnixEpochMillisecondsDateTimeConverter());
        }

        using var writer = new StreamWriter(jsonlPath, append: false);
        foreach (var bucket in episode.Series)
        {
            object timestamp = _emitEpochMilliseconds
                ? new DateTimeOffset(DateTime.SpecifyKind(bucket.TimestampUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
                : bucket.TimestampUtc;

            var row = new
            {
                trade_id = episode.TradeId,
                symbol = episode.Symbol,
                ts = timestamp,
                mark = bucket.MarkPrice,
                spread = bucket.Spread,
                imb = bucket.L2ImbalanceTopN,
                tape_buy = bucket.TapeBuyVolume,
                tape_sell = bucket.TapeSellVolume,
                vol = bucket.VolatilityProxy,
                gate_codes = bucket.StrategyGateCodes
            };

            writer.WriteLine(JsonSerializer.Serialize(row, options));
        }
    }

    private static bool ReadBoolEnvironmentVariable(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private sealed class UnixEpochMillisecondsDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var unixMs))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
            }

            if (reader.TokenType == JsonTokenType.String && reader.TryGetDateTime(out var timestamp))
            {
                return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            }

            throw new JsonException("Invalid timestamp format for DateTime.");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            writer.WriteNumberValue(new DateTimeOffset(utc).ToUnixTimeMilliseconds());
        }
    }

    private sealed record ActiveTradeState(
        DateTime EntryTimestampUtc,
        double EntryPrice,
        string Side,
        double Quantity,
        double PositionQuantity,
        string EntryReason,
        IReadOnlyList<ReplayMicrostructureBucketRow> FeaturesPre,
        List<ReplayTradeFillPoint> RecordedFills
    );
}
