using ManagedDotNet.SignalR.Topics.Types.Exceptions;

namespace ManagedDotNet.SignalR.Topics.Configuration;

public abstract class HandleOnClientConfiguration
{
    private bool _frozen;

    internal void Freeze() => _frozen = true;

    protected void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException(
                "Cannot modify HandleOnClient configuration after MapTopicHubs() has sealed configuration.");
    }

    internal string? Topic { get; set; } = null;
    internal abstract string Serialize(object? message);

    /// <summary>
    /// Ensures that the current mapping configuration is complete and valid.
    /// </summary>
    internal abstract void EnsureIsValid();
}


public sealed class HandleOnClientConfiguration<TModel> : HandleOnClientConfiguration
{
    private static readonly System.Text.Json.JsonSerializerOptions DefaultJson =
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

    internal Func<TModel?, string> Serializer { get; private set; } = message =>
        System.Text.Json.JsonSerializer.Serialize(message, DefaultJson);
    // Routes are keyed by the message's runtime type, so the cast always matches TModel exactly
    internal override string Serialize(object? message) =>
        Serializer(message is null ? default : (TModel)message);

    /// <summary>
    /// <b>Required</b> —
    /// Sets the topic name used when invoking client method <c>Handle</c>
    /// during the publishing process for this configuration.
    /// </summary>
    public HandleOnClientConfiguration<TModel> WithTopic(string topic)
    {
        ThrowIfFrozen();
        base.Topic = topic;
        return this;
    }

    /// <summary>
    /// <b>Optional</b> —
    /// Overrides the default <see cref="System.Text.Json.JsonSerializer"/> 
    /// </summary>
    public HandleOnClientConfiguration<TModel> WithSerializer(Func<TModel?, string> serializer)
    {
        ThrowIfFrozen();
        Serializer = serializer;
        return this;
    }

    internal override void EnsureIsValid()
    {
        if (string.IsNullOrWhiteSpace(base.Topic))
            throw new MisconfiguredException(
                $"Topic is not configured for outgoing message of type '{typeof(TModel).Name}'.\n" +
                $"Use .WithTopic(\"your-topic\") to route the message type ({typeof(TModel).Name}) to a specific topic.");
    }
}
