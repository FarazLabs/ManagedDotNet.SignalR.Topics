namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders;

public static class Symbols
{
    /// <summary> Available symbols and their price ranges </summary>
    private static readonly Dictionary<string, Range> AllSymbols = new()
    {
        { "BTC/USDT",  new Range { Min = 70_000m, Max = 85_000m } },
        { "ETH/USDT",  new Range { Min = 2_200m,  Max = 2_800m } },
        { "SOL/USDT",  new Range { Min = 85m,     Max = 120m } },
        { "XRP/USDT",  new Range { Min = 1.10m,   Max = 1.60m } },
        { "DOGE/USDT", new Range { Min = 0.07m,   Max = 0.11m } },
    };

    public static string[] GetAll()
    {
        return AllSymbols.Keys.ToArray();
    }

    public static Range GetRange(string symbol)
    {
        return AllSymbols[symbol];
    }
}

public class Range
{
    public decimal Min { get; set; }
    public decimal Max { get; set; }
}
