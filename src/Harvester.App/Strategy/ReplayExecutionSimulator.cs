using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record ReplayOrderIntent(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    string OrderType,
    double? LimitPrice,
    double? StopPrice,
    string TimeInForce,
    DateTime? ExpireAtUtc,
    string Source,
    string OrderId = "",
    string ParentOrderId = "",
    string OcoGroup = ""
);

public sealed record ReplayFillRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    double RequestedQuantity,
    double RemainingQuantity,
    bool IsPartial,
    DateTime SubmittedAtUtc,
    string OrderType,
    double FillPrice,
    double Commission,
    double RealizedPnlDelta,
    string Source
);

public sealed record ReplayPortfolioRow(
    DateTime TimestampUtc,
    string Symbol,
    double PositionQuantity,
    double AveragePrice,
    double MarketPrice,
    double Cash,
    double SettledCash,
    double UnsettledCash,
    double RealizedPnl,
    double UnrealizedPnl,
    double Equity
);

public sealed record ReplaySliceSimulationResult(
    IReadOnlyList<ReplayOrderIntent> Orders,
    IReadOnlyList<ReplayFillRow> Fills,
    IReadOnlyList<ReplayCorporateActionAppliedRow> AppliedCorporateActions,
    IReadOnlyList<ReplayDelistAppliedRow> AppliedDelists,
    IReadOnlyList<ReplayFinancingAppliedRow> AppliedFinancing,
    IReadOnlyList<ReplayLocateRejectionRow> LocateRejections,
    IReadOnlyList<ReplayMarginRejectionRow> MarginRejections,
    IReadOnlyList<ReplayMarginEventRow> MarginEvents,
    IReadOnlyList<ReplayCashSettlementRow> CashSettlements,
    IReadOnlyList<ReplayCashRejectionRow> CashRejections,
    IReadOnlyList<ReplayOrderActivationRow> Activations,
    IReadOnlyList<ReplayOrderTriggerRow> Triggers,
    IReadOnlyList<ReplayOrderCancellationRow> Cancellations,
    ReplayPortfolioRow Portfolio
);

public sealed record ReplayOrderActivationRow(
    DateTime TimestampUtc,
    string OrderId,
    string ParentOrderId,
    string Symbol,
    string Side,
    double Quantity,
    string ActivationReason,
    string Source
);

public sealed record ReplayOrderTriggerRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    double StopPrice,
    double? LimitPrice,
    DateTime SubmittedAtUtc,
    string Source
);

public sealed record ReplayOrderCancellationRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    string OrderType,
    string TimeInForce,
    DateTime SubmittedAtUtc,
    DateTime? ExpireAtUtc,
    string CancelReason,
    string Source
);

public sealed record ReplayCorporateActionAppliedRow(
    DateTime TimestampUtc,
    string Symbol,
    string ActionType,
    double SplitRatio,
    double CashAmount,
    double CashDelta,
    double PositionQuantity,
    double AveragePrice,
    string Source
);

public sealed record ReplayDelistAppliedRow(
    DateTime TimestampUtc,
    string Symbol,
    bool IsTerminal,
    double PositionQuantityBefore,
    double PositionQuantityAfter,
    double FillPrice,
    double CashAfter,
    string Source
);

public sealed record ReplayFinancingAppliedRow(
    DateTime TimestampUtc,
    string Symbol,
    string FinancingType,
    double PositionQuantity,
    double MarketPrice,
    double RateBps,
    double QuantityApplied,
    double CashDelta,
    double CashAfter,
    string Source
);

public sealed record ReplayLocateRejectionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    string Reason,
    bool LocateAvailable,
    double LocateFeePerShare,
    string Source
);

public sealed record ReplayMarginRejectionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    double FillPrice,
    double ProjectedEquity,
    double ProjectedInitialMargin,
    string Reason,
    string Source
);

public sealed record ReplayMarginEventRow(
    DateTime TimestampUtc,
    string Symbol,
    string EventType,
    double Equity,
    double MaintenanceRequirement,
    double PositionQuantity,
    double MarketPrice,
    double CashAfter,
    string Source
);

public sealed record ReplayCashSettlementRow(
    DateTime TimestampUtc,
    string Symbol,
    string EventType,
    double Amount,
    double SettledCash,
    double UnsettledCash,
    DateTime? SettleDateUtc,
    string Source
);

public sealed record ReplayCashRejectionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    double RequiredSettledCash,
    double AvailableSettledCash,
    string Reason,
    string Source
);

public sealed class ReplayExecutionSimulator
{
    private readonly double _commissionPerUnit;
    private readonly double _slippageBps;
    private double _cash;
    private double _positionQuantity;
    private double _averagePrice;
    private double _realizedPnl;
    private readonly IReadOnlyList<ReplayCorporateActionRow> _corporateActions;
    private int _corporateActionCursor;
    private readonly ReplayPriceNormalizationMode _normalizationMode;
    private readonly double _initialMarginRate;
    private readonly double _maintenanceMarginRate;
    private readonly double _secFeeRatePerDollar;
    private readonly double _tafFeePerShare;
    private readonly double _tafFeeCapPerOrder;
    private readonly double _exchangeFeePerShare;
    private readonly double _maxFillParticipationRate;
    private readonly bool _enforceQueuePriority;
    private readonly int _settlementLagDays;
    private readonly bool _enforceSettledCash;
    private double _settledCash;
    private readonly List<PendingSettlement> _pendingSettlements;
    private readonly List<ActiveReplayOrder> _activeOrders;
    private readonly HashSet<string> _filledOrderIds;
    private int _generatedOrderIdSeed;
    private DateTime? _lastFinancingTimestampUtc;

    public ReplayExecutionSimulator(
        double initialCash,
        double commissionPerUnit,
        double slippageBps,
        IReadOnlyList<ReplayCorporateActionRow> corporateActions,
        ReplayPriceNormalizationMode normalizationMode,
        double initialMarginRate,
        double maintenanceMarginRate,
        double secFeeRatePerDollar,
        double tafFeePerShare,
        double tafFeeCapPerOrder,
        double exchangeFeePerShare,
        double maxFillParticipationRate,
        bool enforceQueuePriority,
        int settlementLagDays,
        bool enforceSettledCash)
    {
        _cash = initialCash;
        _settledCash = initialCash;
        _commissionPerUnit = Math.Max(0, commissionPerUnit);
        _slippageBps = Math.Max(0, slippageBps);
        _corporateActions = corporateActions
            .OrderBy(x => x.EffectiveTimestampUtc)
            .ToArray();
        _normalizationMode = normalizationMode;
        _initialMarginRate = Math.Max(0, initialMarginRate);
        _maintenanceMarginRate = Math.Max(0, maintenanceMarginRate);
        _secFeeRatePerDollar = Math.Max(0, secFeeRatePerDollar);
        _tafFeePerShare = Math.Max(0, tafFeePerShare);
        _tafFeeCapPerOrder = Math.Max(0, tafFeeCapPerOrder);
        _exchangeFeePerShare = Math.Max(0, exchangeFeePerShare);
        _maxFillParticipationRate = Math.Clamp(maxFillParticipationRate, 0, 1);
        _enforceQueuePriority = enforceQueuePriority;
        _settlementLagDays = Math.Max(0, settlementLagDays);
        _enforceSettledCash = enforceSettledCash;
        _pendingSettlements = [];
        _activeOrders = [];
        _filledOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _generatedOrderIdSeed = 0;
        _corporateActionCursor = 0;
        _lastFinancingTimestampUtc = null;
    }

    public static IReadOnlyList<ReplayOrderIntent> LoadOrderIntents(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay orders input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ReplayOrderIntent[]>(File.ReadAllText(fullPath)) ?? [];
        return rows
            .Where(r => r.TimestampUtc != default)
            .OrderBy(r => r.TimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }

    public ReplaySliceSimulationResult ProcessSlice(
        StrategyDataSlice slice,
        string defaultSymbol,
        IReadOnlyList<ReplayOrderIntent> intents,
        IReadOnlyList<ReplayDelistEventRow> dueDelistEvents,
        ReplayBorrowLocateProfileRow borrowLocateProfile)
    {
        var symbol = string.IsNullOrWhiteSpace(defaultSymbol)
            ? (slice.TopTicks.FirstOrDefault()?.Value ?? "N/A")
            : defaultSymbol;

        var fills = new List<ReplayFillRow>();
        var accepted = new List<ReplayOrderIntent>();
        var appliedActions = ApplyCorporateActions(slice.TimestampUtc, symbol);
        var appliedDelists = new List<ReplayDelistAppliedRow>();
        var appliedFinancing = new List<ReplayFinancingAppliedRow>();
        var locateRejections = new List<ReplayLocateRejectionRow>();
        var marginRejections = new List<ReplayMarginRejectionRow>();
        var marginEvents = new List<ReplayMarginEventRow>();
        var cashSettlements = ApplyDueSettlements(slice.TimestampUtc, symbol);
        var cashRejections = new List<ReplayCashRejectionRow>();
        var activations = new List<ReplayOrderActivationRow>();
        var triggers = new List<ReplayOrderTriggerRow>();
        var cancellations = new List<ReplayOrderCancellationRow>();

        var bar = slice.HistoricalBars.FirstOrDefault();
        var markPrice = bar is not null
            ? bar.Close
            : (slice.TopTicks.FirstOrDefault()?.Price ?? 0);
        var availableSliceQuantity = bar is null
            ? double.MaxValue
            : Math.Max(0, (double)bar.Volume * _maxFillParticipationRate);

        var borrowCharge = ApplyBorrowFinancing(slice.TimestampUtc, symbol, markPrice, borrowLocateProfile);
        if (borrowCharge is not null)
        {
            appliedFinancing.Add(borrowCharge);
        }

        if (dueDelistEvents.Count > 0)
        {
            foreach (var delist in dueDelistEvents)
            {
                if (!delist.IsTerminal)
                {
                    appliedDelists.Add(new ReplayDelistAppliedRow(
                        delist.EffectiveTimestampUtc,
                        delist.Symbol,
                        false,
                        _positionQuantity,
                        _positionQuantity,
                        markPrice,
                        _cash,
                        delist.Source));
                    continue;
                }

                var forced = ApplyTerminalDelistLiquidation(slice.TimestampUtc, symbol, markPrice, delist.Source);
                if (forced is not null)
                {
                    accepted.Add(forced.Value.Order);
                    fills.Add(forced.Value.Fill);
                }

                appliedDelists.Add(new ReplayDelistAppliedRow(
                    delist.EffectiveTimestampUtc,
                    delist.Symbol,
                    true,
                    forced?.PositionQuantityBefore ?? _positionQuantity,
                    _positionQuantity,
                    markPrice,
                    _cash,
                    delist.Source));
            }
        }

        foreach (var intent in intents)
        {
            if (intent.Quantity <= 0)
            {
                continue;
            }

            var normalized = intent with
            {
                TimestampUtc = intent.TimestampUtc == default ? slice.TimestampUtc : intent.TimestampUtc,
                Side = intent.Side.ToUpperInvariant(),
                OrderType = intent.OrderType.ToUpperInvariant(),
                TimeInForce = string.IsNullOrWhiteSpace(intent.TimeInForce) ? "DAY" : intent.TimeInForce.ToUpperInvariant(),
                OrderId = ResolveOrderId(intent.OrderId),
                ParentOrderId = string.IsNullOrWhiteSpace(intent.ParentOrderId) ? string.Empty : intent.ParentOrderId.Trim(),
                OcoGroup = string.IsNullOrWhiteSpace(intent.OcoGroup) ? string.Empty : intent.OcoGroup.Trim(),
                Source = string.IsNullOrWhiteSpace(intent.Source) ? "strategy" : intent.Source
            };

            accepted.Add(normalized);
            var isActive = string.IsNullOrWhiteSpace(normalized.ParentOrderId)
                || _filledOrderIds.Contains(normalized.ParentOrderId);
            _activeOrders.Add(new ActiveReplayOrder(normalized, normalized.TimestampUtc, normalized.Quantity, isActive));
        }

        for (var index = _activeOrders.Count - 1; index >= 0; index--)
        {
            var active = _activeOrders[index];
            if (IsOrderExpired(active, slice.TimestampUtc))
            {
                cancellations.Add(new ReplayOrderCancellationRow(
                    slice.TimestampUtc,
                    active.Intent.Symbol,
                    active.Intent.Side,
                    active.RemainingQuantity,
                    active.Intent.OrderType,
                    active.Intent.TimeInForce,
                    active.SubmittedAtUtc,
                    active.Intent.ExpireAtUtc,
                    "EXPIRED",
                    active.Intent.Source));
                _activeOrders.RemoveAt(index);
                continue;
            }
        }

        var orderedActiveOrders = _enforceQueuePriority
            ? _activeOrders.OrderBy(x => x.SubmittedAtUtc).ToArray()
            : _activeOrders.ToArray();

        foreach (var active in orderedActiveOrders)
        {
            if (availableSliceQuantity <= 0)
            {
                break;
            }

            if (!_activeOrders.Contains(active) || !active.IsActive)
            {
                continue;
            }

            if (TryTriggerStopOrder(active, slice.TimestampUtc, bar, markPrice, out var triggerRow))
            {
                triggers.Add(triggerRow);
            }

            if (!CanExecuteOnSlice(active))
            {
                continue;
            }

            var executableIntent = GetExecutableIntent(active, slice.TimestampUtc);
            var fillPrice = ResolveFillPrice(executableIntent, bar, markPrice);
            if (fillPrice is null)
            {
                continue;
            }

            var fillQuantity = Math.Min(active.RemainingQuantity, availableSliceQuantity);
            if (fillQuantity <= 0)
            {
                continue;
            }

            executableIntent = executableIntent with
            {
                TimestampUtc = slice.TimestampUtc,
                Quantity = fillQuantity
            };

            var locateValidation = ValidateLocateAndApplyFee(executableIntent, fillPrice.Value, borrowLocateProfile);
            if (locateValidation.Rejected is not null)
            {
                locateRejections.Add(locateValidation.Rejected);
                _activeOrders.Remove(active);
                continue;
            }

            var cashValidation = ValidateSettledCash(executableIntent, fillPrice.Value);
            if (cashValidation is not null)
            {
                cashRejections.Add(cashValidation);
                _activeOrders.Remove(active);
                continue;
            }

            if (locateValidation.FeeApplied is not null)
            {
                appliedFinancing.Add(locateValidation.FeeApplied);
            }

            var marginValidation = ValidateInitialMargin(executableIntent, fillPrice.Value);
            if (marginValidation is not null)
            {
                marginRejections.Add(marginValidation);
                _activeOrders.Remove(active);
                continue;
            }

            var fill = ApplyFill(executableIntent, fillPrice.Value, active.SubmittedAtUtc, active.Intent.Quantity, active.RemainingQuantity - fillQuantity);
            fills.Add(fill);
            active.RemainingQuantity -= fillQuantity;
            availableSliceQuantity -= fillQuantity;

            if (active.RemainingQuantity <= 1e-9)
            {
                _filledOrderIds.Add(active.Intent.OrderId);
                _activeOrders.Remove(active);
                ActivateChildren(active.Intent.OrderId, slice.TimestampUtc, activations);
            }

            CancelOcoSiblings(active, slice.TimestampUtc, cancellations);
        }

        var maintenanceEvent = ApplyMaintenanceMarginGuard(slice.TimestampUtc, symbol, markPrice);
        if (maintenanceEvent is not null)
        {
            marginEvents.Add(maintenanceEvent.Value.Event);
            if (maintenanceEvent.Value.ForcedOrder is not null)
            {
                accepted.Add(maintenanceEvent.Value.ForcedOrder.Value.Order);
                fills.Add(maintenanceEvent.Value.ForcedOrder.Value.Fill);
            }
        }

        _lastFinancingTimestampUtc = slice.TimestampUtc;

        var unrealizedPnl = (_positionQuantity == 0 || markPrice <= 0)
            ? 0
            : (markPrice - _averagePrice) * _positionQuantity;

        var equity = _cash + (_positionQuantity * markPrice);
        var portfolio = new ReplayPortfolioRow(
            slice.TimestampUtc,
            symbol,
            _positionQuantity,
            _averagePrice,
            markPrice,
            _cash,
            _settledCash,
            GetUnsettledCash(),
            _realizedPnl,
            unrealizedPnl,
            equity);

        return new ReplaySliceSimulationResult(accepted, fills, appliedActions, appliedDelists, appliedFinancing, locateRejections, marginRejections, marginEvents, cashSettlements, cashRejections, activations, triggers, cancellations, portfolio);
    }

    private void ActivateChildren(string filledOrderId, DateTime timestampUtc, List<ReplayOrderActivationRow> activations)
    {
        if (string.IsNullOrWhiteSpace(filledOrderId))
        {
            return;
        }

        foreach (var child in _activeOrders.Where(x => !x.IsActive && string.Equals(x.Intent.ParentOrderId, filledOrderId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            child.IsActive = true;
            activations.Add(new ReplayOrderActivationRow(
                timestampUtc,
                child.Intent.OrderId,
                child.Intent.ParentOrderId,
                child.Intent.Symbol,
                child.Intent.Side,
                child.RemainingQuantity,
                "PARENT_FILLED",
                child.Intent.Source));
        }
    }

    private void CancelOcoSiblings(ActiveReplayOrder filledOrder, DateTime timestampUtc, List<ReplayOrderCancellationRow> cancellations)
    {
        if (string.IsNullOrWhiteSpace(filledOrder.Intent.OcoGroup))
        {
            return;
        }

        var siblings = _activeOrders
            .Where(x => !ReferenceEquals(x, filledOrder))
            .Where(x => x.RemainingQuantity > 1e-9)
            .Where(x => string.Equals(x.Intent.OcoGroup, filledOrder.Intent.OcoGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var sibling in siblings)
        {
            cancellations.Add(new ReplayOrderCancellationRow(
                timestampUtc,
                sibling.Intent.Symbol,
                sibling.Intent.Side,
                sibling.RemainingQuantity,
                sibling.Intent.OrderType,
                sibling.Intent.TimeInForce,
                sibling.SubmittedAtUtc,
                sibling.Intent.ExpireAtUtc,
                "OCO_SIBLING_FILLED",
                sibling.Intent.Source));
            _activeOrders.Remove(sibling);
        }
    }

    private static bool CanExecuteOnSlice(ActiveReplayOrder active)
    {
        return !IsStopOrderType(active.Intent.OrderType) || active.IsTriggered;
    }

    private static ReplayOrderIntent GetExecutableIntent(ActiveReplayOrder active, DateTime timestampUtc)
    {
        if (!active.IsTriggered || !IsStopOrderType(active.Intent.OrderType))
        {
            return active.Intent with { TimestampUtc = timestampUtc };
        }

        if (IsStopLimitOrderType(active.Intent.OrderType))
        {
            return active.Intent with
            {
                TimestampUtc = timestampUtc,
                OrderType = "LMT"
            };
        }

        return active.Intent with
        {
            TimestampUtc = timestampUtc,
            OrderType = "MKT",
            LimitPrice = null
        };
    }

    private static bool TryTriggerStopOrder(
        ActiveReplayOrder active,
        DateTime timestampUtc,
        HistoricalBarRow? bar,
        double markPrice,
        out ReplayOrderTriggerRow triggerRow)
    {
        triggerRow = null!;
        if (active.IsTriggered || !IsStopOrderType(active.Intent.OrderType) || !active.Intent.StopPrice.HasValue || active.Intent.StopPrice <= 0)
        {
            return false;
        }

        var side = ParseSide(active.Intent.Side);
        if (side == 0)
        {
            return false;
        }

        var stop = active.Intent.StopPrice.Value;
        var isTriggered = bar is not null
            ? (side > 0 ? bar.High >= stop : bar.Low <= stop)
            : (side > 0 ? markPrice >= stop : markPrice <= stop);

        if (!isTriggered)
        {
            return false;
        }

        active.IsTriggered = true;
        active.TriggeredAtUtc = timestampUtc;
        triggerRow = new ReplayOrderTriggerRow(
            timestampUtc,
            active.Intent.Symbol,
            active.Intent.Side,
            active.Intent.OrderType,
            stop,
            active.Intent.LimitPrice,
            active.SubmittedAtUtc,
            active.Intent.Source);
        return true;
    }

    private static bool IsStopOrderType(string orderType)
    {
        if (string.IsNullOrWhiteSpace(orderType))
        {
            return false;
        }

        var normalized = orderType.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        return normalized is "STP" or "STOP" or "STOPMARKET" or "STPLMT" or "STOPLIMIT";
    }

    private static bool IsStopLimitOrderType(string orderType)
    {
        if (string.IsNullOrWhiteSpace(orderType))
        {
            return false;
        }

        var normalized = orderType.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        return normalized is "STPLMT" or "STOPLIMIT";
    }

    private static bool IsOrderExpired(ActiveReplayOrder order, DateTime sliceTimestampUtc)
    {
        if (order.Intent.ExpireAtUtc.HasValue && order.Intent.ExpireAtUtc.Value <= sliceTimestampUtc)
        {
            return true;
        }

        var tif = string.IsNullOrWhiteSpace(order.Intent.TimeInForce) ? "DAY" : order.Intent.TimeInForce;
        if (string.Equals(tif, "GTC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return order.SubmittedAtUtc.Date < sliceTimestampUtc.Date;
    }

    private IReadOnlyList<ReplayCashSettlementRow> ApplyDueSettlements(DateTime timestampUtc, string symbol)
    {
        if (_pendingSettlements.Count == 0)
        {
            return [];
        }

        var applied = new List<ReplayCashSettlementRow>();
        for (var index = _pendingSettlements.Count - 1; index >= 0; index--)
        {
            var row = _pendingSettlements[index];
            if (row.SettleDateUtc > timestampUtc)
            {
                continue;
            }

            _settledCash += row.Amount;
            applied.Add(new ReplayCashSettlementRow(
                timestampUtc,
                row.Symbol,
                "SETTLED",
                row.Amount,
                _settledCash,
                GetUnsettledCashExcluding(index),
                row.SettleDateUtc,
                row.Source));
            _pendingSettlements.RemoveAt(index);
        }

        return applied.OrderBy(x => x.SettleDateUtc).ToArray();
    }

    private ReplayCashRejectionRow? ValidateSettledCash(ReplayOrderIntent intent, double fillPrice)
    {
        if (!_enforceSettledCash)
        {
            return null;
        }

        var side = ParseSide(intent.Side);
        if (side <= 0)
        {
            return null;
        }

        var required = (intent.Quantity * fillPrice) + (intent.Quantity * _commissionPerUnit);
        if (_settledCash + 1e-9 >= required)
        {
            return null;
        }

        return new ReplayCashRejectionRow(
            intent.TimestampUtc,
            intent.Symbol,
            intent.Side,
            intent.Quantity,
            required,
            _settledCash,
            "INSUFFICIENT_SETTLED_CASH",
            intent.Source);
    }

    private ReplayMarginRejectionRow? ValidateInitialMargin(ReplayOrderIntent intent, double fillPrice)
    {
        if (_initialMarginRate <= 0)
        {
            return null;
        }

        var side = ParseSide(intent.Side);
        if (side == 0)
        {
            return null;
        }

        var signedQuantity = side * intent.Quantity;
        var commission = intent.Quantity * _commissionPerUnit;
        var projectedCash = _cash - (signedQuantity * fillPrice) - commission;
        var projectedPosition = _positionQuantity + signedQuantity;
        var projectedEquity = projectedCash + (projectedPosition * fillPrice);
        var projectedInitialMargin = Math.Abs(projectedPosition * fillPrice) * _initialMarginRate;

        if (projectedEquity + 1e-9 >= projectedInitialMargin)
        {
            return null;
        }

        return new ReplayMarginRejectionRow(
            intent.TimestampUtc,
            intent.Symbol,
            intent.Side,
            intent.Quantity,
            fillPrice,
            projectedEquity,
            projectedInitialMargin,
            "INITIAL_MARGIN_BREACH",
            intent.Source);
    }

    private (ReplayMarginEventRow Event, (ReplayOrderIntent Order, ReplayFillRow Fill)? ForcedOrder)? ApplyMaintenanceMarginGuard(
        DateTime timestampUtc,
        string symbol,
        double markPrice)
    {
        if (_maintenanceMarginRate <= 0 || _positionQuantity == 0 || markPrice <= 0)
        {
            return null;
        }

        var equity = _cash + (_positionQuantity * markPrice);
        var maintenanceRequirement = Math.Abs(_positionQuantity * markPrice) * _maintenanceMarginRate;
        if (equity + 1e-9 >= maintenanceRequirement)
        {
            return null;
        }

        var positionBefore = _positionQuantity;
        var forcedSide = _positionQuantity > 0 ? "SELL" : "BUY";
        var forcedOrder = new ReplayOrderIntent(
            timestampUtc,
            symbol,
            forcedSide,
            Math.Abs(_positionQuantity),
            "MKT",
            null,
            null,
            "DAY",
            null,
            "margin");
        var forcedFill = ApplyFill(forcedOrder, markPrice, timestampUtc, forcedOrder.Quantity, 0);

        var eventRow = new ReplayMarginEventRow(
            timestampUtc,
            symbol,
            "MAINTENANCE_MARGIN_LIQUIDATION",
            equity,
            maintenanceRequirement,
            positionBefore,
            markPrice,
            _cash,
            "margin");

        return (eventRow, (forcedOrder, forcedFill));
    }

    private ReplayFinancingAppliedRow? ApplyBorrowFinancing(
        DateTime timestampUtc,
        string symbol,
        double markPrice,
        ReplayBorrowLocateProfileRow profile)
    {
        if (_lastFinancingTimestampUtc is null)
        {
            return null;
        }

        if (_positionQuantity >= 0 || markPrice <= 0 || profile.BorrowRateBps <= 0)
        {
            return null;
        }

        var elapsedDays = (timestampUtc - _lastFinancingTimestampUtc.Value).TotalDays;
        if (elapsedDays <= 0)
        {
            return null;
        }

        var shortShares = Math.Abs(_positionQuantity);
        var rate = profile.BorrowRateBps / 10000.0;
        var charge = shortShares * markPrice * rate * (elapsedDays / 365.0);
        if (charge <= 0)
        {
            return null;
        }

        _cash -= charge;
        return new ReplayFinancingAppliedRow(
            timestampUtc,
            symbol,
            "BORROW",
            _positionQuantity,
            markPrice,
            profile.BorrowRateBps,
            shortShares,
            -charge,
            _cash,
            profile.Source);
    }

    private (ReplayFinancingAppliedRow? FeeApplied, ReplayLocateRejectionRow? Rejected) ValidateLocateAndApplyFee(
        ReplayOrderIntent intent,
        double fillPrice,
        ReplayBorrowLocateProfileRow profile)
    {
        var side = ParseSide(intent.Side);
        if (side >= 0)
        {
            return (null, null);
        }

        var signedQuantity = side * intent.Quantity;
        var projected = _positionQuantity + signedQuantity;
        var currentShort = Math.Max(0, -_positionQuantity);
        var projectedShort = Math.Max(0, -projected);
        var incrementalShort = projectedShort - currentShort;

        if (incrementalShort <= 0)
        {
            return (null, null);
        }

        if (!profile.LocateAvailable)
        {
            return (
                null,
                new ReplayLocateRejectionRow(
                    intent.TimestampUtc,
                    intent.Symbol,
                    intent.Side,
                    intent.Quantity,
                    "LOCATE_UNAVAILABLE",
                    profile.LocateAvailable,
                    profile.LocateFeePerShare,
                    profile.Source));
        }

        if (profile.LocateFeePerShare <= 0)
        {
            return (null, null);
        }

        var fee = incrementalShort * profile.LocateFeePerShare;
        _cash -= fee;
        return (
            new ReplayFinancingAppliedRow(
                intent.TimestampUtc,
                intent.Symbol,
                "LOCATE_FEE",
                _positionQuantity,
                fillPrice,
                0,
                incrementalShort,
                -fee,
                _cash,
                profile.Source),
            null);
    }

    private (ReplayOrderIntent Order, ReplayFillRow Fill, double PositionQuantityBefore)? ApplyTerminalDelistLiquidation(
        DateTime timestampUtc,
        string symbol,
        double markPrice,
        string source)
    {
        if (_positionQuantity == 0 || markPrice <= 0)
        {
            return null;
        }

        var quantityBefore = _positionQuantity;
        var forcedSide = _positionQuantity > 0 ? "SELL" : "BUY";
        var forcedOrder = new ReplayOrderIntent(
            timestampUtc,
            symbol,
            forcedSide,
            Math.Abs(_positionQuantity),
            "MKT",
            null,
            null,
            "DAY",
            null,
            string.IsNullOrWhiteSpace(source) ? "delist" : source);

        var fill = ApplyFill(forcedOrder, markPrice, timestampUtc, forcedOrder.Quantity, 0);
        return (forcedOrder, fill, quantityBefore);
    }

    private IReadOnlyList<ReplayCorporateActionAppliedRow> ApplyCorporateActions(DateTime timestampUtc, string symbol)
    {
        var applied = new List<ReplayCorporateActionAppliedRow>();

        while (_corporateActionCursor < _corporateActions.Count && _corporateActions[_corporateActionCursor].EffectiveTimestampUtc <= timestampUtc)
        {
            var action = _corporateActions[_corporateActionCursor];
            _corporateActionCursor++;

            if (!string.Equals(action.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cashDelta = 0.0;
            if (action.ActionType == "SPLIT" && action.SplitRatio > 0)
            {
                _positionQuantity *= action.SplitRatio;
                if (_averagePrice > 0)
                {
                    _averagePrice /= action.SplitRatio;
                }
            }
            else if (action.ActionType == "DIVIDEND" && action.CashAmount > 0 && _normalizationMode == ReplayPriceNormalizationMode.TotalReturn)
            {
                cashDelta = _positionQuantity * action.CashAmount;
                _cash += cashDelta;
            }

            applied.Add(new ReplayCorporateActionAppliedRow(
                action.EffectiveTimestampUtc,
                action.Symbol,
                action.ActionType,
                action.SplitRatio,
                action.CashAmount,
                cashDelta,
                _positionQuantity,
                _averagePrice,
                action.Source));
        }

        return applied;
    }

    private double? ResolveFillPrice(ReplayOrderIntent intent, HistoricalBarRow? bar, double markPrice)
    {
        var side = ParseSide(intent.Side);
        if (side == 0)
        {
            return null;
        }

        if (string.Equals(intent.OrderType, "LMT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent.OrderType, "LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            if (!intent.LimitPrice.HasValue || intent.LimitPrice <= 0)
            {
                return null;
            }

            var limit = intent.LimitPrice.Value;
            if (bar is null)
            {
                var canFillNoBar = side > 0 ? markPrice <= limit : markPrice >= limit;
                return canFillNoBar ? limit : null;
            }

            var canFill = side > 0
                ? bar.Low <= limit
                : bar.High >= limit;
            return canFill ? limit : null;
        }

        if (markPrice <= 0)
        {
            return null;
        }

        var slippageFactor = 1 + (side * (_slippageBps / 10000.0));
        return markPrice * slippageFactor;
    }

    private ReplayFillRow ApplyFill(
        ReplayOrderIntent intent,
        double fillPrice,
        DateTime submittedAtUtc,
        double requestedQuantity,
        double remainingQuantity)
    {
        var side = ParseSide(intent.Side);
        var signedQuantity = side * intent.Quantity;
        var baseCommission = intent.Quantity * _commissionPerUnit;
        var secFee = side < 0 ? (intent.Quantity * fillPrice * _secFeeRatePerDollar) : 0.0;
        var tafFeeRaw = side < 0 ? (intent.Quantity * _tafFeePerShare) : 0.0;
        var tafFee = _tafFeeCapPerOrder > 0 ? Math.Min(tafFeeRaw, _tafFeeCapPerOrder) : tafFeeRaw;
        var exchangeFee = intent.Quantity * _exchangeFeePerShare;
        var commission = baseCommission + secFee + tafFee + exchangeFee;

        var realizedDelta = 0.0;
        if (_positionQuantity != 0 && Math.Sign(_positionQuantity) != Math.Sign(signedQuantity))
        {
            var closingQuantity = Math.Min(Math.Abs(_positionQuantity), Math.Abs(signedQuantity));
            realizedDelta = (fillPrice - _averagePrice) * closingQuantity * Math.Sign(_positionQuantity);
            _realizedPnl += realizedDelta;
        }

        _cash -= signedQuantity * fillPrice;
        _cash -= commission;

        var previousQuantity = _positionQuantity;
        var nextQuantity = previousQuantity + signedQuantity;

        if (previousQuantity == 0 || Math.Sign(previousQuantity) == Math.Sign(signedQuantity))
        {
            var newAbs = Math.Abs(nextQuantity);
            if (newAbs > 0)
            {
                _averagePrice = ((Math.Abs(previousQuantity) * _averagePrice) + (Math.Abs(signedQuantity) * fillPrice)) / newAbs;
            }
            else
            {
                _averagePrice = 0;
            }
        }
        else
        {
            if (nextQuantity == 0)
            {
                _averagePrice = 0;
            }
            else if (Math.Sign(nextQuantity) != Math.Sign(previousQuantity))
            {
                _averagePrice = fillPrice;
            }
        }

        _positionQuantity = nextQuantity;
        ApplySettlementEffects(intent, fillPrice, commission);

        return new ReplayFillRow(
            intent.TimestampUtc,
            intent.Symbol,
            intent.Side,
            intent.Quantity,
            requestedQuantity,
            remainingQuantity,
            remainingQuantity > 1e-9,
            submittedAtUtc,
            intent.OrderType,
            fillPrice,
            commission,
            realizedDelta,
            intent.Source);
    }

    private void ApplySettlementEffects(ReplayOrderIntent intent, double fillPrice, double commission)
    {
        var side = ParseSide(intent.Side);
        var notional = intent.Quantity * fillPrice;

        if (side > 0)
        {
            _settledCash -= (notional + commission);
            return;
        }

        if (side < 0)
        {
            _settledCash -= commission;
            if (_settlementLagDays == 0)
            {
                _settledCash += notional;
            }
            else
            {
                _pendingSettlements.Add(new PendingSettlement(
                    intent.TimestampUtc.AddDays(_settlementLagDays),
                    intent.Symbol,
                    notional,
                    string.IsNullOrWhiteSpace(intent.Source) ? "trade" : intent.Source));
            }
        }
    }

    private double GetUnsettledCash()
    {
        return _pendingSettlements.Sum(x => x.Amount);
    }

    private double GetUnsettledCashExcluding(int excludedIndex)
    {
        var unsettled = 0.0;
        for (var index = 0; index < _pendingSettlements.Count; index++)
        {
            if (index == excludedIndex)
            {
                continue;
            }

            unsettled += _pendingSettlements[index].Amount;
        }

        return unsettled;
    }

    private string ResolveOrderId(string orderId)
    {
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            return orderId.Trim();
        }

        _generatedOrderIdSeed++;
        return $"replay-{_generatedOrderIdSeed}";
    }

    private static int ParseSide(string side)
    {
        return side.ToUpperInvariant() switch
        {
            "BUY" => 1,
            "SELL" => -1,
            _ => 0
        };
    }

    private sealed record PendingSettlement(
        DateTime SettleDateUtc,
        string Symbol,
        double Amount,
        string Source
    );

    private sealed class ActiveReplayOrder
    {
        public ActiveReplayOrder(ReplayOrderIntent intent, DateTime submittedAtUtc, double remainingQuantity, bool isActive)
        {
            Intent = intent;
            SubmittedAtUtc = submittedAtUtc;
            RemainingQuantity = remainingQuantity;
            IsActive = isActive;
            IsTriggered = false;
            TriggeredAtUtc = null;
        }

        public ReplayOrderIntent Intent { get; }
        public DateTime SubmittedAtUtc { get; }
        public double RemainingQuantity { get; set; }
        public bool IsActive { get; set; }
        public bool IsTriggered { get; set; }
        public DateTime? TriggeredAtUtc { get; set; }
    }
}
