using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Wrapper;
using Harvester.App.Strategy;
using Harvester.Contracts;
using IBApi;

namespace Harvester.App.IBKR.Runtime;

// Phase 3 #10: Extracted from SnapshotRuntime.cs - Scanner modes
public sealed partial class SnapshotRuntime
{
    private async Task RunScannerExamplesMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        while (_wrapper.ScannerDataRows.TryDequeue(out _))
        {
        }

        const int reqId = 9971;
        var subscription = BuildScannerSubscriptionFromOptions();
        brokerAdapter.RequestScannerSubscription(client, reqId, subscription, Array.Empty<TagValue>(), Array.Empty<TagValue>());

        try
        {
            await AwaitWithTimeout(_wrapper.ScannerDataEndTask, token, "scannerDataEnd");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] Scanner examples timed out waiting for scannerDataEnd; exporting current rows.");
        }

        brokerAdapter.CancelScannerSubscription(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var rowsPath = Path.Combine(outputDir, $"scanner_examples_{timestamp}.json");
        var requestPath = Path.Combine(outputDir, $"scanner_examples_request_{timestamp}.json");

        var requestRow = new[]
        {
            new ScannerRequestRow(
                reqId,
                subscription.Instrument,
                subscription.LocationCode,
                subscription.ScanCode,
                subscription.NumberOfRows,
                _options.ScannerScannerSettingPairs,
                _options.ScannerFilterTagValues,
                _options.ScannerOptionsTagValues
            )
        };

        WriteJson(rowsPath, _wrapper.ScannerDataRows.Where(x => x.RequestId == reqId).ToArray());
        WriteJson(requestPath, requestRow);

        Console.WriteLine($"[OK] Scanner examples export: {rowsPath} (rows={_wrapper.ScannerDataRows.Count(x => x.RequestId == reqId)})");
        Console.WriteLine($"[OK] Scanner examples request export: {requestPath}");
    }

    private async Task RunScannerComplexMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        while (_wrapper.ScannerDataRows.TryDequeue(out _))
        {
        }

        const int reqId = 9972;
        var subscription = BuildScannerSubscriptionFromOptions();
        if (!string.IsNullOrWhiteSpace(_options.ScannerScannerSettingPairs))
        {
            subscription.ScannerSettingPairs = _options.ScannerScannerSettingPairs;
        }

        var filterOptions = ParseTagValuePairs(_options.ScannerFilterTagValues);
        var scannerOptions = ParseTagValuePairs(_options.ScannerOptionsTagValues);

        brokerAdapter.RequestScannerSubscription(client, reqId, subscription, scannerOptions, filterOptions);

        try
        {
            await AwaitWithTimeout(_wrapper.ScannerDataEndTask, token, "scannerDataEnd");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] Scanner complex run timed out waiting for scannerDataEnd; exporting current rows.");
        }

        brokerAdapter.CancelScannerSubscription(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var rowsPath = Path.Combine(outputDir, $"scanner_complex_{timestamp}.json");
        var requestPath = Path.Combine(outputDir, $"scanner_complex_request_{timestamp}.json");

        var requestRow = new[]
        {
            new ScannerRequestRow(
                reqId,
                subscription.Instrument,
                subscription.LocationCode,
                subscription.ScanCode,
                subscription.NumberOfRows,
                subscription.ScannerSettingPairs,
                string.Join(';', filterOptions.Select(x => $"{x.Tag}={x.Value}")),
                string.Join(';', scannerOptions.Select(x => $"{x.Tag}={x.Value}"))
            )
        };

        WriteJson(rowsPath, _wrapper.ScannerDataRows.Where(x => x.RequestId == reqId).ToArray());
        WriteJson(requestPath, requestRow);

        Console.WriteLine($"[OK] Scanner complex export: {rowsPath} (rows={_wrapper.ScannerDataRows.Count(x => x.RequestId == reqId)})");
        Console.WriteLine($"[OK] Scanner complex request export: {requestPath}");
    }

    private async Task RunScannerParametersMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        while (_wrapper.ScannerParametersRows.TryDequeue(out _))
        {
        }

        brokerAdapter.RequestScannerParameters(client);
        try
        {
            await AwaitWithTimeout(_wrapper.ScannerParametersTask, token, "scannerParameters");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] Scanner parameters callback not received before timeout; exporting current rows.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var jsonPath = Path.Combine(outputDir, $"scanner_parameters_{timestamp}.json");
        var xmlPath = Path.Combine(outputDir, $"scanner_parameters_{timestamp}.xml");

        var rows = _wrapper.ScannerParametersRows.ToArray();
        WriteJson(jsonPath, rows);

        var xml = rows.LastOrDefault()?.Xml ?? string.Empty;
        File.WriteAllText(xmlPath, xml);

        Console.WriteLine($"[OK] Scanner parameters JSON export: {jsonPath} (rows={rows.Length})");
        Console.WriteLine($"[OK] Scanner parameters XML export: {xmlPath} (chars={xml.Length})");
    }

    private async Task RunScannerWorkbenchMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        var scanCodes = _options.ScannerWorkbenchCodes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToArray();

        if (scanCodes.Length == 0)
        {
            throw new InvalidOperationException("Scanner workbench requires at least one scan code in --scanner-workbench-codes.");
        }

        var runs = Math.Max(1, _options.ScannerWorkbenchRuns);
        var captureSeconds = Math.Max(1, _options.ScannerWorkbenchCaptureSeconds);
        var minRows = Math.Max(0, _options.ScannerWorkbenchMinRows);
        var filterOptions = ParseTagValuePairs(_options.ScannerFilterTagValues);
        var scannerOptions = ParseTagValuePairs(_options.ScannerOptionsTagValues);

        var runRows = new List<ScannerWorkbenchRunRow>();
        var scoreRows = new List<ScannerWorkbenchScoreRow>();
        var candidateObservationRows = new List<ScannerWorkbenchCandidateObservationRow>();
        var baseReqId = 9980;

        for (var codeIndex = 0; codeIndex < scanCodes.Length; codeIndex++)
        {
            var scanCode = scanCodes[codeIndex];

            for (var runIndex = 1; runIndex <= runs; runIndex++)
            {
                var reqId = baseReqId + (codeIndex * 100) + runIndex;
                var subscription = BuildScannerSubscriptionFromOptions();
                subscription.ScanCode = scanCode;

                var errorCountBefore = _wrapper.Errors.Count;
                var startedUtc = DateTime.UtcNow;
                var stopwatch = Stopwatch.StartNew();

                brokerAdapter.RequestScannerSubscription(client, reqId, subscription, scannerOptions, filterOptions);
                await Task.Delay(TimeSpan.FromSeconds(captureSeconds), token);
                brokerAdapter.CancelScannerSubscription(client, reqId);
                await Task.Delay(TimeSpan.FromMilliseconds(400), token);

                stopwatch.Stop();

                var rowsForReq = _wrapper.ScannerDataRows
                    .Where(x => x.RequestId == reqId)
                    .OrderBy(x => x.TimestampUtc)
                    .ToArray();

                foreach (var scannerRow in rowsForReq)
                {
                    var normalizedSymbol = (scannerRow.Symbol ?? string.Empty).Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(normalizedSymbol))
                    {
                        continue;
                    }

                    candidateObservationRows.Add(new ScannerWorkbenchCandidateObservationRow(
                        scannerRow.TimestampUtc,
                        reqId,
                        scanCode,
                        runIndex,
                        normalizedSymbol,
                        scannerRow.ConId,
                        scannerRow.Rank,
                        scannerRow.Exchange,
                        scannerRow.PrimaryExchange,
                        scannerRow.Currency,
                        scannerRow.Distance,
                        scannerRow.Benchmark,
                        scannerRow.Projection
                    ));
                }

                var newErrors = _wrapper.Errors.Skip(errorCountBefore).ToArray();
                var reqErrors = newErrors.Where(x => x.Contains($"id={reqId}", StringComparison.OrdinalIgnoreCase)).ToArray();
                var reqErrorCodes = reqErrors
                    .Select(ParseObservedError)
                    .Where(x => x is not null)
                    .Select(x => x!.Code)
                    .Distinct()
                    .ToArray();

                var firstRowSeconds = rowsForReq.Length == 0
                    ? (double?)null
                    : Math.Max(0, (rowsForReq[0].TimestampUtc - startedUtc).TotalSeconds);

                runRows.Add(new ScannerWorkbenchRunRow(
                    DateTime.UtcNow,
                    reqId,
                    scanCode,
                    runIndex,
                    rowsForReq.Length,
                    Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
                    firstRowSeconds,
                    reqErrors.Length,
                    string.Join(',', reqErrorCodes)
                ));
            }

            var grouped = runRows.Where(x => x.ScanCode == scanCode).ToArray();
            var averageRows = grouped.Length == 0 ? 0 : grouped.Average(x => x.Rows);
            var averageFirstRowSeconds = grouped
                .Where(x => x.FirstRowSeconds is not null)
                .Select(x => x.FirstRowSeconds!.Value)
                .DefaultIfEmpty(captureSeconds)
                .Average();
            var averageErrors = grouped.Length == 0 ? 0 : grouped.Average(x => x.ErrorCount);

            var nonBlockingCodes = new[] { 162, 365, 420 };
            var successfulRuns = grouped.Count(x => x.Rows >= minRows
                && (string.IsNullOrWhiteSpace(x.ErrorCodes) || x.ErrorCodes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .All(c => int.TryParse(c, out var code) && nonBlockingCodes.Contains(code))));

            var coverage = _options.ScannerRows <= 0
                ? 0
                : Math.Min(100, (averageRows / _options.ScannerRows) * 100);
            var speed = Math.Clamp((1 - (averageFirstRowSeconds / captureSeconds)) * 100, 0, 100);
            var stability = grouped.Length == 0 ? 0 : (successfulRuns * 100.0 / grouped.Length);
            var cleanliness = Math.Clamp(100 - (averageErrors * 25), 0, 100);

            var hardFail = averageRows < minRows
                || grouped.Any(x => x.ErrorCodes.Contains("10337", StringComparison.OrdinalIgnoreCase)
                    || x.ErrorCodes.Contains("321", StringComparison.OrdinalIgnoreCase));

            var weighted = (coverage * 0.40) + (speed * 0.20) + (stability * 0.30) + (cleanliness * 0.10);

            scoreRows.Add(new ScannerWorkbenchScoreRow(
                scanCode,
                runs,
                Math.Round(averageRows, 3),
                Math.Round(averageFirstRowSeconds, 3),
                Math.Round(averageErrors, 3),
                Math.Round(coverage, 3),
                Math.Round(speed, 3),
                Math.Round(stability, 3),
                Math.Round(cleanliness, 3),
                Math.Round(weighted, 3),
                hardFail
            ));
        }

        var ranked = scoreRows
            .OrderBy(x => x.HardFail)
            .ThenByDescending(x => x.WeightedScore)
            .ThenByDescending(x => x.CoverageScore)
            .ToArray();
        var candidateRows = BuildScannerWorkbenchCandidates(candidateObservationRows, scanCodes.Length, runs);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var runsPath = Path.Combine(outputDir, $"scanner_workbench_runs_{timestamp}.json");
        var rankingPath = Path.Combine(outputDir, $"scanner_workbench_ranking_{timestamp}.json");
        var candidatesPath = Path.Combine(outputDir, $"scanner_workbench_candidates_{timestamp}.json");

        WriteJson(runsPath, runRows);
        WriteJson(rankingPath, ranked);
        WriteJson(candidatesPath, candidateRows);

        Console.WriteLine($"[OK] Scanner workbench runs export: {runsPath} (rows={runRows.Count})");
        Console.WriteLine($"[OK] Scanner workbench ranking export: {rankingPath} (rows={ranked.Length})");
        Console.WriteLine($"[OK] Scanner workbench candidates export: {candidatesPath} (rows={candidateRows.Length})");
    }

    private Task RunScannerPreviewMode(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var action = _options.LiveAction;
        var resolvedPath = ResolveLiveScannerInputPath(action);
        var fullPath = string.IsNullOrWhiteSpace(resolvedPath)
            ? string.Empty
            : Path.GetFullPath(resolvedPath);

        var sessionCalendar = new UsEquitiesExchangeCalendarService();
        var hasSession = sessionCalendar.TryGetSessionWindowUtc("US-EQUITIES", nowUtc, out var session);
        var sessionOpenUtc = hasSession && session.IsTradingDay
            ? session.SessionOpenUtc
            : DateTime.MinValue;
        var openPhaseEndUtc = hasSession && session.IsTradingDay
            ? sessionOpenUtc.AddMinutes(_options.LiveScannerOpenPhaseMinutes)
            : DateTime.MinValue;
        var postOpenEndUtc = hasSession && session.IsTradingDay
            ? openPhaseEndUtc.AddMinutes(_options.LiveScannerPostOpenMinutes)
            : DateTime.MinValue;

        var phase = "outside-window";
        if (hasSession && session.IsTradingDay)
        {
            phase = nowUtc < sessionOpenUtc
                ? "pre-open"
                : nowUtc < openPhaseEndUtc
                    ? "open-phase"
                    : nowUtc < postOpenEndUtc
                        ? "post-open"
                        : "post-window";
        }

        var fileConfigured = !string.IsNullOrWhiteSpace(fullPath);
        var fileExists = fileConfigured && File.Exists(fullPath);
        var fileIsTemp = fileExists
            && Path.GetFileName(fullPath).StartsWith("~$", StringComparison.OrdinalIgnoreCase);

        var rawRows = fileExists && !fileIsTemp
            ? LoadLiveScannerCandidateRows(fullPath)
            : [];

        var allowSet = _options.AllowedSymbols
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalizedRows = rawRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => new LiveScannerCandidateRow
            {
                Symbol = x.Symbol.Trim().ToUpperInvariant(),
                WeightedScore = x.WeightedScore,
                Eligible = x.Eligible,
                AverageRank = x.AverageRank
            })
            .ToArray();

        var selectedSymbols = normalizedRows
            .Where(x => x.Eligible is not false)
            .Where(x => x.WeightedScore >= _options.LiveScannerMinScore)
            .Where(x => allowSet.Contains(x.Symbol))
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.AverageRank)
            .Take(Math.Max(1, _options.LiveScannerTopN))
            .Select(x => x.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedCandidates = normalizedRows
            .Where(x => selectedSymbols.Contains(x.Symbol))
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.AverageRank)
            .ToArray();

        var previewCandidates = normalizedRows
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.AverageRank)
            .Select(x => new ScannerPreviewCandidateRow(
                x.Symbol,
                x.WeightedScore,
                x.Eligible,
                x.AverageRank,
                allowSet.Contains(x.Symbol),
                x.Eligible is not false && x.WeightedScore >= _options.LiveScannerMinScore,
                selectedSymbols.Contains(x.Symbol)
            ))
            .ToArray();

        var notes = new List<string>();
        if (!fileConfigured)
        {
            notes.Add("No scanner file configured for current time window and action.");
        }
        else if (!fileExists)
        {
            notes.Add($"Resolved file not found: {fullPath}");
        }
        else if (fileIsTemp)
        {
            notes.Add("Resolved file is an Excel lock/temp file (~$*.xlsx). Use the real workbook file.");
        }

        if (selectedCandidates.Length == 0 && fileExists && !fileIsTemp)
        {
            notes.Add("No candidates passed allow-list, eligibility, and score filters.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var summaryPath = Path.Combine(outputDir, $"scanner_preview_summary_{timestamp}.json");
        var candidatesPath = Path.Combine(outputDir, $"scanner_preview_candidates_{timestamp}.json");

        var summary = new ScannerPreviewSummaryRow(
            DateTime.UtcNow,
            action,
            resolvedPath,
            fullPath,
            fileConfigured,
            fileExists,
            fileIsTemp,
            phase,
            hasSession && session.IsTradingDay,
            sessionOpenUtc,
            openPhaseEndUtc,
            postOpenEndUtc,
            rawRows.Length,
            normalizedRows.Length,
            selectedCandidates.Length,
            selectedCandidates.Select(x => x.Symbol).ToArray(),
            notes.ToArray());

        WriteJson(summaryPath, new[] { summary });
        WriteJson(candidatesPath, previewCandidates);

        Console.WriteLine($"[OK] Scanner preview summary export: {summaryPath} (selected={selectedCandidates.Length})");
        Console.WriteLine($"[OK] Scanner preview candidates export: {candidatesPath} (rows={previewCandidates.Length})");

        return Task.CompletedTask;
    }

    private ScannerWorkbenchCandidateRow[] BuildScannerWorkbenchCandidates(
        IReadOnlyList<ScannerWorkbenchCandidateObservationRow> observationRows,
        int totalScanCodes,
        int totalRuns)
    {
        if (observationRows.Count == 0)
        {
            return [];
        }

        var requiredObservations = Math.Max(1, (int)Math.Ceiling(totalRuns * 0.5));

        return observationRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .GroupBy(x => x.Symbol.Trim().ToUpperInvariant())
            .Select(group =>
            {
                var entries = group.ToArray();
                var observationCount = entries.Length;
                var distinctScanCodes = entries
                    .Select(x => x.ScanCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var averageRank = entries.Average(x => x.Rank);
                var bestRank = entries.Min(x => x.Rank);

                var coverageScore = totalScanCodes <= 0
                    ? 0
                    : Math.Clamp((distinctScanCodes * 100.0) / totalScanCodes, 0, 100);
                var consistencyBase = Math.Max(1, totalRuns * Math.Max(1, distinctScanCodes));
                var consistencyScore = Math.Clamp((observationCount * 100.0) / consistencyBase, 0, 100);
                var rankScore = _options.ScannerRows <= 1
                    ? 100
                    : Math.Clamp((1 - ((averageRank - 1) / (_options.ScannerRows - 1))) * 100, 0, 100);

                var projectionValues = entries
                    .Select(x => TryParseScannerMetric(x.Projection))
                    .Where(x => x is not null)
                    .Select(x => x!.Value)
                    .ToArray();

                var averageProjection = projectionValues.Length == 0
                    ? (double?)null
                    : projectionValues.Average();
                var signalScore = averageProjection is null
                    ? 50
                    : Math.Clamp(50 + (averageProjection.Value * 5), 0, 100);

                var weightedScore = (rankScore * 0.45) + (consistencyScore * 0.20) + (coverageScore * 0.20) + (signalScore * 0.15);

                var rejectReason = string.Empty;
                if (observationCount < requiredObservations)
                {
                    rejectReason = "insufficient-observations";
                }

                var eligible = string.IsNullOrWhiteSpace(rejectReason);
                var exchange = entries
                    .Select(x => x.PrimaryExchange)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? entries.Select(x => x.Exchange).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? string.Empty;
                var currency = entries
                    .Select(x => x.Currency)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? string.Empty;

                return new ScannerWorkbenchCandidateRow(
                    group.Key,
                    entries.Select(x => x.ConId).FirstOrDefault(x => x > 0),
                    exchange,
                    currency,
                    observationCount,
                    distinctScanCodes,
                    Math.Round(averageRank, 3),
                    bestRank,
                    averageProjection is null ? null : Math.Round(averageProjection.Value, 4),
                    Math.Round(rankScore, 3),
                    Math.Round(consistencyScore, 3),
                    Math.Round(coverageScore, 3),
                    Math.Round(signalScore, 3),
                    Math.Round(weightedScore, 3),
                    eligible,
                    rejectReason
                );
            })
            .OrderByDescending(x => x.Eligible)
            .ThenByDescending(x => x.WeightedScore)
            .ThenByDescending(x => x.CoverageScore)
            .ThenBy(x => x.AverageRank)
            .ToArray();
    }

    private static double? TryParseScannerMetric(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace("%", string.Empty);
        return double.TryParse(normalized, out var parsed)
            ? parsed
            : null;
    }

}
