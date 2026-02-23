using Harvester.App.IBKR.Wrapper;

namespace Harvester.App.IBKR.Runtime;

public enum IbErrorAction
{
    Ignore,
    Warn,
    Retry,
    HardFail
}

public sealed record IbErrorDecision(IbErrorAction Action, string Reason);

public sealed class IbErrorPolicy
{
    private static readonly HashSet<int> InformationalCodes =
    [
        2100, 2104, 2106, 2158,
        10089, 10167, 10168, 10187,
        10285, 354, 322, 300, 310, 420
    ];

    private static readonly HashSet<int> RetryableCodes =
    [
        1100, 1101, 1102
    ];

    private static readonly HashSet<int> ScannerExpectedCodes =
    [
        162, 200, 300, 321, 354, 365, 420, 10186, 10337
    ];

    public IbErrorDecision Evaluate(IbApiError error, RunMode mode, bool optionGreeksAutoFallback)
    {
        if (error.Code is null)
        {
            return new IbErrorDecision(IbErrorAction.Warn, "unclassified error payload");
        }

        var code = error.Code.Value;
        var message = error.Message ?? string.Empty;

        if (InformationalCodes.Contains(code))
        {
            return new IbErrorDecision(IbErrorAction.Warn, "informational/non-blocking code");
        }

        if (RetryableCodes.Contains(code))
        {
            return new IbErrorDecision(IbErrorAction.Retry, "connectivity/retryable code");
        }

        if (code == 162 && message.Contains("query cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return new IbErrorDecision(IbErrorAction.Warn, "expected query cancellation");
        }

        if (mode == RunMode.OptionGreeks && optionGreeksAutoFallback)
        {
            var isExpectedProbeError =
                (error.Id == 98040 || error.Id == 9804)
                && (code == 200 || code == 300);

            if (isExpectedProbeError)
            {
                return new IbErrorDecision(IbErrorAction.Warn, "expected option probe error during auto-fallback");
            }
        }

        if ((mode == RunMode.FaAllocationGroups
            || mode == RunMode.FaGroupsProfiles
            || mode == RunMode.FaUnification
            || mode == RunMode.FaModelPortfolios
            || mode == RunMode.FaOrder)
            && code == 321)
        {
            var expectedFaValidation =
                message.Contains("FA data operations ignored for non FA customers", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Model name", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cause - Model", StringComparison.OrdinalIgnoreCase);

            if (expectedFaValidation)
            {
                return new IbErrorDecision(IbErrorAction.Warn, "expected FA validation path");
            }
        }

        if (mode == RunMode.FundamentalData && code == 10358)
        {
            return new IbErrorDecision(IbErrorAction.Warn, "fundamental data entitlement/availability warning");
        }

        if ((mode == RunMode.ScannerExamples
            || mode == RunMode.ScannerComplex
            || mode == RunMode.ScannerParameters
            || mode == RunMode.ScannerWorkbench)
            && ScannerExpectedCodes.Contains(code))
        {
            return new IbErrorDecision(IbErrorAction.Warn, "scanner expected warning path");
        }

        if ((mode == RunMode.DisplayGroupsQuery
            || mode == RunMode.DisplayGroupsSubscribe
            || mode == RunMode.DisplayGroupsUpdate
            || mode == RunMode.DisplayGroupsUnsubscribe)
            && (code == 321 || code == 344 || code == 365))
        {
            return new IbErrorDecision(IbErrorAction.Warn, "display-groups expected validation/warning path");
        }

        return new IbErrorDecision(IbErrorAction.HardFail, "default hard-fail classification");
    }
}
