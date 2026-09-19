using Microsoft.Extensions.DependencyInjection;

namespace ManagedDotNet.SignalR.Topics.Examples.Shared;

/// <summary>
/// Contract for a modular monolith feature module that registers its own services and topic hubs.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Registers this module's services and topic hubs.
    /// </summary>
    /// <param name="services">Application service collection</param>
    void Register(IServiceCollection services);
}
