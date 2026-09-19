using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
    public void Incomplete_outbound_WithTopic_throws_at_MapTopicHubs()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/ping")
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("ping")
                    .WithHandler<PingHandler>())
            .HandleOnClient<OutboundUpdate>(_ => { });

        using WebApplication app = builder.Build();

        MisconfiguredException ex = Assert.Throws<MisconfiguredException>(() =>
            app.MapTopicHubs());

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
}
