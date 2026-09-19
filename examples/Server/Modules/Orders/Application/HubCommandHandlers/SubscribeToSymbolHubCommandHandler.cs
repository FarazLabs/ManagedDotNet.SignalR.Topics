using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Infrastructure;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;
using ManagedDotNet.SignalR.Topics.Examples.Shared;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Services;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Core;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders;
using static ManagedDotNet.SignalR.Topics.Examples.Shared.Services.AuthService;

namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Application.HubCommandHandlers;


[Authorize(Roles = $"{Roles.User},{Roles.Administrator}")]
public class SubscribeToSymbolHubCommandHandler : IHubCommandHandler<SubscribeToSymbolCommand>
{
    private readonly ITopicHubContext<OrderBookHub> _hubContext;
    private readonly ILogger<SubscribeToSymbolHubCommandHandler> _logger;

    public SubscribeToSymbolHubCommandHandler
    (
        ITopicHubContext<OrderBookHub> hubContext,
        ILogger<SubscribeToSymbolHubCommandHandler> logger
    )
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(SubscribeToSymbolCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        if(string.IsNullOrEmpty(request.Symbol))
            throw new ArgumentException("Symbol is null or empty");

        string? group = Symbols.GetAll()
                    .FirstOrDefault(s => s.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));

        if (group == null)
        {
            throw new Exception($"Symbol not found: {request!.Symbol}");
        }

        // add the connection to the group
        await _hubContext.Groups.AddToGroupAsync(context.ConnectionId, group, cancellationToken);

    }
}
