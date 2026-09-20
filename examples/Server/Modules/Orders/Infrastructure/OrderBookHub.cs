using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using Microsoft.Extensions.Logging;
using ManagedDotNet.SignalR.Topics.Abstractions;

namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Infrastructure;

public class OrderBookHub : TopicHub
{
    private readonly ILogger<OrderBookHub> _logger;

    public OrderBookHub
    (
        ILogger<OrderBookHub> logger
    )
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        PrettyPrint.Info($"connected {Context.ConnectionId}");

        ConnectionAlert alert = new ConnectionAlert
        {
            Message = "Welcome! You are connected to the order book hub."
        };

        await Clients.Caller.Handle(alert);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        PrettyPrint.Info($"disconnected {Context.ConnectionId}");
    }
}
