using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Infrastructure;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Core;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders;
using static ManagedDotNet.SignalR.Topics.Examples.Shared.Services.AuthService;

namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Application.HubCommandHandlers;


[Authorize(Roles = $"{Roles.User},{Roles.Administrator}")]
public class UnsubscribeFromSymbolHubCommandHandler : IHubCommandHandler<UnsubscribeFromSymbolCommand>
{
    private readonly ITopicHubContext<OrderBookHub> _hubContext;
    private readonly ILogger<UnsubscribeFromSymbolHubCommandHandler> _logger;

    public UnsubscribeFromSymbolHubCommandHandler
    (
        ITopicHubContext<OrderBookHub> hubContext,
        ILogger<UnsubscribeFromSymbolHubCommandHandler> logger
    )
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(UnsubscribeFromSymbolCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        if(string.IsNullOrEmpty(request.Symbol))
            throw new ArgumentException("Symbol is null or empty");

        string? group = Symbols.GetAll()
                .FirstOrDefault(s => s.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));

        if (group == null)
            throw new Exception(message: $"Symbol {request.Symbol} not found");

        // remove the connection from the group
        await _hubContext.Groups.RemoveFromGroupAsync(context.ConnectionId, group, cancellationToken);

    }
}
