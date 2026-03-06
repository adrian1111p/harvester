using Harvester.App.IBKR.Risk;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;

namespace Harvester.App.IBKR.Runtime;

internal static class AppOptionsParser
{
    public static AppOptions Parse(string[] args)
    {
        args = BuildMergedArguments(args);

        var mode = RunMode.Connect;
        var host = "127.0.0.1";
        var port = 7496;
        var clientId = 9100;
        var account = ResolveDefaultAccount();
        var timeoutSeconds = 25;
        var exportDir = "exports";
        var symbol = "SIRI";
        var primaryExchange = "NASDAQ";
        var enableLive = false;
        var liveSymbol = "SIRI";
        var liveAction = "BUY";
        var liveOrderType = "LMT";
        var liveQuantity = 1.0;
        var liveLimitPrice = 5.00;
        var livePriceSanityRequireQuote = true;
        var liveMomentumGuardEnabled = true;
        var liveMomentumMaxAdverseBps = 20.0;
        var conductL1StaleSec = -1;
        var monitorUiPort = 5100;
        var monitorUiEnabled = false;
        var cancelOrderId = 0;
        var cancelOrderIdempotent = false;
        var maxNotional = 100.00;
        var maxShares = 10.0;
        var maxPrice = 10.0;
        var allowedSymbols = new[] { "SIRI", "SOFI", "F", "PLTR" };
        var liveScannerCandidatesInputPath = string.Empty;
        var liveScannerOpenPhaseInputPath = string.Empty;
        var liveScannerPostOpenGainersInputPath = string.Empty;
        var liveScannerPostOpenLosersInputPath = string.Empty;
        var liveScannerOpenPhaseMinutes = 45;
        var liveScannerPostOpenMinutes = 120;
        var liveScannerTopN = 5;
        var liveScannerMinScore = 60.0;
        var liveAllocationMode = "manual";
        var liveAllocationBudget = 0.0;
        var liveScannerKillSwitchMaxFileAgeMinutes = 30;
        var liveScannerKillSwitchMinCandidates = 1;
        var liveScannerKillSwitchMaxBudgetConcentrationPct = 100.0;
        var whatIfTemplate = "lmt";
        var marketDataType = 3;
        var captureSeconds = 12;
        var depthRows = 5;
        var depthExchange = "NASDAQ";
        var realTimeBarsWhatToShow = "TRADES";
        var historicalEndDateTime = string.Empty;
        var historicalDuration = "1 D";
        var historicalBarSize = "5 mins";
        var historicalWhatToShow = "TRADES";
        var historicalUseRth = 1;
        var historicalFormatDate = 1;
        var histogramPeriod = "1 week";
        var historicalTickStart = string.Empty;
        var historicalTickEnd = DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss");
        var historicalTicksNumber = 200;
        var historicalTicksWhatToShow = "TRADES";
        var historicalTickIgnoreSize = true;
        var headTimestampWhatToShow = "TRADES";
        var updateAccount = account;
        var accountSummaryGroup = "All";
        var accountSummaryTags = "AccountType,NetLiquidation,TotalCashValue,BuyingPower,MaintMarginReq,AvailableFunds";
        var accountUpdatesMultiAccount = account;
        var positionsMultiAccount = account;
        var modelCode = string.Empty;
        var pnlAccount = account;
        var pnlConId = 0;
        var optionSymbol = "SIRI";
        var optionExpiry = DateTime.UtcNow.AddMonths(1).ToString("yyyyMMdd");
        var optionStrike = 5.0;
        var optionRight = "C";
        var optionExchange = "SMART";
        var optionCurrency = "USD";
        var optionMultiplier = "100";
        var optionUnderlyingSecType = "STK";
        var optionFutFopExchange = string.Empty;
        var optionExerciseAllow = false;
        var optionExerciseAction = 1;
        var optionExerciseQuantity = 1;
        var optionExerciseOverride = 0;
        var optionExerciseManualTime = string.Empty;
        var optionGreeksAutoFallback = false;
        var cryptoSymbol = "BTC";
        var cryptoExchange = "PAXOS";
        var cryptoCurrency = "USD";
        var cryptoOrderAllow = false;
        var cryptoOrderAction = "BUY";
        var cryptoOrderQuantity = 0.001;
        var cryptoOrderLimit = 30000.0;
        var cryptoMaxNotional = 100.0;
        var faAccount = account;
        var faModelCode = string.Empty;
        var faOrderAllow = false;
        var faOrderAccount = account;
        var faOrderSymbol = "SIRI";
        var faOrderAction = "BUY";
        var faOrderQuantity = 1.0;
        var faOrderLimit = 5.0;
        var faMaxNotional = 100.0;
        var faOrderGroup = string.Empty;
        var faOrderMethod = string.Empty;
        var faOrderPercentage = string.Empty;
        var faOrderProfile = string.Empty;
        var faOrderExchange = "SMART";
        var faOrderPrimaryExchange = "NASDAQ";
        var faOrderCurrency = "USD";
        var faRoutingStrictness = FaRoutingStrictness.Reject;
        var preTradeControlsDsl = "max-notional=reject;max-qty=reject;max-daily-orders=reject;session-window=halt";
        var preTradeMaxDailyOrders = 5;
        var preTradeSessionStartUtc = "13:30";
        var preTradeSessionEndUtc = "16:15";
        var marketCloseWarningMinutes = 15;
        var preTradeCostProfile = PreTradeCostProfile.MicroEquity;
        var preTradeCommissionPerUnit = 0.0035;
        var preTradeMinCommissionPerOrder = 0.0;
        var preTradeSlippageBps = 4.0;
        var fundamentalReportType = "ReportSnapshot";
        var wshFilterJson = "{}";
        var scannerInstrument = "STK";
        var scannerLocationCode = "STK.US.MAJOR";
        var scannerScanCode = "TOP_PERC_GAIN";
        var scannerRows = 10;
        var scannerAbovePrice = 1.0;
        var scannerBelowPrice = 0.0;
        var scannerAboveVolume = 100000;
        var scannerMarketCapAbove = 0.0;
        var scannerMarketCapBelow = 0.0;
        var scannerStockTypeFilter = "ALL";
        var scannerScannerSettingPairs = string.Empty;
        var scannerFilterTagValues = string.Empty;
        var scannerOptionsTagValues = string.Empty;
        var scannerWorkbenchCodes = "TOP_PERC_GAIN,HOT_BY_VOLUME,MOST_ACTIVE";
        var scannerWorkbenchRuns = 2;
        var scannerWorkbenchCaptureSeconds = 6;
        var scannerWorkbenchMinRows = 1;
        var displayGroupId = 1;
        var displayGroupContractInfo = "265598@SMART";
        var displayGroupCaptureSeconds = 4;
        var replayInputPath = string.Empty;
        var replayOrdersInputPath = string.Empty;
        var replayCorporateActionsInputPath = string.Empty;
        var replaySymbolMappingsInputPath = string.Empty;
        var replayDelistEventsInputPath = string.Empty;
        var replayBorrowLocateInputPath = string.Empty;
        var replayScannerCandidatesInputPath = string.Empty;
        var replayScannerTopN = 5;
        var replayScannerMinScore = 60.0;
        var replayScannerOrderQuantity = 1.0;
        var replayScannerOrderSide = "BUY";
        var replayScannerOrderType = "MKT";
        var replayScannerOrderTimeInForce = "DAY";
        var replayScannerLimitOffsetBps = 0.0;
        var replayPriceNormalization = "raw";
        var replayIntervalSeconds = 0;
        var replayMaxRows = 5000;
        var replayInitialCash = 100000.0;
        var replayCommissionPerUnit = preTradeCommissionPerUnit;
        var replaySlippageBps = preTradeSlippageBps;
        var replayInitialMarginRate = 0.50;
        var replayMaintenanceMarginRate = 0.30;
        var replaySecFeeRatePerDollar = 0.0;
        var replayTafFeePerShare = 0.0;
        var replayTafFeeCapPerOrder = 0.0;
        var replayExchangeFeePerShare = 0.0;
        var replayMaxFillParticipationRate = 1.0;
        var replayPriceIncrement = 0.0;
        var replayEnforceQueuePriority = true;
        var replaySettlementLagDays = 2;
        var replayEnforceSettledCash = true;
        var heartbeatMonitorEnabled = true;
        var heartbeatIntervalSeconds = 6;
        var heartbeatProbeTimeoutSeconds = 4;
        var reconnectMaxAttempts = 3;
        var reconnectBackoffSeconds = 2;
        var clockSkewAction = ClockSkewAction.Warn;
        var clockSkewWarnSeconds = 2.0;
        var clockSkewFailSeconds = 15.0;
        var reconciliationGateAction = ReconciliationGateAction.Warn;
        var reconciliationMinCommissionCoverage = 0.80;
        var reconciliationMinOrderCoverage = 0.95;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    mode = ParseMode(args[++i]);
                    break;
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    port = p;
                    i++;
                    break;
                case "--client-id" when i + 1 < args.Length && int.TryParse(args[i + 1], out var c):
                    clientId = c;
                    i++;
                    break;
                case "--account" when i + 1 < args.Length:
                    account = args[++i];
                    break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t):
                    timeoutSeconds = t;
                    i++;
                    break;
                case "--export-dir" when i + 1 < args.Length:
                    exportDir = args[++i];
                    break;
                case "--symbol" when i + 1 < args.Length:
                    symbol = args[++i].ToUpperInvariant();
                    break;
                case "--primary-exchange" when i + 1 < args.Length:
                    primaryExchange = NormalizeSupportedStockExchange(args[++i], "--primary-exchange");
                    break;
                case "--enable-live" when i + 1 < args.Length:
                    enableLive = bool.TryParse(args[++i], out var flag) && flag;
                    break;
                case "--live-symbol" when i + 1 < args.Length:
                    liveSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--live-action" when i + 1 < args.Length:
                    liveAction = args[++i].ToUpperInvariant();
                    break;
                case "--live-order-type" when i + 1 < args.Length:
                    liveOrderType = args[++i].ToUpperInvariant();
                    break;
                case "--live-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var q):
                    liveQuantity = q;
                    i++;
                    break;
                case "--live-limit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var lp):
                    liveLimitPrice = lp;
                    i++;
                    break;
                case "--live-price-sanity-require-quote" when i + 1 < args.Length:
                    livePriceSanityRequireQuote = bool.TryParse(args[++i], out var lpsrq) && lpsrq;
                    break;
                case "--live-momentum-guard" when i + 1 < args.Length:
                    liveMomentumGuardEnabled = bool.TryParse(args[++i], out var lmg) && lmg;
                    break;
                case "--conduct-l1-stale-sec" when i + 1 < args.Length && int.TryParse(args[i + 1], out var cls):
                    conductL1StaleSec = cls;
                    i++;
                    break;
                case "--monitor-ui-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var muip):
                    monitorUiPort = muip;
                    i++;
                    break;
                case "--monitor-ui":
                    monitorUiEnabled = true;
                    break;
                case "--live-momentum-max-adverse-bps" when i + 1 < args.Length && double.TryParse(args[i + 1], out var lmmab):
                    liveMomentumMaxAdverseBps = Math.Max(0, lmmab);
                    i++;
                    break;
                case "--cancel-order-id" when i + 1 < args.Length && int.TryParse(args[i + 1], out var coid):
                    cancelOrderId = coid;
                    i++;
                    break;
                case "--cancel-idempotent" when i + 1 < args.Length:
                    cancelOrderIdempotent = bool.TryParse(args[++i], out var cio) && cio;
                    break;
                case "--max-notional" when i + 1 < args.Length && double.TryParse(args[i + 1], out var mn):
                    maxNotional = mn;
                    i++;
                    break;
                case "--max-shares" when i + 1 < args.Length && double.TryParse(args[i + 1], out var ms):
                    maxShares = ms;
                    i++;
                    break;
                case "--max-price" when i + 1 < args.Length && double.TryParse(args[i + 1], out var mp):
                    maxPrice = mp;
                    i++;
                    break;
                case "--allowed-symbols" when i + 1 < args.Length:
                    allowedSymbols = args[++i]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => x.ToUpperInvariant())
                        .ToArray();
                    break;
                case "--live-scanner-candidates-input" when i + 1 < args.Length:
                    liveScannerCandidatesInputPath = args[++i];
                    break;
                case "--live-scanner-open-phase-input" when i + 1 < args.Length:
                    liveScannerOpenPhaseInputPath = args[++i];
                    break;
                case "--live-scanner-post-open-gainers-input" when i + 1 < args.Length:
                    liveScannerPostOpenGainersInputPath = args[++i];
                    break;
                case "--live-scanner-post-open-losers-input" when i + 1 < args.Length:
                    liveScannerPostOpenLosersInputPath = args[++i];
                    break;
                case "--live-scanner-open-phase-minutes" when i + 1 < args.Length && int.TryParse(args[i + 1], out var lsopm):
                    liveScannerOpenPhaseMinutes = Math.Max(1, lsopm);
                    i++;
                    break;
                case "--live-scanner-post-open-minutes" when i + 1 < args.Length && int.TryParse(args[i + 1], out var lspom):
                    liveScannerPostOpenMinutes = Math.Max(1, lspom);
                    i++;
                    break;
                case "--live-scanner-top-n" when i + 1 < args.Length && int.TryParse(args[i + 1], out var lstn):
                    liveScannerTopN = Math.Max(1, lstn);
                    i++;
                    break;
                case "--live-scanner-min-score" when i + 1 < args.Length && double.TryParse(args[i + 1], out var lsms):
                    liveScannerMinScore = lsms;
                    i++;
                    break;
                case "--live-allocation-mode" when i + 1 < args.Length:
                    liveAllocationMode = args[++i].ToLowerInvariant();
                    break;
                case "--live-allocation-budget" when i + 1 < args.Length && double.TryParse(args[i + 1], out var lab):
                    liveAllocationBudget = Math.Max(0, lab);
                    i++;
                    break;
                case "--live-killswitch-max-file-age-minutes" when i + 1 < args.Length && int.TryParse(args[i + 1], out var lsf):
                    liveScannerKillSwitchMaxFileAgeMinutes = Math.Max(0, lsf);
                    i++;
                    break;
                case "--live-killswitch-min-candidates" when i + 1 < args.Length && int.TryParse(args[i + 1], out var lsmc):
                    liveScannerKillSwitchMinCandidates = Math.Max(1, lsmc);
                    i++;
                    break;
                case "--live-killswitch-max-budget-concentration-pct" when i + 1 < args.Length && double.TryParse(args[i + 1], out var lsbc):
                    liveScannerKillSwitchMaxBudgetConcentrationPct = Math.Clamp(lsbc, 0, 100);
                    i++;
                    break;
                case "--whatif-template" when i + 1 < args.Length:
                    whatIfTemplate = args[++i].ToLowerInvariant();
                    break;
                case "--market-data-type" when i + 1 < args.Length && int.TryParse(args[i + 1], out var mdt):
                    marketDataType = mdt;
                    i++;
                    break;
                case "--capture-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var cs):
                    captureSeconds = cs;
                    i++;
                    break;
                case "--depth-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dr):
                    depthRows = dr;
                    i++;
                    break;
                case "--depth-exchange" when i + 1 < args.Length:
                    depthExchange = NormalizeSupportedStockExchange(args[++i], "--depth-exchange");
                    break;
                case "--rtb-what" when i + 1 < args.Length:
                    realTimeBarsWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--hist-end" when i + 1 < args.Length:
                    historicalEndDateTime = args[++i];
                    break;
                case "--hist-duration" when i + 1 < args.Length:
                    historicalDuration = args[++i];
                    break;
                case "--hist-barsize" when i + 1 < args.Length:
                    historicalBarSize = args[++i].ToLowerInvariant();
                    break;
                case "--hist-what" when i + 1 < args.Length:
                    historicalWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--hist-use-rth" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hu):
                    historicalUseRth = hu;
                    i++;
                    break;
                case "--hist-format-date" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hf):
                    historicalFormatDate = hf;
                    i++;
                    break;
                case "--histogram-period" when i + 1 < args.Length:
                    histogramPeriod = args[++i];
                    break;
                case "--hist-tick-start" when i + 1 < args.Length:
                    historicalTickStart = args[++i];
                    break;
                case "--hist-tick-end" when i + 1 < args.Length:
                    historicalTickEnd = args[++i];
                    break;
                case "--hist-ticks-num" when i + 1 < args.Length && int.TryParse(args[i + 1], out var htn):
                    historicalTicksNumber = htn;
                    i++;
                    break;
                case "--hist-ticks-what" when i + 1 < args.Length:
                    historicalTicksWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--hist-ignore-size" when i + 1 < args.Length:
                    historicalTickIgnoreSize = bool.TryParse(args[++i], out var his) && his;
                    break;
                case "--head-what" when i + 1 < args.Length:
                    headTimestampWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--update-account" when i + 1 < args.Length:
                    updateAccount = args[++i];
                    break;
                case "--summary-group" when i + 1 < args.Length:
                    accountSummaryGroup = args[++i];
                    break;
                case "--summary-tags" when i + 1 < args.Length:
                    accountSummaryTags = args[++i];
                    break;
                case "--updates-multi-account" when i + 1 < args.Length:
                    accountUpdatesMultiAccount = args[++i];
                    break;
                case "--positions-multi-account" when i + 1 < args.Length:
                    positionsMultiAccount = args[++i];
                    break;
                case "--model-code":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        modelCode = args[++i];
                    }
                    else
                    {
                        modelCode = string.Empty;
                    }
                    break;
                case "--pnl-account" when i + 1 < args.Length:
                    pnlAccount = args[++i];
                    break;
                case "--pnl-conid" when i + 1 < args.Length && int.TryParse(args[i + 1], out var pcon):
                    pnlConId = pcon;
                    i++;
                    break;
                case "--opt-symbol" when i + 1 < args.Length:
                    optionSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--opt-expiry" when i + 1 < args.Length:
                    optionExpiry = args[++i];
                    break;
                case "--opt-strike" when i + 1 < args.Length && double.TryParse(args[i + 1], out var os):
                    optionStrike = os;
                    i++;
                    break;
                case "--opt-right" when i + 1 < args.Length:
                    optionRight = args[++i].ToUpperInvariant();
                    break;
                case "--opt-exchange" when i + 1 < args.Length:
                    optionExchange = args[++i].ToUpperInvariant();
                    break;
                case "--opt-currency" when i + 1 < args.Length:
                    optionCurrency = args[++i].ToUpperInvariant();
                    break;
                case "--opt-multiplier" when i + 1 < args.Length:
                    optionMultiplier = args[++i];
                    break;
                case "--opt-underlying-sec-type" when i + 1 < args.Length:
                    optionUnderlyingSecType = args[++i].ToUpperInvariant();
                    break;
                case "--opt-futfop-exchange" when i + 1 < args.Length:
                    optionFutFopExchange = args[++i].ToUpperInvariant();
                    break;
                case "--option-exercise-allow" when i + 1 < args.Length:
                    optionExerciseAllow = bool.TryParse(args[++i], out var oea) && oea;
                    break;
                case "--option-exercise-action" when i + 1 < args.Length && int.TryParse(args[i + 1], out var oaction):
                    optionExerciseAction = oaction;
                    i++;
                    break;
                case "--option-exercise-qty" when i + 1 < args.Length && int.TryParse(args[i + 1], out var oqty):
                    optionExerciseQuantity = oqty;
                    i++;
                    break;
                case "--option-exercise-override" when i + 1 < args.Length && int.TryParse(args[i + 1], out var oovr):
                    optionExerciseOverride = oovr;
                    i++;
                    break;
                case "--option-exercise-time" when i + 1 < args.Length:
                    optionExerciseManualTime = args[++i];
                    break;
                case "--option-greeks-auto-fallback" when i + 1 < args.Length:
                    optionGreeksAutoFallback = bool.TryParse(args[++i], out var ogf) && ogf;
                    break;
                case "--crypto-symbol" when i + 1 < args.Length:
                    cryptoSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-exchange" when i + 1 < args.Length:
                    cryptoExchange = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-currency" when i + 1 < args.Length:
                    cryptoCurrency = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-order-allow" when i + 1 < args.Length:
                    cryptoOrderAllow = bool.TryParse(args[++i], out var coa) && coa;
                    break;
                case "--crypto-order-action" when i + 1 < args.Length:
                    cryptoOrderAction = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-order-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var coq):
                    cryptoOrderQuantity = coq;
                    i++;
                    break;
                case "--crypto-order-limit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var col):
                    cryptoOrderLimit = col;
                    i++;
                    break;
                case "--crypto-max-notional" when i + 1 < args.Length && double.TryParse(args[i + 1], out var cmn):
                    cryptoMaxNotional = cmn;
                    i++;
                    break;
                case "--fa-account" when i + 1 < args.Length:
                    faAccount = args[++i];
                    break;
                case "--fa-model-code" when i + 1 < args.Length:
                    faModelCode = args[++i];
                    break;
                case "--fa-order-allow" when i + 1 < args.Length:
                    faOrderAllow = bool.TryParse(args[++i], out var foa) && foa;
                    break;
                case "--fa-order-account" when i + 1 < args.Length:
                    faOrderAccount = args[++i];
                    break;
                case "--fa-order-symbol" when i + 1 < args.Length:
                    faOrderSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-action" when i + 1 < args.Length:
                    faOrderAction = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var foq):
                    faOrderQuantity = foq;
                    i++;
                    break;
                case "--fa-order-limit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var fol):
                    faOrderLimit = fol;
                    i++;
                    break;
                case "--fa-max-notional" when i + 1 < args.Length && double.TryParse(args[i + 1], out var fmn):
                    faMaxNotional = fmn;
                    i++;
                    break;
                case "--fa-order-group" when i + 1 < args.Length:
                    faOrderGroup = args[++i];
                    break;
                case "--fa-order-method" when i + 1 < args.Length:
                    faOrderMethod = args[++i];
                    break;
                case "--fa-order-percentage" when i + 1 < args.Length:
                    faOrderPercentage = args[++i];
                    break;
                case "--fa-order-profile" when i + 1 < args.Length:
                    faOrderProfile = args[++i];
                    break;
                case "--fa-order-exchange" when i + 1 < args.Length:
                    faOrderExchange = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-primary-exchange" when i + 1 < args.Length:
                    faOrderPrimaryExchange = NormalizeSupportedStockExchange(args[++i], "--fa-order-primary-exchange");
                    break;
                case "--fa-order-currency" when i + 1 < args.Length:
                    faOrderCurrency = args[++i].ToUpperInvariant();
                    break;
                case "--fa-routing-strictness" when i + 1 < args.Length:
                    faRoutingStrictness = ParseFaRoutingStrictness(args[++i]);
                    break;
                case "--pretrade-controls" when i + 1 < args.Length:
                    preTradeControlsDsl = args[++i];
                    break;
                case "--pretrade-max-daily-orders" when i + 1 < args.Length && int.TryParse(args[i + 1], out var ptd):
                    preTradeMaxDailyOrders = ptd;
                    i++;
                    break;
                case "--pretrade-session-start" when i + 1 < args.Length:
                    preTradeSessionStartUtc = args[++i];
                    break;
                case "--pretrade-session-end" when i + 1 < args.Length:
                    preTradeSessionEndUtc = args[++i];
                    break;
                case "--market-close-warning-minutes" when i + 1 < args.Length && int.TryParse(args[i + 1], out var mcw):
                    marketCloseWarningMinutes = mcw;
                    i++;
                    break;
                case "--pretrade-cost-profile" when i + 1 < args.Length:
                    preTradeCostProfile = ParsePreTradeCostProfile(args[++i]);
                    break;
                case "--pretrade-commission-per-unit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var ptcpu):
                    preTradeCommissionPerUnit = ptcpu;
                    i++;
                    break;
                case "--pretrade-min-commission-per-order" when i + 1 < args.Length && double.TryParse(args[i + 1], out var ptmc):
                    preTradeMinCommissionPerOrder = Math.Max(0, ptmc);
                    i++;
                    break;
                case "--pretrade-slippage-bps" when i + 1 < args.Length && double.TryParse(args[i + 1], out var ptsb):
                    preTradeSlippageBps = ptsb;
                    i++;
                    break;
                case "--fund-report-type" when i + 1 < args.Length:
                    fundamentalReportType = args[++i];
                    break;
                case "--wsh-filter-json" when i + 1 < args.Length:
                    wshFilterJson = args[++i];
                    break;
                case "--scanner-instrument" when i + 1 < args.Length:
                    scannerInstrument = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-location" when i + 1 < args.Length:
                    scannerLocationCode = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-code" when i + 1 < args.Length:
                    scannerScanCode = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var srows):
                    scannerRows = srows;
                    i++;
                    break;
                case "--scanner-above-price" when i + 1 < args.Length && double.TryParse(args[i + 1], out var sap):
                    scannerAbovePrice = sap;
                    i++;
                    break;
                case "--scanner-below-price" when i + 1 < args.Length && double.TryParse(args[i + 1], out var sbp):
                    scannerBelowPrice = sbp;
                    i++;
                    break;
                case "--scanner-above-volume" when i + 1 < args.Length && int.TryParse(args[i + 1], out var sav):
                    scannerAboveVolume = sav;
                    i++;
                    break;
                case "--scanner-mcap-above" when i + 1 < args.Length && double.TryParse(args[i + 1], out var smca):
                    scannerMarketCapAbove = smca;
                    i++;
                    break;
                case "--scanner-mcap-below" when i + 1 < args.Length && double.TryParse(args[i + 1], out var smcb):
                    scannerMarketCapBelow = smcb;
                    i++;
                    break;
                case "--scanner-stock-type" when i + 1 < args.Length:
                    scannerStockTypeFilter = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-setting-pairs" when i + 1 < args.Length:
                    scannerScannerSettingPairs = args[++i];
                    break;
                case "--scanner-filter-tags" when i + 1 < args.Length:
                    scannerFilterTagValues = args[++i];
                    break;
                case "--scanner-options-tags" when i + 1 < args.Length:
                    scannerOptionsTagValues = args[++i];
                    break;
                case "--scanner-workbench-codes" when i + 1 < args.Length:
                    scannerWorkbenchCodes = args[++i];
                    break;
                case "--scanner-workbench-runs" when i + 1 < args.Length && int.TryParse(args[i + 1], out var swr):
                    scannerWorkbenchRuns = swr;
                    i++;
                    break;
                case "--scanner-workbench-capture-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var swc):
                    scannerWorkbenchCaptureSeconds = swc;
                    i++;
                    break;
                case "--scanner-workbench-min-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var swm):
                    scannerWorkbenchMinRows = swm;
                    i++;
                    break;
                case "--display-group-id" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dgid):
                    displayGroupId = dgid;
                    i++;
                    break;
                case "--display-group-contract-info" when i + 1 < args.Length:
                    displayGroupContractInfo = args[++i];
                    break;
                case "--display-group-capture-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dgcs):
                    displayGroupCaptureSeconds = dgcs;
                    i++;
                    break;
                case "--replay-input" when i + 1 < args.Length:
                    replayInputPath = args[++i];
                    break;
                case "--replay-orders-input" when i + 1 < args.Length:
                    replayOrdersInputPath = args[++i];
                    break;
                case "--replay-corporate-actions-input" when i + 1 < args.Length:
                    replayCorporateActionsInputPath = args[++i];
                    break;
                case "--replay-symbol-mappings-input" when i + 1 < args.Length:
                    replaySymbolMappingsInputPath = args[++i];
                    break;
                case "--replay-delist-events-input" when i + 1 < args.Length:
                    replayDelistEventsInputPath = args[++i];
                    break;
                case "--replay-borrow-locate-input" when i + 1 < args.Length:
                    replayBorrowLocateInputPath = args[++i];
                    break;
                case "--replay-scanner-candidates-input" when i + 1 < args.Length:
                    replayScannerCandidatesInputPath = args[++i];
                    break;
                case "--replay-scanner-top-n" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rsn):
                    replayScannerTopN = Math.Max(1, rsn);
                    i++;
                    break;
                case "--replay-scanner-min-score" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rsms):
                    replayScannerMinScore = rsms;
                    i++;
                    break;
                case "--replay-scanner-order-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rsoq):
                    replayScannerOrderQuantity = Math.Max(0, rsoq);
                    i++;
                    break;
                case "--replay-scanner-order-side" when i + 1 < args.Length:
                    replayScannerOrderSide = args[++i].ToUpperInvariant();
                    break;
                case "--replay-scanner-order-type" when i + 1 < args.Length:
                    replayScannerOrderType = args[++i].ToUpperInvariant();
                    break;
                case "--replay-scanner-order-tif" when i + 1 < args.Length:
                    replayScannerOrderTimeInForce = args[++i].ToUpperInvariant();
                    break;
                case "--replay-scanner-limit-offset-bps" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rslob):
                    replayScannerLimitOffsetBps = Math.Max(0, rslob);
                    i++;
                    break;
                case "--replay-price-normalization" when i + 1 < args.Length:
                    replayPriceNormalization = args[++i];
                    break;
                case "--replay-interval-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var ris):
                    replayIntervalSeconds = ris;
                    i++;
                    break;
                case "--replay-max-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rmr):
                    replayMaxRows = rmr;
                    i++;
                    break;
                case "--replay-initial-cash" when i + 1 < args.Length && double.TryParse(args[i + 1], out var ric):
                    replayInitialCash = ric;
                    i++;
                    break;
                case "--replay-commission-per-unit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rcpu):
                    replayCommissionPerUnit = rcpu;
                    i++;
                    break;
                case "--replay-slippage-bps" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rsb):
                    replaySlippageBps = rsb;
                    i++;
                    break;
                case "--replay-initial-margin-rate" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rimr):
                    replayInitialMarginRate = Math.Max(0, rimr);
                    i++;
                    break;
                case "--replay-maintenance-margin-rate" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rmmr):
                    replayMaintenanceMarginRate = Math.Max(0, rmmr);
                    i++;
                    break;
                case "--replay-sec-fee-rate" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rsfr):
                    replaySecFeeRatePerDollar = Math.Max(0, rsfr);
                    i++;
                    break;
                case "--replay-taf-fee-per-share" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rtfs):
                    replayTafFeePerShare = Math.Max(0, rtfs);
                    i++;
                    break;
                case "--replay-taf-fee-cap" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rtfc):
                    replayTafFeeCapPerOrder = Math.Max(0, rtfc);
                    i++;
                    break;
                case "--replay-exchange-fee-per-share" when i + 1 < args.Length && double.TryParse(args[i + 1], out var refs):
                    replayExchangeFeePerShare = Math.Max(0, refs);
                    i++;
                    break;
                case "--replay-max-fill-participation" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rmfp):
                    replayMaxFillParticipationRate = Math.Clamp(rmfp, 0, 1);
                    i++;
                    break;
                case "--replay-price-increment" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rpi):
                    replayPriceIncrement = Math.Max(0, rpi);
                    i++;
                    break;
                case "--replay-enforce-queue-priority" when i + 1 < args.Length:
                    replayEnforceQueuePriority = bool.TryParse(args[++i], out var reqp) && reqp;
                    break;
                case "--replay-settlement-lag-days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rsld):
                    replaySettlementLagDays = Math.Max(0, rsld);
                    i++;
                    break;
                case "--replay-enforce-settled-cash" when i + 1 < args.Length:
                    replayEnforceSettledCash = bool.TryParse(args[++i], out var resc) && resc;
                    break;
                case "--heartbeat-monitor" when i + 1 < args.Length:
                    heartbeatMonitorEnabled = bool.TryParse(args[++i], out var hm) && hm;
                    break;
                case "--heartbeat-interval" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hi):
                    heartbeatIntervalSeconds = hi;
                    i++;
                    break;
                case "--heartbeat-probe-timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hpt):
                    heartbeatProbeTimeoutSeconds = hpt;
                    i++;
                    break;
                case "--reconnect-max-attempts" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rma):
                    reconnectMaxAttempts = rma;
                    i++;
                    break;
                case "--reconnect-backoff" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rbs):
                    reconnectBackoffSeconds = rbs;
                    i++;
                    break;
                case "--clock-skew-action" when i + 1 < args.Length:
                    clockSkewAction = ParseClockSkewAction(args[++i]);
                    break;
                case "--clock-skew-warn-seconds" when i + 1 < args.Length && double.TryParse(args[i + 1], out var csw):
                    clockSkewWarnSeconds = csw;
                    i++;
                    break;
                case "--clock-skew-fail-seconds" when i + 1 < args.Length && double.TryParse(args[i + 1], out var csf):
                    clockSkewFailSeconds = csf;
                    i++;
                    break;
                case "--recon-gate-action" when i + 1 < args.Length:
                    reconciliationGateAction = ParseReconciliationGateAction(args[++i]);
                    break;
                case "--recon-min-commission-coverage" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rmcc):
                    reconciliationMinCommissionCoverage = rmcc;
                    i++;
                    break;
                case "--recon-min-order-coverage" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rmoc):
                    reconciliationMinOrderCoverage = rmoc;
                    i++;
                    break;
            }
        }

        return new AppOptions(
            mode,
            host,
            port,
            clientId,
            account,
            timeoutSeconds,
            exportDir,
            symbol,
            primaryExchange,
            enableLive,
            liveSymbol,
            liveAction,
            liveOrderType,
            liveQuantity,
            liveLimitPrice,
            livePriceSanityRequireQuote,
            liveMomentumGuardEnabled,
            liveMomentumMaxAdverseBps,
            cancelOrderId,
            cancelOrderIdempotent,
            maxNotional,
            maxShares,
            maxPrice,
            allowedSymbols,
            liveScannerCandidatesInputPath,
            liveScannerOpenPhaseInputPath,
            liveScannerPostOpenGainersInputPath,
            liveScannerPostOpenLosersInputPath,
            liveScannerOpenPhaseMinutes,
            liveScannerPostOpenMinutes,
            liveScannerTopN,
            liveScannerMinScore,
            liveAllocationMode,
            liveAllocationBudget,
            liveScannerKillSwitchMaxFileAgeMinutes,
            liveScannerKillSwitchMinCandidates,
            liveScannerKillSwitchMaxBudgetConcentrationPct,
            whatIfTemplate,
            marketDataType,
            captureSeconds,
            depthRows,
            depthExchange,
            realTimeBarsWhatToShow,
            historicalEndDateTime,
            historicalDuration,
            historicalBarSize,
            historicalWhatToShow,
            historicalUseRth,
            historicalFormatDate,
            histogramPeriod,
            historicalTickStart,
            historicalTickEnd,
            historicalTicksNumber,
            historicalTicksWhatToShow,
            historicalTickIgnoreSize,
            headTimestampWhatToShow,
            updateAccount,
            accountSummaryGroup,
            accountSummaryTags,
            accountUpdatesMultiAccount,
            positionsMultiAccount,
            modelCode,
            pnlAccount,
            pnlConId,
            optionSymbol,
            optionExpiry,
            optionStrike,
            optionRight,
            optionExchange,
            optionCurrency,
            optionMultiplier,
            optionUnderlyingSecType,
            optionFutFopExchange,
            optionExerciseAllow,
            optionExerciseAction,
            optionExerciseQuantity,
            optionExerciseOverride,
            optionExerciseManualTime,
            optionGreeksAutoFallback,
            cryptoSymbol,
            cryptoExchange,
            cryptoCurrency,
            cryptoOrderAllow,
            cryptoOrderAction,
            cryptoOrderQuantity,
            cryptoOrderLimit,
            cryptoMaxNotional,
            faAccount,
            faModelCode,
            faOrderAllow,
            faOrderAccount,
            faOrderSymbol,
            faOrderAction,
            faOrderQuantity,
            faOrderLimit,
            faMaxNotional,
            faOrderGroup,
            faOrderMethod,
            faOrderPercentage,
            faOrderProfile,
            faOrderExchange,
            faOrderPrimaryExchange,
            faOrderCurrency,
            faRoutingStrictness,
            preTradeControlsDsl,
            preTradeMaxDailyOrders,
            preTradeSessionStartUtc,
            preTradeSessionEndUtc,
            marketCloseWarningMinutes,
            preTradeCostProfile,
            preTradeCommissionPerUnit,
            preTradeMinCommissionPerOrder,
            preTradeSlippageBps,
            fundamentalReportType,
            wshFilterJson,
            scannerInstrument,
            scannerLocationCode,
            scannerScanCode,
            scannerRows,
            scannerAbovePrice,
            scannerBelowPrice,
            scannerAboveVolume,
            scannerMarketCapAbove,
            scannerMarketCapBelow,
            scannerStockTypeFilter,
            scannerScannerSettingPairs,
            scannerFilterTagValues,
            scannerOptionsTagValues,
            scannerWorkbenchCodes,
            scannerWorkbenchRuns,
            scannerWorkbenchCaptureSeconds,
            scannerWorkbenchMinRows,
            displayGroupId,
            displayGroupContractInfo,
            displayGroupCaptureSeconds,
            replayInputPath,
            replayOrdersInputPath,
            replayCorporateActionsInputPath,
            replaySymbolMappingsInputPath,
            replayDelistEventsInputPath,
            replayBorrowLocateInputPath,
            replayScannerCandidatesInputPath,
            replayScannerTopN,
            replayScannerMinScore,
            replayScannerOrderQuantity,
            replayScannerOrderSide,
            replayScannerOrderType,
            replayScannerOrderTimeInForce,
            replayScannerLimitOffsetBps,
            replayPriceNormalization,
            replayIntervalSeconds,
            replayMaxRows,
            replayInitialCash,
            replayCommissionPerUnit,
            replaySlippageBps,
            replayInitialMarginRate,
            replayMaintenanceMarginRate,
            replaySecFeeRatePerDollar,
            replayTafFeePerShare,
            replayTafFeeCapPerOrder,
            replayExchangeFeePerShare,
            replayMaxFillParticipationRate,
            replayPriceIncrement,
            replayEnforceQueuePriority,
            replaySettlementLagDays,
            replayEnforceSettledCash,
            heartbeatMonitorEnabled,
            heartbeatIntervalSeconds,
            heartbeatProbeTimeoutSeconds,
            reconnectMaxAttempts,
            reconnectBackoffSeconds,
            clockSkewAction,
            clockSkewWarnSeconds,
            clockSkewFailSeconds,
            reconciliationGateAction,
            reconciliationMinCommissionCoverage,
            reconciliationMinOrderCoverage,
            conductL1StaleSec,
            monitorUiPort,
            monitorUiEnabled
        );
    }

    private static string[] BuildMergedArguments(string[] cliArgs)
    {
        var merged = new List<string>();
        merged.AddRange(BuildArgumentsFromConfiguration(cliArgs));
        merged.AddRange(cliArgs);
        return merged.ToArray();
    }

    private static IEnumerable<string> BuildArgumentsFromConfiguration(string[] cliArgs)
    {
        var explicitConfigPath = GetExplicitConfigPath(cliArgs);
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            builder.AddJsonFile(explicitConfigPath, optional: false, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(prefix: "HARVESTER_");

        var configuration = builder.Build();
        var section = configuration.GetSection("Harvester");
        if (!section.Exists())
        {
            section = configuration.GetSection("AppOptions");
        }

        if (!section.Exists())
        {
            return Array.Empty<string>();
        }

        return FlattenConfigurationSectionToArguments(section);
    }

    private static IEnumerable<string> FlattenConfigurationSectionToArguments(IConfigurationSection section)
    {
        var scalarValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var indexedValues = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in section.AsEnumerable(makePathsRelative: true))
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null)
            {
                continue;
            }

            var segments = entry.Key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var lastSegment = segments[^1];
            if (int.TryParse(lastSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                if (segments.Length == 1)
                {
                    continue;
                }

                var optionPath = BuildOptionPath(segments[..^1]);
                if (string.IsNullOrWhiteSpace(optionPath))
                {
                    continue;
                }

                if (!indexedValues.TryGetValue(optionPath, out var values))
                {
                    values = new SortedDictionary<int, string>();
                    indexedValues[optionPath] = values;
                }

                values[index] = entry.Value;
                continue;
            }

            var scalarPath = BuildOptionPath(segments);
            if (!string.IsNullOrWhiteSpace(scalarPath))
            {
                scalarValues[scalarPath] = entry.Value;
            }
        }

        foreach (var pair in indexedValues)
        {
            scalarValues[pair.Key] = string.Join(',', pair.Value.Values);
        }

        var args = new List<string>(scalarValues.Count * 2);
        foreach (var pair in scalarValues)
        {
            args.Add($"--{pair.Key}");
            args.Add(pair.Value);
        }

        return args;
    }

    private static string BuildOptionPath(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var normalized = segments
            .Select(ToKebabCase)
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join('-', normalized);
    }

    private static string? GetExplicitConfigPath(string[] cliArgs)
    {
        for (var i = 0; i < cliArgs.Length - 1; i++)
        {
            if (string.Equals(cliArgs[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                return cliArgs[i + 1];
            }
        }

        return null;
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string ResolveDefaultAccount()
    {
        var env = Environment.GetEnvironmentVariable("HARVESTER_IB_ACCOUNT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        env = Environment.GetEnvironmentVariable("IBKR_ACCOUNT");
        return string.IsNullOrWhiteSpace(env) ? string.Empty : env.Trim();
    }

    private static ReconciliationGateAction ParseReconciliationGateAction(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "off" => ReconciliationGateAction.Off,
            "warn" => ReconciliationGateAction.Warn,
            "fail" => ReconciliationGateAction.Fail,
            _ => throw new ArgumentException($"Unknown reconciliation gate action '{value}'. Use off|warn|fail.")
        };
    }

    private static FaRoutingStrictness ParseFaRoutingStrictness(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "off" => FaRoutingStrictness.Off,
            "warn" => FaRoutingStrictness.Warn,
            "reject" => FaRoutingStrictness.Reject,
            _ => throw new ArgumentException($"Unknown FA routing strictness '{value}'. Use off|warn|reject.")
        };
    }

    private static PreTradeCostProfile ParsePreTradeCostProfile(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "micro" => PreTradeCostProfile.MicroEquity,
            "microequity" => PreTradeCostProfile.MicroEquity,
            "conservative" => PreTradeCostProfile.Conservative,
            "volume-share" => PreTradeCostProfile.VolumeShareImpact,
            "volumeshare" => PreTradeCostProfile.VolumeShareImpact,
            "volshare" => PreTradeCostProfile.VolumeShareImpact,
            _ => throw new ArgumentException($"Unknown pretrade cost profile '{value}'. Use micro|conservative|volume-share.")
        };
    }

    private static ClockSkewAction ParseClockSkewAction(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "off" => ClockSkewAction.Off,
            "warn" => ClockSkewAction.Warn,
            "fail" => ClockSkewAction.Fail,
            _ => throw new ArgumentException($"Unknown clock skew action '{value}'. Use off|warn|fail.")
        };
    }

    private static string NormalizeSupportedStockExchange(string value, string optionName)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "NYSE" => "NYSE",
            "NSDQ" => "NASDAQ",
            "NASDAQ" => "NASDAQ",
            _ => throw new ArgumentException($"Unsupported exchange '{value}' for {optionName}. Use NYSE or NSDQ.")
        };
    }

    private static RunMode ParseMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "connect" => RunMode.Connect,
            "orders" => RunMode.Orders,
            "orders-all-open" => RunMode.OrdersAllOpen,
            "positions" => RunMode.Positions,
            "positions-monitor-1pct" => RunMode.PositionsMonitor1Pct,
            "positions-monitor-1pct-loop" => RunMode.PositionsMonitor1PctLoop,
            "positions-auto-replace-scan-loop" => RunMode.PositionsAutoReplaceScanLoop,
            "snapshot-all" => RunMode.SnapshotAll,
            "contracts-validate" => RunMode.ContractsValidate,
            "orders-dryrun" => RunMode.OrdersDryRun,
            "orders-place-sim" => RunMode.OrdersPlaceSim,
            "orders-cancel-sim" => RunMode.OrdersCancelSim,
            "orders-whatif" => RunMode.OrdersWhatIf,
            "top-data" => RunMode.TopData,
            "market-depth" => RunMode.MarketDepth,
            "realtime-bars" => RunMode.RealtimeBars,
            "market-data-all" => RunMode.MarketDataAll,
            "historical-bars" => RunMode.HistoricalBars,
            "historical-bars-live" => RunMode.HistoricalBarsKeepUpToDate,
            "histogram" => RunMode.Histogram,
            "historical-ticks" => RunMode.HistoricalTicks,
            "head-timestamp" => RunMode.HeadTimestamp,
            "managed-accounts" => RunMode.ManagedAccounts,
            "family-codes" => RunMode.FamilyCodes,
            "account-updates" => RunMode.AccountUpdates,
            "account-updates-multi" => RunMode.AccountUpdatesMulti,
            "account-summary" => RunMode.AccountSummaryOnly,
            "positions-multi" => RunMode.PositionsMulti,
            "pnl-account" => RunMode.PnlAccount,
            "pnl-single" => RunMode.PnlSingle,
            "option-chains" => RunMode.OptionChains,
            "option-exercise" => RunMode.OptionExercise,
            "option-greeks" => RunMode.OptionGreeks,
            "crypto-permissions" => RunMode.CryptoPermissions,
            "crypto-contract" => RunMode.CryptoContract,
            "crypto-streaming" => RunMode.CryptoStreaming,
            "crypto-historical" => RunMode.CryptoHistorical,
            "crypto-order" => RunMode.CryptoOrder,
            "fa-allocation-groups" => RunMode.FaAllocationGroups,
            "fa-groups-profiles" => RunMode.FaGroupsProfiles,
            "fa-unification" => RunMode.FaUnification,
            "fa-model-portfolios" => RunMode.FaModelPortfolios,
            "fa-order" => RunMode.FaOrder,
            "fundamental-data" => RunMode.FundamentalData,
            "wsh-filters" => RunMode.WshFilters,
            "error-codes" => RunMode.ErrorCodes,
            "scanner-examples" => RunMode.ScannerExamples,
            "scanner-complex" => RunMode.ScannerComplex,
            "scanner-parameters" => RunMode.ScannerParameters,
            "scanner-workbench" => RunMode.ScannerWorkbench,
            "scanner-preview" => RunMode.ScannerPreview,
            "display-groups-query" => RunMode.DisplayGroupsQuery,
            "display-groups-subscribe" => RunMode.DisplayGroupsSubscribe,
            "display-groups-update" => RunMode.DisplayGroupsUpdate,
            "display-groups-unsubscribe" => RunMode.DisplayGroupsUnsubscribe,
            "strategy-replay" => RunMode.StrategyReplay,
            "strategy-live-v3" => RunMode.StrategyLiveV3,
            "positions-monitor-ui" => RunMode.PositionsMonitorUi,
            "backtest-run" => RunMode.BacktestRun,
            "backtest-sweep" => RunMode.BacktestSweep,
            "backtest-optimize" => RunMode.BacktestOptimize,
            "backtest-scan" => RunMode.BacktestScan,
            "backtest-live-sim" => RunMode.BacktestLiveSim,
            "backtest-compare" => RunMode.BacktestCompare,
            _ => throw new ArgumentException($"Unknown mode '{value}'. Use connect|orders|orders-all-open|positions|positions-monitor-1pct|positions-monitor-1pct-loop|positions-auto-replace-scan-loop|snapshot-all|contracts-validate|orders-dryrun|orders-place-sim|orders-cancel-sim|orders-whatif|top-data|market-depth|realtime-bars|market-data-all|historical-bars|historical-bars-live|histogram|historical-ticks|head-timestamp|managed-accounts|family-codes|account-updates|account-updates-multi|account-summary|positions-multi|pnl-account|pnl-single|option-chains|option-exercise|option-greeks|crypto-permissions|crypto-contract|crypto-streaming|crypto-historical|crypto-order|fa-allocation-groups|fa-groups-profiles|fa-unification|fa-model-portfolios|fa-order|fundamental-data|wsh-filters|error-codes|scanner-examples|scanner-complex|scanner-parameters|scanner-workbench|scanner-preview|display-groups-query|display-groups-subscribe|display-groups-update|display-groups-unsubscribe|strategy-replay|strategy-live-v3|positions-monitor-ui|backtest-run|backtest-sweep|backtest-optimize|backtest-scan|backtest-live-sim|backtest-compare.")
        };
    }
}
