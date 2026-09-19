using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using ManagedDotNet.SignalR.Topics.Abstractions;
using static ManagedDotNet.SignalR.Topics.Examples.Shared.Services.AuthService;

namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Application.HubCommandHandlers;

[Authorize(Roles = Roles.Administrator)]
public class TerminateHubCommandHandler : IHubCommandHandler<TerminateCommand>
{
    private readonly IHostApplicationLifetime _lifetime;

    public TerminateHubCommandHandler
    (
        IHostApplicationLifetime lifetime
    )
    {
        _lifetime = lifetime;
    }

    public Task Handle(TerminateCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        PrettyPrint.Info($"Terminate accepted — reason: {request.Reason}");
        // soft stop: let the host drain connections and hosted services
        _lifetime.StopApplication();
        return Task.CompletedTask;
    }
}
