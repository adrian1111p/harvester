namespace Harvester.Contracts;

/// <summary>Scanner request parameters for an IBKR scanner subscription.</summary>
public sealed record ScannerRequestRow(
    int RequestId,
    string Instrument,
    string LocationCode,
    string ScanCode,
    int NumberOfRows,
    string ScannerSettingPairs,
    string FilterTagPairs,
    string OptionTagPairs
);

/// <summary>Scanner workbench run result — a single run of a scan code.</summary>
public sealed record ScannerWorkbenchRunRow(
    DateTime TimestampUtc,
    int RequestId,
    string ScanCode,
    int RunIndex,
    int Rows,
    double DurationSeconds,
    double? FirstRowSeconds,
    int ErrorCount,
    string ErrorCodes
);

/// <summary>Scanner workbench quality score for a scan code.</summary>
public sealed record ScannerWorkbenchScoreRow(
    string ScanCode,
    int Runs,
    double AverageRows,
    double AverageFirstRowSeconds,
    double AverageErrors,
    double CoverageScore,
    double SpeedScore,
    double StabilityScore,
    double CleanlinessScore,
    double WeightedScore,
    bool HardFail
);

/// <summary>Individual candidate observation from a scanner workbench run.</summary>
public sealed record ScannerWorkbenchCandidateObservationRow(
    DateTime TimestampUtc,
    int RequestId,
    string ScanCode,
    int RunIndex,
    string Symbol,
    int ConId,
    int Rank,
    string Exchange,
    string PrimaryExchange,
    string Currency,
    string Distance,
    string Benchmark,
    string Projection
);

/// <summary>Aggregated candidate score from scanner workbench.</summary>
public sealed record ScannerWorkbenchCandidateRow(
    string Symbol,
    int ConId,
    string Exchange,
    string Currency,
    int ObservationCount,
    int DistinctScanCodes,
    double AverageRank,
    int BestRank,
    double? AverageProjection,
    double RankScore,
    double ConsistencyScore,
    double CoverageScore,
    double SignalScore,
    double WeightedScore,
    bool Eligible,
    string RejectReason
);

/// <summary>Scanner preview summary — aggregate stats for a preview run.</summary>
public sealed record ScannerPreviewSummaryRow(
    DateTime TimestampUtc,
    string Action,
    string ResolvedInputPath,
    string ResolvedInputFullPath,
    bool FileConfigured,
    bool FileExists,
    bool FileIsTempLock,
    string Phase,
    bool IsTradingDay,
    DateTime SessionOpenUtc,
    DateTime OpenPhaseEndUtc,
    DateTime PostOpenEndUtc,
    int RawRowCount,
    int NormalizedRowCount,
    int SelectedRowCount,
    string[] SelectedSymbols,
    string[] Notes
);

/// <summary>Individual scanner preview candidate.</summary>
public sealed record ScannerPreviewCandidateRow(
    string Symbol,
    double WeightedScore,
    bool? Eligible,
    double AverageRank,
    bool AllowListed,
    bool MeetsScoreAndEligibility,
    bool Selected
);
