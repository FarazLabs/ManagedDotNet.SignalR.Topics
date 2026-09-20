using System.Security.Claims;
using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Implementations;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SignalR.Topics.Tests.Fixtures;

namespace SignalR.Topics.Tests;

public sealed class HubCommandDispatcherTests
{
    [Fact]
    public async Task Unknown_topic_throws_MissingConfigurationException()
    {
        (ServiceProvider sp, EndpointOptionRegistry _, HandlerCapture _) = TestServiceFactory.CreatePingServices();
        await using ServiceProvider _ = sp;

        HubCommandDispatcher dispatcher = sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>()
            as HubCommandDispatcher
            ?? throw new InvalidOperationException("Expected HubCommandDispatcher");

        await Assert.ThrowsAsync<MissingConfigurationException>(() =>
            dispatcher.DispatchAsync(typeof(PingHub), "missing", "{}", new TestHubCallerContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Null_message_throws_ArgumentNullException()
    {
        (ServiceProvider sp, EndpointOptionRegistry _, HandlerCapture _) = TestServiceFactory.CreatePingServices();
        await using ServiceProvider _ = sp;

        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dispatcher.DispatchAsync(typeof(PingHub), "ping", null!, new TestHubCallerContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Null_deserialize_throws_ArgumentNullException()
    {
        (ServiceProvider sp, EndpointOptionRegistry _, HandlerCapture _) = TestServiceFactory.CreatePingServices(
            options =>
            {
                options.HandleOnServer<PingCommand>(cfg =>
                    cfg.WithTopic("ping")
                        .WithDeserializer(_ => null)
                        .WithHandler<PingHandler>());

                options.HandleOnClient<OutboundUpdate>(cfg =>
                    cfg.WithTopic("update"));
            });
        await using ServiceProvider _ = sp;

        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dispatcher.DispatchAsync(typeof(PingHub), "ping", "payload", new TestHubCallerContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_handler_in_DI_throws_ServiceNotRegisteredException()
    {
        (ServiceProvider sp, EndpointOptionRegistry _, HandlerCapture _) = TestServiceFactory.CreatePingServices(
            registerHandlerInDi: false);
        await using ServiceProvider _ = sp;

        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();

        await Assert.ThrowsAsync<ServiceNotRegisteredException>(() =>
            dispatcher.DispatchAsync(typeof(PingHub), "ping", "{\"Value\":\"x\"}", new TestHubCallerContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Unauthorized_topic_throws_HubException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<HandlerCapture>();
        services.AddSignalR();

        services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous()
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("admin")
                    .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
                    .WithHandler<AdminOnlyHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using ServiceProvider sp = services.BuildServiceProvider();
        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();

        // Authenticated user without Admin role
        ClaimsPrincipal user = TestServiceFactory.CreateUser();
        TestHubCallerContext context = new TestHubCallerContext(user);

        HubException ex = await Assert.ThrowsAsync<HubException>(() =>
            dispatcher.DispatchAsync(typeof(PingHub), "admin", "{\"Value\":\"x\"}", context, CancellationToken.None));

        Assert.Contains("unauthorized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_role_invokes_authorized_handler()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<HandlerCapture>();
        services.AddSignalR();

        services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous()
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("admin")
                    .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
                    .WithHandler<AdminOnlyHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using ServiceProvider sp = services.BuildServiceProvider();
        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();
        HandlerCapture capture = sp.GetRequiredService<HandlerCapture>();

        ClaimsPrincipal user = TestServiceFactory.CreateUser("Admin");
        TestHubCallerContext context = new TestHubCallerContext(user);

        await dispatcher.DispatchAsync(typeof(PingHub), "admin", "{\"Value\":\"ok\"}", context, CancellationToken.None);

        Assert.True(capture.Invocations.TryDequeue(out (object Command, string? ConnectionId) hit));
        Assert.Equal("ok", Assert.IsType<PingCommand>(hit.Command).Value);
    }

    [Fact]
    public async Task AllowAnonymous_handler_invokes_for_anonymous_user()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<HandlerCapture>();
        services.AddSignalR();

        services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous()
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("open")
                    .AllowAnonymous()
                    .WithHandler<TopicAllowAnonymousHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using ServiceProvider sp = services.BuildServiceProvider();
        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();
        HandlerCapture capture = sp.GetRequiredService<HandlerCapture>();

        // Unauthenticated principal — topic AllowAnonymous skips topic auth
        TestHubCallerContext context = new TestHubCallerContext();

        await dispatcher.DispatchAsync(typeof(PingHub), "open", "{\"Value\":\"anon\"}", context, CancellationToken.None);

        Assert.True(capture.Invocations.TryDequeue(out (object Command, string? ConnectionId) hit));
        Assert.Equal("anon", Assert.IsType<PingCommand>(hit.Command).Value);
    }

    [Fact]
    public async Task Dispatch_invokes_handler_for_known_topic()
    {
        (ServiceProvider sp, EndpointOptionRegistry _, HandlerCapture capture) = TestServiceFactory.CreatePingServices();
        await using ServiceProvider _ = sp;

        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();
        TestHubCallerContext context = new TestHubCallerContext();

        await dispatcher.DispatchAsync(typeof(PingHub), "ping", "{\"Value\":\"ok\"}", context, CancellationToken.None);

        Assert.True(capture.Invocations.TryDequeue(out (object Command, string? ConnectionId) invocation));
        PingCommand command = Assert.IsType<PingCommand>(invocation.Command);
        Assert.Equal("ok", command.Value);
        Assert.Equal(context.ConnectionId, invocation.ConnectionId);
    }

    [Fact]
    public async Task Dispatch_honors_cancelled_token()
    {
        (ServiceProvider sp, EndpointOptionRegistry _, HandlerCapture _) = TestServiceFactory.CreatePingServices();
        await using ServiceProvider _ = sp;

        HubCommandDispatcher dispatcher = (HubCommandDispatcher)sp.GetRequiredService<ManagedDotNet.SignalR.Topics.Abstractions.IHubCommandDispatcher>();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(typeof(PingHub), "ping", "{\"Value\":\"ok\"}", new TestHubCallerContext(), cts.Token));
    }
}
