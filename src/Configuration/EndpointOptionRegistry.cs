using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedDotNet.SignalR.Topics.Configuration;

/// <summary>
/// Central configuration for topic-based SignalR hubs 
/// </summary>
public sealed class EndpointOptionRegistry
{
    private IServiceCollection? Services { get; set; }
    internal List<EndpointOptions> Endpoints { get; set; }
    private bool _isSealed;

    private EndpointOptionRegistry
    (
        IServiceCollection services
    )
    {
        Endpoints = new List<EndpointOptions>();
        Services = services;
    }

    /// <summary>
    /// Returns the DI singleton registry for <paramref name="services"/>, creating it on first use.
    /// </summary>
    internal static EndpointOptionRegistry GetOrCreate
    (
        IServiceCollection services
    )
    {
        ServiceDescriptor? existing = services.FirstOrDefault(d =>
            d.ServiceType == typeof(EndpointOptionRegistry)
            && d.ImplementationInstance is EndpointOptionRegistry);

        if (existing?.ImplementationInstance is EndpointOptionRegistry registry)
        {
            if (registry._isSealed)
            {
                throw new InvalidOperationException(
                    "EndpointOptionRegistry has already been sealed for runtime use. Register hubs during application startup before the host serves requests.");
            }

            return registry;
        }

        EndpointOptionRegistry created = new EndpointOptionRegistry(services);
        services.AddSingleton(created);
        return created;
    }

    /// <summary>
    /// Finalizes the instance and rids the object of unnecessary references.
    /// Called from <c>MapTopicHubs</c> after all hubs are mapped.
    /// </summary>
    internal void Seal()
    {
        if (_isSealed)
        {
            return;
        }

        Services = null;
        Endpoints!.ForEach(e => e.Freeze());
        _isSealed = true;
    }

    /// <summary>
    /// Adds a topic hub at the given route path.
    /// </summary>
    /// <typeparam name="THub">Hub type to configure</typeparam>
    /// <param name="path">Hub route path (e.g. <c>/apphub</c>)</param>
    /// <returns>Configuration builder for the hub</returns>
    public EndpointOptions AddTopicHub<THub>
    (
        string path
    )
        where THub : TopicHub
    {
        if (_isSealed)
        {
            throw new InvalidOperationException(
                "Cannot add topic hubs after ManagedDotNet.SignalR.Topics has been sealed for runtime use.");
        }

        if (Services is null)
        {
            throw new InvalidOperationException("ManagedDotNet.SignalR.Topics options are not bound to a service collection.");
        }

        string normalizedPath = NormalizePath(path);

        EndpointOptions? existing = Endpoints.FirstOrDefault(m => m.HubType == typeof(THub));
        if (existing is not null)
        {
            throw new InvalidOperationException(
                string.Equals(existing.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)
                    ? $"Hub {typeof(THub).FullName} is already registered at '{existing.Path}'."
                    : $"Hub {typeof(THub).FullName} is already registered at '{existing.Path}'; cannot re-register at '{normalizedPath}'.");
        }

        EndpointOptions? pathConflict = Endpoints.FirstOrDefault(e =>
            string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (pathConflict is not null)
        {
            throw new InvalidOperationException(
                $"Hub path '{normalizedPath}' is already registered for {pathConflict.HubType.FullName}.");
        }

        EndpointOptions config = new EndpointOptions(typeof(THub), normalizedPath, this, Services);
        Endpoints.Add(config);

        // Register THub with custom factory
        Services.AddTransient<THub>(sp =>
        {
            // instantiate using the param-less constructor
            THub hub = ActivatorUtilities.CreateInstance<THub>(sp);

            // proceed to resolve & set internal dependencies on the hub instance
            hub.Dispatcher = sp.GetRequiredService<IHubCommandDispatcher>();
            hub.Registry = sp.GetRequiredService<EndpointOptionRegistry>();

            return hub;
        });

        return config;
    }

    private static string NormalizePath
    (
        string path
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Hub path cannot be null or whitespace.", nameof(path));
        }

        string trimmed = path.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    /// <summary>
    /// Finds &amp; returns the <see cref="Endpoints"/> associated with the <see cref="TopicHub"/>> hub of provided concrete type
    /// </summary>
    /// <returns>The matching <see cref="Endpoints"/> as configured at startup.</returns>
    /// <exception cref="MissingConfigurationException">configuration not found</exception>
    /// <exception cref="InvalidOperationException">invalid input type</exception>
    internal EndpointOptions GetEndpointOptions(Type type)
    {
        if (!typeof(TopicHub).IsAssignableFrom(type))
            throw new InvalidOperationException($"Type {type.FullName} is not a valid TopicHub type.");

        EndpointOptions? config = Endpoints.SingleOrDefault(x => x.HubType == type);

        if (config is null)
            throw new MissingConfigurationException($"No configuration found for hub type {type.FullName}. Please ensure it is registered with AddTopicHub<THub>(path).");

        return config;
    }
}
