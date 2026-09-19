namespace ManagedDotNet.SignalR.Topics.Examples.Shared.Models;

public record OrderBookUpdate
{
    public string Symbol { get; set; }
    public decimal Price { get; set; }
}
