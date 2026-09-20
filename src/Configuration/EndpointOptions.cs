using ManagedDotNet.SignalR.Topics.Types.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedDotNet.SignalR.Topics.Configuration;


/// <summary>
/// Holds mappings and other configuration options for a specific SignalR hub endpoint.
/// </summary>
public sealed class EndpointOptions
{
    internal IServiceCollection? Services { get; set; }
    public readonly EndpointOptionRegistry Parent;

    /// <summary>
    /// Returns to the parent <see cref="EndpointOptionRegistry"/> builder
    /// to allow configuring an additional hub on the same options instance.
    /// Prefer <c>services.AddTopicHub&lt;THub&gt;(path)</c> per module in a modular monolith.
    /// </summary>
    /// <returns>The parent <see cref="EndpointOptionRegistry"/> instance for fluent chaining.</returns>
    public EndpointOptionRegistry And() => Parent;
    

    public EndpointOptions
    (
        Type hubType,
        string path,
        EndpointOptionRegistry parent,
        IServiceCollection services
    )
    {
        HubType = hubType;
        Path = path;
        Parent = parent;
        Services = services;
        _handleOnServerConfigurations = new();
        _handleOnClientConfigurations = new();
        _endpointConventions = new();
    }

    private bool _frozen;

    /// <summary>
    /// Finalize the object &amp; clear the unnecessary references
    /// </summary>
    internal void Freeze()
    {
        Services = null;
        _frozen = true;
        foreach (HandleOnServerConfiguration cfg in _handleOnServerConfigurations.Values)
            cfg.Freeze();
        foreach (HandleOnClientConfiguration cfg in _handleOnClientConfigurations.Values)
            cfg.Freeze();
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException(
                $"Cannot modify hub '{HubType.FullName}' after MapTopicHubs() has sealed configuration.");
    }


    /// <summary>
    /// Hub type being configured
    /// </summary>
    internal Type HubType { get; set; }

    /// <summary>
    /// Route path used when mapping this hub (e.g. <c>/apphub</c>).
    /// </summary>
    internal string Path { get; set; }

    #region AUTH CONFIGURATIONS

    private IAuthorizeData[] _authorizeData = Array.Empty<IAuthorizeData>();
    private AuthorizationPolicy[] _authorizationPolicies = Array.Empty<AuthorizationPolicy>();
    private bool _allowsAnonymous;

    // MapHub parity: neither call → no endpoint auth metadata (fallback policy still applies).
    internal bool RequiresAuthorization => _authorizeData.Length > 0 || _authorizationPolicies.Length > 0;
    internal bool AllowsAnonymous => _allowsAnonymous && !RequiresAuthorization;
    internal IAuthorizeData[] AuthorizeData => _authorizeData;
    internal AuthorizationPolicy[] AuthorizationPolicies => _authorizationPolicies;


    /// <summary> <b>Optional</b> 
    /// | Requires the default authorization policy to connect to this hub (MapHub-style). 
    /// </summary>
    public EndpointOptions RequireAuthorization()
    {
        ThrowIfFrozen();
        _authorizeData = _authorizeData.Append(new AuthorizeAttribute()).ToArray();
        return this;
    }

    /// <summary> <b>Optional</b> | 
    /// Requires the named authorization policies to connect to this hub. </summary>
    public EndpointOptions RequireAuthorization(params string[] policyNames)
    {
        ThrowIfFrozen();
        _authorizeData = _authorizeData
            .Concat(policyNames.Select(static name => (IAuthorizeData)new AuthorizeAttribute { Policy = name }))
            .ToArray();
        return this;
    }

    /// <summary> <b>Optional</b> |
    ///  Requires the given authorize data (e.g. <see cref="AuthorizeAttribute"/> with Roles) to connect to this hub. </summary>
    public EndpointOptions RequireAuthorization(params IAuthorizeData[] authorizeData)
    {
        ThrowIfFrozen();
        _authorizeData = _authorizeData.Concat(authorizeData).ToArray();
        return this;
    }

    /// <summary> <b>Optional</b> |
    /// Requires the given authorization policy to connect to this hub.
    /// </summary>
    public EndpointOptions RequireAuthorization(AuthorizationPolicy policy)
    {
        ThrowIfFrozen();
        _authorizationPolicies = _authorizationPolicies.Append(policy).ToArray();
        return this;
    }

    /// <summary> <b>Optional</b> |
    /// Builds a policy via <paramref name="configurePolicy"/> and requires it to connect to this hub.
    /// </summary>
    public EndpointOptions RequireAuthorization(Action<AuthorizationPolicyBuilder> configurePolicy)
    {
        ThrowIfFrozen();
        AuthorizationPolicyBuilder builder = new AuthorizationPolicyBuilder();
        configurePolicy(builder);
        return RequireAuthorization(builder.Build());
    }

    /// <summary> <b>Optional</b> |
    /// Allows anonymous connections to this hub.
    /// </summary>
    public EndpointOptions AllowAnonymous()
    {
        ThrowIfFrozen();
        _authorizeData = Array.Empty<IAuthorizeData>();
        _authorizationPolicies = Array.Empty<AuthorizationPolicy>();
        _allowsAnonymous = true;
        return this;
    }

    #endregion


    #region CORS CONFIGURATIONS

    // Last RequireCors call wins (MapHub usually sets one policy per hub).
    private Action<HubEndpointConventionBuilder>? _applyCors;
    internal Action<HubEndpointConventionBuilder>? ApplyCors => _applyCors;

    /// <summary> <b>Optional</b> |
    ///  Adds the default CORS policy to this hub (MapHub-style <c>RequireCors()</c>). 
    /// </summary>
    public EndpointOptions RequireCors()
    {
        ThrowIfFrozen();
        _applyCors = static builder => builder.RequireCors();
        return this;
    }

    /// <summary> <b>Optional</b> |
    ///  Adds the named CORS policy to this hub. 
    /// </summary>
    public EndpointOptions RequireCors(string policyName)
    {
        ThrowIfFrozen();
        _applyCors = builder => builder.RequireCors(policyName);
        return this;
    }

    /// <summary> <b>Optional</b> |
    ///  Builds a CORS policy via <paramref name="configurePolicy"/> and requires it on this hub. 
    /// </summary>
    public EndpointOptions RequireCors(Action<CorsPolicyBuilder> configurePolicy)
    {
        ThrowIfFrozen();
        _applyCors = builder => builder.RequireCors(configurePolicy);
        return this;
    }

    #endregion


    #region CONNECTION OPTIONS

    private Action<HttpConnectionDispatcherOptions>? _configureHttpConnection;
    internal Action<HttpConnectionDispatcherOptions>? ConfigureHttpConnectionCallback => _configureHttpConnection;

    /// <summary> <b>Optional</b> | 
    /// Configures SignalR HTTP connection dispatcher options for this hub (MapHub-style <c>ConfigureHttpConnection</c>).
    /// </summary>
    public EndpointOptions ConfigureHttpConnection(Action<HttpConnectionDispatcherOptions> configureOptions)
    {
        ThrowIfFrozen();
        _configureHttpConnection = configureOptions;
        return this;
    }

    #endregion


    #region ENDPOINT CONVENTIONS

    private readonly List<Action<HubEndpointConventionBuilder>> _endpointConventions;
    internal IReadOnlyList<Action<HubEndpointConventionBuilder>> EndpointConventions => _endpointConventions;

    /// <summary> <b>Optional</b> | Adds arbitrary endpoint metadata to this hub (MapHub-style <c>WithMetadata</c>). </summary>
    public EndpointOptions WithMetadata(params object[] items)
    {
        ThrowIfFrozen();
        _endpointConventions.Add(builder => builder.WithMetadata(items));
        return this;
    }

    /// <summary> <b>Optional</b> | Restricts this hub to the given hosts (MapHub-style <c>RequireHost</c>). </summary>
    public EndpointOptions RequireHost(params string[] hosts)
    {
        ThrowIfFrozen();
        _endpointConventions.Add(builder => builder.RequireHost(hosts));
        return this;
    }

    /// <summary> <b>Optional</b> | Sets the display name for this hub endpoint (MapHub-style <c>WithDisplayName</c>). </summary>
    public EndpointOptions WithDisplayName(string displayName)
    {
        ThrowIfFrozen();
        _endpointConventions.Add(builder => builder.WithDisplayName(displayName));
        return this;
    }

    /// <summary> <b>Optional</b> | Escape hatch for any other <see cref="HubEndpointConventionBuilder"/> conventions not mirrored on this type. </summary>
    public EndpointOptions ConfigureEndpoint(Action<HubEndpointConventionBuilder> configure)
    {
        ThrowIfFrozen();
        _endpointConventions.Add(configure);
        return this;
    }

    #endregion


    #region  TOPIC CONFIGURATIONS
    private Dictionary<string, HandleOnServerConfiguration> _handleOnServerConfigurations { get; set; }

    private Dictionary<Type, HandleOnClientConfiguration> _handleOnClientConfigurations { get; set; }

    internal IReadOnlyDictionary<string, HandleOnServerConfiguration> HandleOnServerConfigurations => _handleOnServerConfigurations;
    internal IReadOnlyDictionary<Type, HandleOnClientConfiguration> HandleOnClientConfigurations => _handleOnClientConfigurations;

    /// <summary> <b>Required</b> | Configures how messages are sent to clients </summary>
    public EndpointOptions HandleOnClient<TOutboundMessage>(Action<HandleOnClientConfiguration<TOutboundMessage>> configurer)
    {
        ThrowIfFrozen();
        HandleOnClientConfiguration<TOutboundMessage> configuration = new HandleOnClientConfiguration<TOutboundMessage>();

        configurer.Invoke(configuration);
        configuration.EnsureIsValid();

        // Same hub must not emit two outbound types on one topic string
        if (_handleOnClientConfigurations.Values.Any(c => c.Topic == configuration.Topic))
        {
            throw new MisconfiguredException(
                $"Topic '{configuration.Topic}' is already registered on hub '{HubType.FullName}'. Use a distinct topic per HandleOnClient binding.");
        }

        // One outbound DTO type → one topic per hub
        if (!_handleOnClientConfigurations.TryAdd(typeof(TOutboundMessage), configuration))
        {
            throw new MisconfiguredException(
                $"Outbound message type '{typeof(TOutboundMessage).FullName}' is already registered on hub '{HubType.FullName}'. Use a distinct type per HandleOnClient binding.");
        }

        return this;
    }

    /// <summary> <b>Required</b> | Configures how messages are received from clients </summary>
    public EndpointOptions HandleOnServer<TInboundMessage>(Action<HandleOnServerConfiguration<TInboundMessage>> configurer)
    {
        ThrowIfFrozen();
        HandleOnServerConfiguration<TInboundMessage> configuration = new HandleOnServerConfiguration<TInboundMessage>();

        configurer.Invoke(configuration);
        configuration.EnsureIsValid();

        // Same hub must not bind two handlers to one topic string
        if (!_handleOnServerConfigurations.TryAdd(configuration.Topic!, configuration))
        {
            throw new MisconfiguredException(
                $"Topic '{configuration.Topic}' is already registered on hub '{HubType.FullName}'. Use a distinct topic per HandleOnServer binding.");
        }

        Services!.AddScoped(configuration.HandlerType!);

        return this;
    }

    #endregion


    /// <summary>
    /// Validates outbound routes for this hub.
    /// Called from <c>MapTopicHubs</c> before hubs are mapped.
    /// </summary>
    internal void EnsureIsValid()
    {
        foreach (HandleOnClientConfiguration configuration in _handleOnClientConfigurations.Values)
        {
            configuration.EnsureIsValid();
        }
    }


}
