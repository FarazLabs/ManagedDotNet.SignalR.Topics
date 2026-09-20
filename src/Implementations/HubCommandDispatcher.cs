using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Configuration;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;

namespace ManagedDotNet.SignalR.Topics.Implementations;

internal class HubCommandDispatcher : IHubCommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EndpointOptionRegistry _registry;

    public HubCommandDispatcher
    (
        IServiceProvider serviceProvider,
        EndpointOptionRegistry registry
    )
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
    }

    public async Task DispatchAsync(Type hubType, string topic, string message, HubCallerContext context, CancellationToken cancellationToken)
    {
        EndpointOptions configuration = _registry.GetEndpointOptions(hubType);

        if (!configuration.HandleOnServerConfigurations.TryGetValue(topic, out HandleOnServerConfiguration? route))
            throw new MissingConfigurationException($"No configuration found for topic {topic}. Please ensure it is registered with HandleOnServer<TModel>().");

        // Per-topic auth from HandleOnServer.RequireAuthorization — before deserialize.
        if (!route.IsAnonymousAllowed && route.AuthorizeData.Length > 0)
        {
            IAuthorizationPolicyProvider policyProvider = _serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
            IAuthorizationService authorization = _serviceProvider.GetRequiredService<IAuthorizationService>();
            AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(policyProvider, route.AuthorizeData);
            if (policy is not null)
            {
                AuthorizationResult result = await authorization.AuthorizeAsync(context.User!, resource: null, policy);
                if (!result.Succeeded)
                    throw new HubException($"Failed to invoke topic '{topic}' because the user is unauthorized.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(message);

        object? command = route.Deserialize(message);
        if (command is null)
            throw new ArgumentNullException(nameof(message), $"Payload for topic '{topic}' deserialized to null.");

        Type handlerType = route.HandlerType ?? throw new MisconfiguredException($"Handler type not specified for topic {topic}. Please call WithHandler() to register the respective handler");
        Func<object, object?, HubCallerContext, CancellationToken, Task> invoke = route.Invoke ?? throw new MisconfiguredException($"Invoke delegate not set for topic {topic}. Please call WithHandler() to register the respective handler");

        object? handler = _serviceProvider.GetService(handlerType);
        if (handler is null)
            throw new ServiceNotRegisteredException(handlerType.ToString());

        await invoke(handler, command, context, cancellationToken);
    }
}
