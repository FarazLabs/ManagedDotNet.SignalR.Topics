namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;

public record SubscribeToSymbolCommand
{
    public string Symbol { get; set; }
}
