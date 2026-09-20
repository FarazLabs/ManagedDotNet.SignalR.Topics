using System.Collections.Concurrent;
using System.Security.Claims;
using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace SignalR.Topics.Tests.Fixtures;

internal sealed class PingHub : TopicHub
{
}

internal sealed class OtherHub : TopicHub
{
}

internal sealed class PingCommand
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class PlainTextCommand
{
    public string Text { get; set; } = string.Empty;
}

internal class OutboundUpdate
{
    public string Symbol { get; set; } = string.Empty;
}

internal sealed class DerivedOutboundUpdate : OutboundUpdate
{
}

internal sealed class UnregisteredOutbound
{
    public string Symbol { get; set; } = string.Empty;
}

internal sealed class HandlerCapture
{
    public ConcurrentQueue<(object Command, string? ConnectionId)> Invocations { get; } = new();
}

internal sealed class PingHandler : IHubCommandHandler<PingCommand>
{
    private readonly HandlerCapture _capture;

    public PingHandler
    (
        HandlerCapture capture
    )
    {
        _capture = capture;
    }

    public Task Handle(PingCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        _capture.Invocations.Enqueue((request, context.ConnectionId));
        return Task.CompletedTask;
    }
}

internal sealed class PlainTextHandler : IHubCommandHandler<PlainTextCommand>
{
    private readonly HandlerCapture _capture;

    public PlainTextHandler
    (
        HandlerCapture capture
    )
    {
        _capture = capture;
    }

    public Task Handle(PlainTextCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        _capture.Invocations.Enqueue((request, context.ConnectionId));
        return Task.CompletedTask;
    }
}

internal sealed class AdminOnlyHandler : IHubCommandHandler<PingCommand>
{
    private readonly HandlerCapture _capture;

    public AdminOnlyHandler
    (
        HandlerCapture capture
    )
    {
        _capture = capture;
    }

    public Task Handle(PingCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        _capture.Invocations.Enqueue((request, context.ConnectionId));
        return Task.CompletedTask;
    }
}

internal sealed class TopicAllowAnonymousHandler : IHubCommandHandler<PingCommand>
{
    private readonly HandlerCapture _capture;

    public TopicAllowAnonymousHandler
    (
        HandlerCapture capture
    )
    {
        _capture = capture;
    }

    public Task Handle(PingCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        _capture.Invocations.Enqueue((request, context.ConnectionId));
        return Task.CompletedTask;
    }
}

internal sealed class AuthSuccessHandler : IHubCommandHandler<PingCommand>
{
    private readonly HandlerCapture _capture;

    public AuthSuccessHandler
    (
        HandlerCapture capture
    )
    {
        _capture = capture;
    }

    public Task Handle(PingCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        _capture.Invocations.Enqueue((request, context.ConnectionId));
        return Task.CompletedTask;
    }
}

internal sealed class EchoPingHandler : IHubCommandHandler<PingCommand>
{
    private readonly ITopicHubContext<PingHub> _hub;

    public EchoPingHandler
    (
        ITopicHubContext<PingHub> hub
    )
    {
        _hub = hub;
    }

    public Task Handle(PingCommand request, HubCallerContext context, CancellationToken cancellationToken) =>
        _hub.Clients.Client(context.ConnectionId).Handle(new OutboundUpdate { Symbol = request.Value });
}

internal static class TestServiceFactory
{
    public static (ServiceProvider Sp, EndpointOptionRegistry Registry, HandlerCapture Capture) CreatePingServices
    (
        Action<EndpointOptions>? configure = null,
        bool registerHandlerInDi = true
    )
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<HandlerCapture>();
        services.AddSignalR();

        EndpointOptions options = services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous();

        if (configure is null)
        {
            options.HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>());

            options.HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));
        }
        else
        {
            configure(options);
        }

        // HandleOnServer already registers the handler scoped; only remove when testing missing DI
        if (!registerHandlerInDi)
        {
            ServiceDescriptor? handlerDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PingHandler));
            if (handlerDescriptor is not null)
            {
                services.Remove(handlerDescriptor);
            }
        }

        ServiceProvider sp = services.BuildServiceProvider();
        EndpointOptionRegistry registry = sp.GetRequiredService<EndpointOptionRegistry>();
        HandlerCapture capture = sp.GetRequiredService<HandlerCapture>();
        return (sp, registry, capture);
    }

    public static ClaimsPrincipal CreateUser(params string[] roles)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Name, "test-user")
        };

        foreach (string role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        ClaimsIdentity identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }
}
