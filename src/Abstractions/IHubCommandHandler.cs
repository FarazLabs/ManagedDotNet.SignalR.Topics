using Microsoft.AspNetCore.SignalR;

namespace ManagedDotNet.SignalR.Topics.Abstractions;

/// <summary>
/// Handler for processing specific command types
/// </summary>
/// <typeparam name="TCommand">Command type to handle</typeparam>
public interface IHubCommandHandler<in TCommand>
{
    /// <summary>
    /// Handles the specified command asynchronously.
    /// </summary>
    /// <param name="request">Command to process</param>
    /// <param name="context">SignalR connection context</param>
    /// <param name="cancellationToken">Cancelled when the SignalR connection aborts</param>
    Task Handle(TCommand request, HubCallerContext context, CancellationToken cancellationToken);
}
