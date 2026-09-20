using Microsoft.AspNetCore.SignalR;

namespace ManagedDotNet.SignalR.Topics.Abstractions;

/// <summary>
/// Dispatches commands to their appropriate handlers
/// </summary>
internal interface IHubCommandDispatcher
{
    /// <summary>
    /// Deserializes the payload into a command using the configuration for the topic hub( topic to type using deserializer),
    /// and delegates it to appropriate handler based on the topic
    /// </summary>
    /// <param name="hubType">The type of the hub that received the message</param>
    /// <param name="topic">The message topic used for routing</param>
    /// <param name="message">The serialized message payload</param>
    /// <param name="context">The SignalR hub context</param>
    /// <param name="cancellationToken">Cancellation for the in-flight handle</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task DispatchAsync(Type hubType, string topic, string message, HubCallerContext context, CancellationToken cancellationToken);
} 