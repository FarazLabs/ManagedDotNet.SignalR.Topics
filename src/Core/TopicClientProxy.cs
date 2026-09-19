using Microsoft.AspNetCore.SignalR;
using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;

namespace ManagedDotNet.SignalR.Topics.Core;

/// <summary>
/// Wraps <see cref="IClientProxy"/> and carries the hub type used for outbound topic lookup.
/// </summary>
public sealed class TopicClientProxy
{
    internal IClientProxy Client { get; }
    internal Type HubType { get; }
    private readonly EndpointOptionRegistry _registry;

    public TopicClientProxy
    (
        IClientProxy client,
        Type hubType,
        EndpointOptionRegistry registry
    )
    {
        Client = client;
        HubType = hubType;
        _registry = registry;
    }

    /// <summary>
    /// Maps the message to its configured topic and invokes <c>Handle</c> on the client.
    /// </summary>
    /// <exception cref="MissingConfigurationException"></exception>
    public async Task Handle<TMessage>(TMessage? message, CancellationToken cancellationToken = default)
    {
        EndpointOptions endpoint = _registry.GetEndpointOptions(this.HubType);

        // Null has no runtime type — fall back to TMessage; otherwise prefer runtime type, then base types
        Type messageType = message is null ? typeof(TMessage) : message.GetType();

        HandleOnClientConfiguration? route = null;
        for (Type? candidate = messageType; candidate is not null && candidate != typeof(object); candidate = candidate.BaseType)
        {
            if (endpoint.HandleOnClientConfigurations.TryGetValue(candidate, out route))
                break;
        }

        if (route is null)
        {
            throw new MissingConfigurationException(
                $"No configuration found for outgoing message type {messageType} within hub {this.HubType.FullName}.");
        }

        string topic = route.Topic!;
        string payload = route.Serialize(message);

        await this.Client.SendAsync("Handle", topic, payload, cancellationToken);
    }
}
