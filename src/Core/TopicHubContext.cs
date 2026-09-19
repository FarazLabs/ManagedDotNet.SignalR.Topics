using Microsoft.AspNetCore.SignalR;
using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Configuration;

namespace ManagedDotNet.SignalR.Topics.Core;

/// <summary>
/// Hub context for topic hubs. Prefer this over <see cref="IHubContext{THub}"/> so client selectors carry hub type.
/// </summary>
public interface ITopicHubContext<THub> where THub : TopicHub
{
    TopicHubContextClient Clients { get; }
    IGroupManager Groups { get; }
}

public sealed class TopicHubContext<THub> : ITopicHubContext<THub> where THub : TopicHub
{
    private readonly IHubContext<THub> _hubContext;
    private readonly EndpointOptionRegistry _registry;

    public TopicHubContext
    (
        IHubContext<THub> hubContext,
        EndpointOptionRegistry registry
    )
    {
        _hubContext = hubContext;
        _registry = registry;
    }

    public TopicHubContextClient Clients => new TopicHubContextClient(_hubContext.Clients, typeof(THub), _registry);
    public IGroupManager Groups => _hubContext.Groups;
}
