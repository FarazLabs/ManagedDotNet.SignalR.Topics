using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedDotNet.SignalR.Topics.Configuration;

/// <summary>
/// Extension methods for mapping topic SignalR hubs onto the endpoint route builder.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    private static readonly MethodInfo MapHubMethodDefinition = ResolveMapHubMethod(parameterCount: 2);
    private static readonly MethodInfo MapHubWithOptionsMethodDefinition = ResolveMapHubMethod(parameterCount: 3);

    /// <summary>
    /// Validates every <c>HandleOnServer</c> / <c>HandleOnClient</c> binding, then maps every hub
    /// previously registered via <c>AddTopicHub&lt;THub&gt;(path)</c>.
    /// Call once from the host (e.g. inside <c>UseEndpoints</c>) so modules only register services.
    /// Auth, CORS, connection options, and other conventions are applied from each hub's
    /// <see cref="EndpointOptions"/> (set at registration time).
    /// </summary>
    /// <param name="endpoints">Endpoint route builder</param>
    /// <returns>The same <paramref name="endpoints"/> for chaining</returns>
    public static IEndpointRouteBuilder MapTopicHubs
    (
        this IEndpointRouteBuilder endpoints
    )
    {
        EndpointOptionRegistry options = endpoints.ServiceProvider.GetRequiredService<EndpointOptionRegistry>();
        if (options.Endpoints.Count == 0)
        {
            throw new InvalidOperationException(
                "No topic hubs are registered. Call services.AddTopicHub<THub>(path) during startup before MapTopicHubs().");
        }

        foreach (EndpointOptions endpoint in options.Endpoints)
        {
            // Validate outbound WithTopic bindings (inbound already validated in HandleOnServer)
            endpoint.EnsureIsValid();

            // Registry already lists HubType + Path; reflection only binds MapHub<T> to that runtime Type.
            MethodInfo mapHubDefinition = endpoint.ConfigureHttpConnectionCallback is null
                ? MapHubMethodDefinition
                : MapHubWithOptionsMethodDefinition;

            MethodInfo mapHub = mapHubDefinition.MakeGenericMethod(endpoint.HubType);
            object?[] invokeArgs = endpoint.ConfigureHttpConnectionCallback is null
                ? new object[] { endpoints, endpoint.Path }
                : new object[] { endpoints, endpoint.Path, endpoint.ConfigureHttpConnectionCallback };

            object? mapped = mapHub.Invoke(null, invokeArgs);

            if (mapped is HubEndpointConventionBuilder hubEndpoint)
            {
                ApplyEndpointOptions(hubEndpoint, endpoint);
            }
        }

        // Registration complete — drop builder-only IServiceCollection refs
        options.Seal();

        return endpoints;
    }

    private static void ApplyEndpointOptions
    (
        HubEndpointConventionBuilder hubEndpoint,
        EndpointOptions endpoint
    )
    {
        // Same as MapHub: only add conventions when caller asked for them.
        if (endpoint.RequiresAuthorization)
        {
            if (endpoint.AuthorizeData.Length > 0)
                hubEndpoint.RequireAuthorization(endpoint.AuthorizeData);

            foreach (AuthorizationPolicy policy in endpoint.AuthorizationPolicies)
                hubEndpoint.RequireAuthorization(policy);
        }
        else if (endpoint.AllowsAnonymous)
        {
            hubEndpoint.AllowAnonymous();
        }

        endpoint.ApplyCors?.Invoke(hubEndpoint);

        foreach (Action<HubEndpointConventionBuilder> convention in endpoint.EndpointConventions)
            convention(hubEndpoint);
    }

    private static MethodInfo ResolveMapHubMethod(int parameterCount)
    {
        // Cache the open generic MapHub<THub> overloads.
        // Hub types arrive only as runtime Type from EndpointOptionRegistry, so we cannot
        // call MapHub<THub> with a compile-time type argument and must MakeGenericMethod later.
        MethodInfo? method = typeof(HubEndpointRouteBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == nameof(HubEndpointRouteBuilderExtensions.MapHub)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == parameterCount
                && m.GetParameters()[0].ParameterType == typeof(IEndpointRouteBuilder)
                && m.GetParameters()[1].ParameterType == typeof(string)
                && (parameterCount == 2
                    || m.GetParameters()[2].ParameterType == typeof(Action<HttpConnectionDispatcherOptions>)));

        if (method is null)
        {
            string signature = parameterCount == 2
                ? "MapHub<THub>(IEndpointRouteBuilder, string)"
                : "MapHub<THub>(IEndpointRouteBuilder, string, Action<HttpConnectionDispatcherOptions>)";

            throw new InvalidOperationException(
                $"Could not resolve {signature}. Ensure Microsoft.AspNetCore.SignalR is referenced.");
        }

        return method;
    }
}
