namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;

public record UnsubscribeFromSymbolCommand
{
    public string Symbol { get; set; }
};
