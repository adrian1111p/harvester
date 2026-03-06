namespace Harvester.App.Strategy;

/// <summary>
/// Direction of an order action.
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>
/// Order execution type.
/// </summary>
public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit,
    MarketableLimit
}

/// <summary>
/// Order time-in-force policy.
/// </summary>
public enum OrderTimeInForce
{
    /// <summary>Good for the day session only.</summary>
    Day,
    /// <summary>Immediate or cancel — fill what you can, cancel the rest.</summary>
    Ioc,
    /// <summary>Fill or kill — fill entirely or cancel.</summary>
    Fok,
    /// <summary>Good till cancelled.</summary>
    Gtc,
    /// <summary>Day+ (extended hours).</summary>
    DayPlus
}

/// <summary>
/// Direction of a tracked position (long/short).
/// </summary>
public enum PositionSide
{
    Long,
    Short
}

/// <summary>
/// Conversion helpers between enums and their string wire-format representations.
/// </summary>
public static class OrderEnumExtensions
{
    // ── OrderSide ────────────────────────────────────────────────────────

    public static string ToWireString(this OrderSide side) => side switch
    {
        OrderSide.Buy => "BUY",
        OrderSide.Sell => "SELL",
        _ => "BUY"
    };

    public static OrderSide ParseOrderSide(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "BUY" => OrderSide.Buy,
            "SELL" => OrderSide.Sell,
            _ => OrderSide.Buy
        };

    // ── OrderType ────────────────────────────────────────────────────────

    public static string ToWireString(this OrderType type) => type switch
    {
        OrderType.Market => "MKT",
        OrderType.Limit => "LMT",
        OrderType.Stop => "STP",
        OrderType.StopLimit => "STP LMT",
        OrderType.MarketableLimit => "MARKETABLE_LIMIT",
        _ => "MKT"
    };

    public static OrderType ParseOrderType(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "MKT" or "MARKET" => OrderType.Market,
            "LMT" or "LIMIT" => OrderType.Limit,
            "STP" or "STOP" => OrderType.Stop,
            "STP LMT" or "STOP_LIMIT" or "STOPLIMIT" => OrderType.StopLimit,
            "MARKETABLE_LIMIT" => OrderType.MarketableLimit,
            _ => OrderType.Market
        };

    // ── OrderTimeInForce ─────────────────────────────────────────────────

    public static string ToWireString(this OrderTimeInForce tif) => tif switch
    {
        OrderTimeInForce.Day => "DAY",
        OrderTimeInForce.Ioc => "IOC",
        OrderTimeInForce.Fok => "FOK",
        OrderTimeInForce.Gtc => "GTC",
        OrderTimeInForce.DayPlus => "DAY+",
        _ => "DAY"
    };

    public static OrderTimeInForce ParseTimeInForce(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "DAY" => OrderTimeInForce.Day,
            "IOC" => OrderTimeInForce.Ioc,
            "FOK" => OrderTimeInForce.Fok,
            "GTC" => OrderTimeInForce.Gtc,
            "DAY+" => OrderTimeInForce.DayPlus,
            _ => OrderTimeInForce.Day
        };

    // ── PositionSide ─────────────────────────────────────────────────────

    public static string ToWireString(this PositionSide side) => side switch
    {
        PositionSide.Long => "LONG",
        PositionSide.Short => "SHORT",
        _ => "LONG"
    };

    public static PositionSide ParsePositionSide(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "LONG" or "BUY" => PositionSide.Long,
            "SHORT" or "SELL" => PositionSide.Short,
            _ => PositionSide.Long
        };

    /// <summary>Convert an OrderSide action to the resulting PositionSide.</summary>
    public static PositionSide ToPositionSide(this OrderSide side) => side switch
    {
        OrderSide.Buy => PositionSide.Long,
        OrderSide.Sell => PositionSide.Short,
        _ => PositionSide.Long
    };

    /// <summary>Get the closing OrderSide for a given PositionSide.</summary>
    public static OrderSide ClosingSide(this PositionSide side) => side switch
    {
        PositionSide.Long => OrderSide.Sell,
        PositionSide.Short => OrderSide.Buy,
        _ => OrderSide.Sell
    };
}
