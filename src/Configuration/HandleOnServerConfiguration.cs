using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ManagedDotNet.SignalR.Topics.Abstractions;
using ManagedDotNet.SignalR.Topics.Types.Exceptions;

namespace ManagedDotNet.SignalR.Topics.Configuration;

public abstract class HandleOnServerConfiguration
{
    internal string? Topic { get; set; } = null;
    internal Type? HandlerType { get; set; } = null;
    internal IAuthorizeData[] AuthorizeData { get; set; } = Array.Empty<IAuthorizeData>();
    internal bool IsAnonymousAllowed { get; set; }

    /// <summary>
    /// Casts the handler to a <see cref="IHubCommandHandler{TModel}"/> and calls the <see cref="IHubCommandHandler{TModel}.Handle"/> method. 
    /// ( TModel is baked at registration time and therefore available) <br/>
    /// This allows for the generic dispatcher to call the <see cref="IHubCommandHandler{TModel}.Handle"/> method without reflection.
    /// </summary>
    internal Func<object, object?, HubCallerContext, CancellationToken, Task>? Invoke { get; set; }
    internal abstract object? Deserialize(string payload);

    /// <summary>
    /// Ensures that the current mapping configuration is complete and valid.
    /// </summary>
    internal abstract void EnsureIsValid();
}

public sealed class HandleOnServerConfiguration<TModel> : HandleOnServerConfiguration
{
    private static readonly System.Text.Json.JsonSerializerOptions DefaultJson =
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

    internal Func<string, TModel?> Deserializer { get; private set; } = message =>
        System.Text.Json.JsonSerializer.Deserialize<TModel>(message, DefaultJson);

    internal override object? Deserialize(string payload) => Deserializer(payload);

    /// <summary>
    /// <b>Required</b> —
    /// Sets the topic for incoming messages
    /// </summary>
    public HandleOnServerConfiguration<TModel> WithTopic(string topic)
    {
        base.Topic = topic;
        return this;
    }

    /// <summary>
    /// <b>Optional</b> —
    /// Overrides the default <see cref="System.Text.Json.JsonSerializer"/> 
    /// </summary>
    public HandleOnServerConfiguration<TModel> WithDeserializer(Func<string, TModel?> deserializer)
    {
        this.Deserializer = deserializer;
        return this;
    }

    /// <summary>
    /// <b>Required</b> | Sets the handler type for processing messages 
    /// </summary>
    public HandleOnServerConfiguration<TModel> WithHandler<THandler>() where THandler : IHubCommandHandler<TModel>
    {
        HandlerType = typeof(THandler);

        // Bake [Authorize] / [AllowAnonymous] from the handler class (SignalR method-attr equivalent).
        Attribute[] attrs = Attribute.GetCustomAttributes(typeof(THandler), inherit: true);
        AuthorizeData = attrs.OfType<IAuthorizeData>().ToArray();
        IsAnonymousAllowed = attrs.OfType<IAllowAnonymous>().Any();

        Invoke = (handler, cmd, ctx, ct) =>
            ((IHubCommandHandler<TModel>)handler).Handle((TModel)cmd!, ctx, ct);

        return this;
    }

    internal override void EnsureIsValid()
    {
        if (string.IsNullOrWhiteSpace(base.Topic))
            throw new MisconfiguredException(
                $"Topic is not configured for message type '{typeof(TModel).Name}'.\n" +
                $"Use .WithTopic(\"your-topic\") to bind the message type ({typeof(TModel).Name}) to a specific topic.");

        if (HandlerType == null)
            throw new MisconfiguredException(
                $"Handler for '{typeof(TModel).Name}' is not registered.\n" +
                $"You must specify a concrete handler using .WithHandler<YourHandler>() where YourHandler : IHubCommandHandler<{typeof(TModel).Name}>."
            );

        // WithHandler sets HandlerType + Invoke together; guard the pair if either was left unset
        if (Invoke == null)
            throw new MisconfiguredException(
                $"Invoke delegate for '{typeof(TModel).Name}' is not set.\n" +
                $"Call .WithHandler<YourHandler>() so the typed invoker is baked at registration."
            );
    }
}
