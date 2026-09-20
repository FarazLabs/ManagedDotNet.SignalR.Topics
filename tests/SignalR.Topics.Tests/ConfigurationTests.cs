using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalR.Topics.Tests.Fixtures;

namespace SignalR.Topics.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public async Task MapTopicHubs_happy_path_maps_and_seals_registry()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous()
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using WebApplication app = builder.Build();
        app.MapTopicHubs();

        EndpointOptionRegistry registry = app.Services.GetRequiredService<EndpointOptionRegistry>();
        Assert.Single(registry.Endpoints);
        Assert.Equal("/ping", registry.Endpoints[0].Path);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            registry.AddTopicHub<OtherHub>("/other"));
        Assert.Contains("sealed", ex.Message, StringComparison.OrdinalIgnoreCase);

        InvalidOperationException frozen = Assert.Throws<InvalidOperationException>(() =>
            registry.Endpoints[0].HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("late")
                    .WithHandler<PingHandler>()));
        Assert.Contains("sealed", frozen.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MapTopicHubs_warns_when_hub_auth_and_topic_allow_anonymous()
    {
        List<string> warnings = new List<string>();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CollectingLoggerProvider(warnings));
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/ping")
            .RequireAuthorization()
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("open")
                    .AllowAnonymous()
                    .WithHandler<TopicAllowAnonymousHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using WebApplication app = builder.Build();
        app.MapTopicHubs();

        Assert.Contains(warnings, w =>
            w.Contains("AllowAnonymous", StringComparison.Ordinal)
            && w.Contains("open", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nested_HandleOn_mutators_throw_after_MapTopicHubs()
    {
        HandleOnServerConfiguration<PingCommand>? inbound = null;
        HandleOnClientConfiguration<OutboundUpdate>? outbound = null;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous()
            .HandleOnServer<PingCommand>(cfg =>
            {
                inbound = cfg;
                cfg.WithTopic("ping").WithHandler<PingHandler>();
            })
            .HandleOnClient<OutboundUpdate>(cfg =>
            {
                outbound = cfg;
                cfg.WithTopic("update");
            });

        await using WebApplication app = builder.Build();
        app.MapTopicHubs();

        Assert.Throws<InvalidOperationException>(() => inbound!.WithTopic("hacked"));
        Assert.Throws<InvalidOperationException>(() => outbound!.WithTopic("hacked"));
    }

    // MapHub parity: neither RequireAuthorization nor AllowAnonymous → anonymously connectable.
    [Fact]
    public async Task Hub_without_fluent_auth_allows_anonymous_connect()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/open")
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using WebApplication app = builder.Build();
        app.MapTopicHubs();
        await app.StartAsync();

        string hubUrl = $"{app.Urls.First().TrimEnd('/')}/open";
        await using HubConnection connection = new HubConnectionBuilder().WithUrl(hubUrl).Build();
        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public void MapTopicHubs_with_no_hubs_throws()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSignalR();
        // Ensure registry exists with zero hubs
        EndpointOptionRegistry.GetOrCreate(builder.Services);

        using WebApplication app = builder.Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            app.MapTopicHubs());
        Assert.Contains("No topic hubs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_inbound_topic_throws_MisconfiguredException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();

        EndpointOptions options = services.AddTopicHub<PingHub>("/ping");
        options.HandleOnServer<PingCommand>(cfg =>
            cfg.WithTopic("ping")
                .WithHandler<PingHandler>());

        MisconfiguredException ex = Assert.Throws<MisconfiguredException>(() =>
            options.HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>()));

        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_outbound_type_throws_MisconfiguredException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();

        EndpointOptions options = services.AddTopicHub<PingHub>("/ping");
        options.HandleOnClient<OutboundUpdate>(cfg => cfg.WithTopic("update"));

        MisconfiguredException ex = Assert.Throws<MisconfiguredException>(() =>
            options.HandleOnClient<OutboundUpdate>(cfg => cfg.WithTopic("other")));

        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_outbound_topic_throws_MisconfiguredException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();

        EndpointOptions options = services.AddTopicHub<PingHub>("/ping");
        options.HandleOnClient<OutboundUpdate>(cfg => cfg.WithTopic("update"));

        MisconfiguredException ex = Assert.Throws<MisconfiguredException>(() =>
            options.HandleOnClient<UnregisteredOutbound>(cfg => cfg.WithTopic("update")));

        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Incomplete_WithHandler_throws_MisconfiguredException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();

        EndpointOptions options = services.AddTopicHub<PingHub>("/ping");

        MisconfiguredException ex = Assert.Throws<MisconfiguredException>(() =>
            options.HandleOnServer<PingCommand>(cfg => cfg.WithTopic("ping")));

        Assert.Contains("Handler", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_RequireAuthorization_params_throws_MisconfiguredException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();
        EndpointOptions options = services.AddTopicHub<PingHub>("/ping");

        MisconfiguredException hub = Assert.Throws<MisconfiguredException>(() =>
            options.RequireAuthorization(Array.Empty<string>()));
        Assert.Contains("silently skip", hub.Message, StringComparison.OrdinalIgnoreCase);

        MisconfiguredException topic = Assert.Throws<MisconfiguredException>(() =>
            options.HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .RequireAuthorization(Array.Empty<string>())
                    .WithHandler<PingHandler>()));
        Assert.Contains("silently skip", topic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Incomplete_outbound_WithTopic_throws_MisconfiguredException()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();

        EndpointOptions options = services.AddTopicHub<PingHub>("/ping");

        MisconfiguredException ex = Assert.Throws<MisconfiguredException>(() =>
            options.HandleOnClient<OutboundUpdate>(_ => { }));

        Assert.Contains("Topic is not configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTopicHub_registers_authorization_services()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddTopicHub<PingHub>("/ping")
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        using ServiceProvider sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>());
    }

    [Fact]
    public void Path_without_leading_slash_is_normalized()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSignalR();
        services.AddTopicHub<PingHub>("orderBook")
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        ServiceProvider sp = services.BuildServiceProvider();
        EndpointOptionRegistry registry = sp.GetRequiredService<EndpointOptionRegistry>();

        Assert.Equal("/orderBook", registry.Endpoints[0].Path);
    }

    [Fact]
    public void Multi_hub_routes_are_isolated_by_hub_type()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<HandlerCapture>();
        services.AddSignalR();

        services.AddTopicHub<PingHub>("/ping")
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        services.AddTopicHub<OtherHub>("/other")
            .HandleOnServer<PlainTextCommand>(cfg =>
                cfg.WithTopic("plain")
                    .WithHandler<PlainTextHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("other-update"));

        ServiceProvider sp = services.BuildServiceProvider();
        EndpointOptionRegistry registry = sp.GetRequiredService<EndpointOptionRegistry>();

        EndpointOptions ping = registry.GetEndpointOptions(typeof(PingHub));
        EndpointOptions other = registry.GetEndpointOptions(typeof(OtherHub));

        Assert.True(ping.HandleOnServerConfigurations.ContainsKey("ping"));
        Assert.False(ping.HandleOnServerConfigurations.ContainsKey("plain"));
        Assert.True(other.HandleOnServerConfigurations.ContainsKey("plain"));
        Assert.False(other.HandleOnServerConfigurations.ContainsKey("ping"));
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings;

        public CollectingLoggerProvider(List<string> warnings) => _warnings = warnings;

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(_warnings);

        public void Dispose() { }

        private sealed class CollectingLogger : ILogger
        {
            private readonly List<string> _warnings;

            public CollectingLogger(List<string> warnings) => _warnings = warnings;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>
            (
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (logLevel >= LogLevel.Warning)
                    _warnings.Add(formatter(state, exception));
            }
        }
    }
}
