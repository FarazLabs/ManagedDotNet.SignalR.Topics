using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ManagedDotNet.SignalR.Topics.Implementations;

namespace ManagedDotNet.SignalR.Topics.Configuration;

/// <summary>
/// Extension methods for configuring SignalR services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a topic hub at <paramref name="path"/> and ensures core ManagedDotNet.SignalR.Topics services are present.
    /// Safe to call from multiple modules; core registrations are idempotent.
    /// Map all registered hubs later with <c>endpoints.MapTopicHubs()</c>
    /// (that call also validates topic/handler/serializer configuration).
    /// </summary>
    /// <typeparam name="THub">Hub type to configure</typeparam>
    /// <param name="services">Service collection to configure</param>
    /// <param name="path">Hub route path (e.g. <c>/apphub</c>)</param>
    /// <returns>Configuration builder for the hub</returns>
    public static EndpointOptions AddTopicHub<THub>
    (
        this IServiceCollection services,
        string path
    )
        where THub : TopicHub
    {
        AddFramework(services);

        EndpointOptionRegistry frameworkOptions = EndpointOptionRegistry.GetOrCreate(services);
        return frameworkOptions.AddTopicHub<THub>(path);
    }

    private static void AddFramework(IServiceCollection services)
    {
        services.TryAddSingleton(typeof(ITopicHubContext<>), typeof(TopicHubContext<>));

        // Command dispatcher resolved into TopicHub instances
        services.TryAddScoped<IHubCommandDispatcher, HubCommandDispatcher>();

        // Topic RequireAuthorization resolves IAuthorizationService at dispatch
        services.AddAuthorization();
    }
}
