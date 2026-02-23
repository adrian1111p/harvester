using IBApi;

namespace Harvester.App.IBKR.Contracts;

public static class ContractFactory
{
    public static Contract Stock(string symbol, string exchange = "SMART", string currency = "USD", string primaryExchange = "NASDAQ")
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "STK",
            Exchange = exchange,
            Currency = currency,
            PrimaryExch = primaryExchange
        };
    }

    public static Contract Forex(string pair, string exchange = "IDEALPRO")
    {
        var normalized = pair.Replace("/", "", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 6)
        {
            throw new ArgumentException("Forex pair must have 6 letters, e.g. EURUSD or EUR/USD.");
        }

        return new Contract
        {
            Symbol = normalized[..3],
            SecType = "CASH",
            Currency = normalized[3..],
            Exchange = exchange
        };
    }

    public static Contract Future(string symbol, string yyyymm, string exchange, string currency = "USD")
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "FUT",
            LastTradeDateOrContractMonth = yyyymm,
            Exchange = exchange,
            Currency = currency
        };
    }

    public static Contract Option(
        string symbol,
        string yyyymmdd,
        double strike,
        string right,
        string exchange = "SMART",
        string currency = "USD",
        string multiplier = "100")
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "OPT",
            LastTradeDateOrContractMonth = yyyymmdd,
            Strike = strike,
            Right = right,
            Exchange = exchange,
            Currency = currency,
            Multiplier = multiplier
        };
    }

    public static Contract Cfd(string symbol, string exchange = "SMART", string currency = "USD")
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "CFD",
            Exchange = exchange,
            Currency = currency
        };
    }

    public static Contract Index(string symbol, string exchange, string currency = "USD")
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "IND",
            Exchange = exchange,
            Currency = currency
        };
    }

    public static Contract Crypto(string symbol, string exchange = "PAXOS", string currency = "USD")
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "CRYPTO",
            Exchange = exchange,
            Currency = currency
        };
    }

    public static Contract Bag(string symbol, string currency, string exchange, IEnumerable<ComboLeg> legs)
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "BAG",
            Currency = currency,
            Exchange = exchange,
            ComboLegs = legs.ToList()
        };
    }
}
