using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace SignalR.Topics.Tests.Fixtures;

internal sealed class TestHubCallerContext : HubCallerContext
{
    private readonly ClaimsPrincipal _user;
    private readonly CancellationToken _aborted;

    public TestHubCallerContext
    (
        ClaimsPrincipal? user = null,
        string connectionId = "test-connection"
    )
    {
        ConnectionId = connectionId;
        _user = user ?? new ClaimsPrincipal(new ClaimsIdentity());
        _aborted = CancellationToken.None;
    }

    public override string ConnectionId { get; }
    public override string? UserIdentifier => _user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public override ClaimsPrincipal User => _user;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => _aborted;
    public override void Abort() { }
}
