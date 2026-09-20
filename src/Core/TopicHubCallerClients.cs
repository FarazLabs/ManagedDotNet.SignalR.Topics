using Microsoft.AspNetCore.SignalR;
using ManagedDotNet.SignalR.Topics.Configuration;

namespace ManagedDotNet.SignalR.Topics.Core;

/// <summary>
/// Wraps <see cref="IHubCallerClients"/> and stamps each selector with the hub type.
/// </summary>
public sealed class TopicHubCallerClients
{
    private readonly IHubCallerClients _clients;
    private readonly Type _hubType;
    private readonly EndpointOptionRegistry _registry;

    public TopicHubCallerClients
    (
        IHubCallerClients clients,
        Type hubType,
        EndpointOptionRegistry registry
    )
    {
        _clients = clients;
        _hubType = hubType;
        _registry = registry;
    }

    public TopicClientProxy All => new(_clients.All, _hubType, _registry);
    public TopicClientProxy Caller => new(_clients.Caller, _hubType, _registry);
    public TopicClientProxy Others => new(_clients.Others, _hubType, _registry);
    public TopicClientProxy OthersInGroup(string groupName) => new(_clients.OthersInGroup(groupName), _hubType, _registry);
    public TopicClientProxy Client(string connectionId) => new(_clients.Client(connectionId), _hubType, _registry);
    public TopicClientProxy Clients(IReadOnlyList<string> connectionIds) => new(_clients.Clients(connectionIds), _hubType, _registry);
    public TopicClientProxy Group(string groupName) => new(_clients.Group(groupName), _hubType, _registry);
    public TopicClientProxy Groups(IReadOnlyList<string> groupNames) => new(_clients.Groups(groupNames), _hubType, _registry);
    public TopicClientProxy User(string userId) => new(_clients.User(userId), _hubType, _registry);
    public TopicClientProxy Users(IReadOnlyList<string> userIds) => new(_clients.Users(userIds), _hubType, _registry);
}
