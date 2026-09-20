using System.Security.Claims;
using System.Text.Encodings.Web;
using ManagedDotNet.SignalR.Topics.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignalR.Topics.Tests.Fixtures;

namespace SignalR.Topics.Tests.Integration;

public sealed class TopicHubEndToEndTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _hubUrl = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<HandlerCapture>();
        builder.Services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AccessTokenAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR();
        builder.Services.AddTopicHub<PingHub>("/e2e")
            .RequireAuthorization()
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("auth")
                    .RequireAuthorization()
                    .WithHandler<AuthSuccessHandler>())
            .HandleOnServer<PingCommand>(cfg =>
                cfg.WithTopic("echo").WithHandler<EchoPingHandler>())
            .HandleOnClient<OutboundUpdate>(cfg => cfg.WithTopic("update"));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapTopicHubs();
        await _app.StartAsync();

        _hubUrl = $"{_app.Urls.First().TrimEnd('/')}/e2e";
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    // ponytail: AccessTokenProvider → ?access_token= (same wire as JWT); real JwtBearer when you want crypto.
    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(_hubUrl, o => o.AccessTokenProvider = () => Task.FromResult<string?>("e2e-token"))
            .Build();

    [Fact]
    public async Task Hub_JWT_connect_succeeds()
    {
        await using HubConnection connection = Connect();
        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Hub_anonymous_connect_is_rejected()
    {
        await using HubConnection connection = new HubConnectionBuilder().WithUrl(_hubUrl).Build();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Auth_success_invokes_authorized_handler()
    {
        HandlerCapture capture = _app.Services.GetRequiredService<HandlerCapture>();
        while (capture.Invocations.TryDequeue(out _)) { }

        await using HubConnection connection = Connect();
        await connection.StartAsync();

        await connection.InvokeAsync("Handle", "auth", """{"Value":"ok"}""");

        Assert.True(capture.Invocations.TryDequeue(out (object Command, string? ConnectionId) hit));
        Assert.Equal("ok", Assert.IsType<PingCommand>(hit.Command).Value);
    }

    [Fact]
    public async Task Outbound_On_Handle_delivers_topic_and_payload()
    {
        TaskCompletionSource<(string Topic, string Payload)> received =
            new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using HubConnection connection = Connect();
        connection.On<string, string>("Handle", (topic, payload) => received.TrySetResult((topic, payload)));
        await connection.StartAsync();

        await connection.InvokeAsync("Handle", "echo", """{"Value":"BTC"}""");

        (string Topic, string Payload) result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("update", result.Topic);
        Assert.Equal("""{"symbol":"BTC"}""", result.Payload);
    }

    private sealed class AccessTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AccessTokenAuthHandler
        (
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder
        )
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? token = Request.Query["access_token"];
            if (string.IsNullOrEmpty(token)
                && Request.Headers.Authorization.ToString() is string auth
                && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = auth["Bearer ".Length..].Trim();
            }

            if (string.IsNullOrEmpty(token))
                return Task.FromResult(AuthenticateResult.NoResult());

            ClaimsIdentity identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "e2e") },
                authenticationType: Scheme.Name);
            AuthenticationTicket ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
