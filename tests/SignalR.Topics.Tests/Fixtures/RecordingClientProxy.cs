using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace SignalR.Topics.Tests.Fixtures;

internal sealed class RecordingClientProxy : IClientProxy
{
    public ConcurrentQueue<(string Method, object?[] Args)> Calls { get; } = new();
    public CancellationToken LastCancellationToken { get; private set; }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        LastCancellationToken = cancellationToken;
        Calls.Enqueue((method, args));
        return Task.CompletedTask;
    }
}
