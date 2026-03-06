namespace Harvester.App.Strategy;

public sealed record ReplayBenchmarkPoint(
    DateTime TimestampUtc,
    double BenchmarkPrice,
    double BenchmarkEquity,
    double BenchmarkReturn
);

public sealed record ReplayPerformancePacketRow(
    DateTime TimestampUtc,
    double Equity,
    double PeriodReturn,
    double CumulativeReturn,
    double Drawdown,
    double BenchmarkReturn,
    double Alpha,
    double RealizedPnl,
    double UnrealizedPnl,
    double Cash
);

public sealed record ReplayPerformanceSummaryRow(
    int SliceCount,
    int FillCount,
    double InitialEquity,
    double FinalEquity,
    double TotalReturn,
    double BenchmarkTotalReturn,
    double AlphaTotal,
    double MaxDrawdown,
    double Turnover,
    double WinRate,
    double SharpeLike
);

public sealed record ReplayPerformanceResult(
    IReadOnlyList<ReplayBenchmarkPoint> Benchmark,
    IReadOnlyList<ReplayPerformancePacketRow> Packets,
    ReplayPerformanceSummaryRow Summary
);

public sealed class ReplayPerformanceAnalyzer
{
    public ReplayPerformanceResult Analyze(
        IReadOnlyList<StrategyDataSlice> slices,
        IReadOnlyList<ReplayFillRow> fills,
        IReadOnlyList<ReplayPortfolioRow> portfolioRows,
        double initialCash)
    {
        if (slices.Count == 0 || portfolioRows.Count == 0)
        {
            return new ReplayPerformanceResult([], [], new ReplayPerformanceSummaryRow(0, 0, initialCash, initialCash, 0, 0, 0, 0, 0, 0, 0));
        }

        var benchmark = BuildBenchmark(slices, initialCash);
        var packets = BuildPackets(portfolioRows, benchmark, initialCash);
        var summary = BuildSummary(fills, portfolioRows, benchmark, packets, initialCash);
        return new ReplayPerformanceResult(benchmark, packets, summary);
    }

    private static IReadOnlyList<ReplayBenchmarkPoint> BuildBenchmark(IReadOnlyList<StrategyDataSlice> slices, double initialCash)
    {
        var firstPrice = slices.FirstOrDefault()?.HistoricalBars.FirstOrDefault()?.Close
            ?? slices.FirstOrDefault()?.TopTicks.FirstOrDefault()?.Price
            ?? 0;

        if (firstPrice <= 0)
        {
            return slices.Select(s => new ReplayBenchmarkPoint(s.TimestampUtc, 0, initialCash, 0)).ToArray();
        }

        return slices
            .Select(slice =>
            {
                var price = slice.HistoricalBars.FirstOrDefault()?.Close
                    ?? slice.TopTicks.FirstOrDefault()?.Price
                    ?? firstPrice;
                var relative = price / firstPrice;
                var equity = initialCash * relative;
                var benchmarkReturn = relative - 1;
                return new ReplayBenchmarkPoint(slice.TimestampUtc, price, equity, benchmarkReturn);
            })
            .ToArray();
    }

    private static IReadOnlyList<ReplayPerformancePacketRow> BuildPackets(
        IReadOnlyList<ReplayPortfolioRow> portfolioRows,
        IReadOnlyList<ReplayBenchmarkPoint> benchmark,
        double initialCash)
    {
        var benchmarkByTs = benchmark.ToDictionary(x => x.TimestampUtc, x => x.BenchmarkReturn);
        var packets = new List<ReplayPerformancePacketRow>(portfolioRows.Count);

        var priorEquity = initialCash;
        var highWater = Math.Max(1e-9, initialCash);

        foreach (var row in portfolioRows.OrderBy(x => x.TimestampUtc))
        {
            var equity = Math.Max(1e-9, row.Equity);
            var periodReturn = (equity - priorEquity) / priorEquity;
            var cumulativeReturn = (equity - initialCash) / initialCash;

            highWater = Math.Max(highWater, equity);
            var drawdown = highWater <= 0 ? 0 : (equity - highWater) / highWater;

            var benchmarkReturn = benchmarkByTs.TryGetValue(row.TimestampUtc, out var bmk) ? bmk : 0;
            var alpha = cumulativeReturn - benchmarkReturn;

            packets.Add(new ReplayPerformancePacketRow(
                row.TimestampUtc,
                equity,
                periodReturn,
                cumulativeReturn,
                drawdown,
                benchmarkReturn,
                alpha,
                row.RealizedPnl,
                row.UnrealizedPnl,
                row.Cash));

            priorEquity = equity;
        }

        return packets;
    }

    private static ReplayPerformanceSummaryRow BuildSummary(
        IReadOnlyList<ReplayFillRow> fills,
        IReadOnlyList<ReplayPortfolioRow> portfolioRows,
        IReadOnlyList<ReplayBenchmarkPoint> benchmark,
        IReadOnlyList<ReplayPerformancePacketRow> packets,
        double initialCash)
    {
        var finalEquity = portfolioRows.Last().Equity;
        var totalReturn = initialCash <= 0 ? 0 : (finalEquity - initialCash) / initialCash;
        var benchmarkTotalReturn = benchmark.Count == 0 ? 0 : benchmark.Last().BenchmarkReturn;
        var alphaTotal = totalReturn - benchmarkTotalReturn;

        var maxDrawdown = packets.Count == 0 ? 0 : packets.Min(x => x.Drawdown);

        var tradedNotional = fills.Sum(f => Math.Abs(f.Quantity * f.FillPrice));
        var avgEquity = Math.Max(1e-9, portfolioRows.Average(x => Math.Abs(x.Equity)));
        var turnover = tradedNotional / avgEquity;

        var winning = fills.Count(f => f.RealizedPnlDelta > 0);
        var winRate = fills.Count == 0 ? 0 : (double)winning / fills.Count;

        var periodReturns = packets.Select(x => x.PeriodReturn).ToArray();
        var mean = periodReturns.Length == 0 ? 0 : periodReturns.Average();
        var stdDev = periodReturns.Length < 2
            ? 0
            : Math.Sqrt(periodReturns.Sum(r => Math.Pow(r - mean, 2)) / (periodReturns.Length - 1));
        var sharpeLike = stdDev <= 1e-9 ? 0 : mean / stdDev * Math.Sqrt(252);

        return new ReplayPerformanceSummaryRow(
            portfolioRows.Count,
            fills.Count,
            initialCash,
            finalEquity,
            totalReturn,
            benchmarkTotalReturn,
            alphaTotal,
            maxDrawdown,
            turnover,
            winRate,
            sharpeLike);
    }
}
