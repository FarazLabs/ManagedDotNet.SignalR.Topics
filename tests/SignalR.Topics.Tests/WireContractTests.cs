using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SignalR.Topics.Tests.Fixtures;

namespace SignalR.Topics.Tests;

public sealed class WireContractTests
{
    [Fact]
    public async Task Inbound_Handle_invokes_registered_handler()
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
        await app.StartAsync();

        string baseUrl = app.Urls.First();
        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/ping")
            .Build();

        await connection.StartAsync();
        // Default STJ uses JsonSerializerDefaults.Web (camelCase + case-insensitive)
        await connection.InvokeAsync("Handle", "ping", """{"value":"hello"}""");

        HandlerCapture capture = app.Services.GetRequiredService<HandlerCapture>();
        Assert.True(capture.Invocations.TryDequeue(out (object Command, string? ConnectionId) invocation));
        PingCommand command = Assert.IsType<PingCommand>(invocation.Command);
        Assert.Equal("hello", command.Value);
        Assert.False(string.IsNullOrEmpty(invocation.ConnectionId));

        await connection.InvokeAsync("Handle", "ping", """{"Value":"pascal"}""");
        Assert.True(capture.Invocations.TryDequeue(out invocation));
        Assert.Equal("pascal", Assert.IsType<PingCommand>(invocation.Command).Value);
    }

    [Fact]
    public async Task Inbound_custom_deserializer_round_trips_plain_text()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/ping")
            .AllowAnonymous()
            .HandleOnServer<PlainTextCommand>(cfg =>
                cfg.WithTopic("plain")
                    .WithDeserializer(str => new PlainTextCommand { Text = str.Trim().ToUpperInvariant() })
                    .WithHandler<PlainTextHandler>())
            .HandleOnClient<OutboundUpdate>(cfg =>
                cfg.WithTopic("update"));

        await using WebApplication app = builder.Build();
        app.MapTopicHubs();
        await app.StartAsync();

        string baseUrl = app.Urls.First();
        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/ping")
            .Build();

        await connection.StartAsync();
        await connection.InvokeAsync("Handle", "plain", "  abc  ");

        HandlerCapture capture = app.Services.GetRequiredService<HandlerCapture>();
        Assert.True(capture.Invocations.TryDequeue(out (object Command, string? ConnectionId) invocation));
        PlainTextCommand command = Assert.IsType<PlainTextCommand>(invocation.Command);
        Assert.Equal("ABC", command.Text);
    }

    [Fact]
    public async Task Outbound_Handle_sends_Handle_method_with_topic_and_payload()
    {
        (ServiceProvider sp, EndpointOptionRegistry registry, HandlerCapture _) = TestServiceFactory.CreatePingServices(
            options =>
            {
                options.HandleOnServer<PingCommand>(cfg =>
                    cfg.WithTopic("ping")
                        .WithHandler<PingHandler>());

                options.HandleOnClient<OutboundUpdate>(cfg =>
                    cfg.WithTopic("update")
                        .WithSerializer(msg => msg!.Symbol));
            });

        await using ServiceProvider _ = sp;

        RecordingClientProxy proxy = new RecordingClientProxy();
        TopicClientProxy topicProxy = new TopicClientProxy(proxy, typeof(PingHub), registry);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await topicProxy.Handle(new OutboundUpdate { Symbol = "BTC" }, cts.Token);

        Assert.True(proxy.Calls.TryDequeue(out (string Method, object?[] Args) call));
        Assert.Equal("Handle", call.Method);
        Assert.Equal(2, call.Args.Length);
        Assert.Equal("update", call.Args[0]);
        Assert.Equal("BTC", call.Args[1]);
        Assert.Equal(cts.Token, proxy.LastCancellationToken);
    }

    [Fact]
    public async Task Outbound_unregistered_message_type_throws_MissingConfigurationException()
    {
        (ServiceProvider sp, EndpointOptionRegistry registry, HandlerCapture _) = TestServiceFactory.CreatePingServices();
        await using ServiceProvider _ = sp;

        RecordingClientProxy proxy = new RecordingClientProxy();
        TopicClientProxy topicProxy = new TopicClientProxy(proxy, typeof(PingHub), registry);

        await Assert.ThrowsAsync<ManagedDotNet.SignalR.Topics.Types.Exceptions.MissingConfigurationException>(
            () => topicProxy.Handle(new UnregisteredOutbound { Symbol = "ETH" }));
    }

    [Fact]
    public async Task Outbound_derived_type_falls_back_to_base_registration()
    {
        (ServiceProvider sp, EndpointOptionRegistry registry, HandlerCapture _) = TestServiceFactory.CreatePingServices(
            options =>
            {
                options.HandleOnServer<PingCommand>(cfg =>
                    cfg.WithTopic("ping")
                        .WithHandler<PingHandler>());

                options.HandleOnClient<OutboundUpdate>(cfg =>
                    cfg.WithTopic("update")
                        .WithSerializer(msg => msg!.Symbol));
            });

        await using ServiceProvider _ = sp;

        RecordingClientProxy proxy = new RecordingClientProxy();
        TopicClientProxy topicProxy = new TopicClientProxy(proxy, typeof(PingHub), registry);

        await topicProxy.Handle(new DerivedOutboundUpdate { Symbol = "ETH" });

        Assert.True(proxy.Calls.TryDequeue(out (string Method, object?[] Args) call));
        Assert.Equal("update", call.Args[0]);
        Assert.Equal("ETH", call.Args[1]);
    }
}
