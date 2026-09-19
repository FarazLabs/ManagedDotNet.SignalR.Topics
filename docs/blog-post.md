# Topic-based SignalR hubs for ASP.NET Core — introducing ManagedDotNet.SignalR.Topics

This post introduces **ManagedDotNet.SignalR.Topics** (successor to [ManagedSignalR](https://www.nuget.org/packages/ManagedSignalR)): topic-routed SignalR hubs with typed message bindings, per-message (de)serialization, and DI command handlers.

---

SignalR is excellent for real-time communication — but once an application grows beyond a handful of message types, hubs often get messy.

## Issues & motivation

- **No built-in message routing** — You either cram everything into one method with a giant `switch`, or scatter logic across dozens of hub methods. Neither scales cleanly in a modular codebase.
- **One-size-fits-all serialization** — SignalR’s global serializer makes it awkward when some topics want plain text, others JSON, others a custom wire format.
- **Business logic in the hub** — Handlers mixed into hub methods hurt testing and layering (application vs infrastructure).
- **Method-name sprawl** — Every new operation tends to mean another public hub method (and matching client invoke name) to keep in sync across services and clients.

I wanted a single, predictable wire shape, centralized topic bindings, and handlers that live outside the hub — without giving up normal SignalR groups, auth, and hosting.

## The idea

**ManagedDotNet.SignalR.Topics** keeps SignalR underneath and adds a **topic envelope**:

| Direction | Call | Registration |
| --- | --- | --- |
| Client → server | `Handle(topic, payload)` | `HandleOnServer` — topic → deserializer → `IHubCommandHandler<T>` |
| Server → client | `Handle(topic, payload)` | `HandleOnClient` — message type → topic → serializer |

Both directions use the same method name on the wire: **`Handle`**. The **topic** decides intent; the library maps that to types, (de)serializers, and DI handlers.

![Client → server](https://raw.githubusercontent.com/farazzbhn/SignalR.Topics/master/handleOnServer.svg)

![Server → client](https://raw.githubusercontent.com/farazzbhn/SignalR.Topics/master/handleOnClient.svg)

What you get:

- **Topic → type bindings** for inbound and outbound messages
- **MediatR-style** `IHubCommandHandler<T>` (auto-registered via `.WithHandler<T>()`)
- **Per-message (de)serializers**, or default `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase, case-insensitive)
- **Modular registration** — each feature module calls `AddTopicHub`; the host calls `MapTopicHubs()` once (validates bindings, maps hubs, applies conventions)
- **MapHub parity** — `RequireAuthorization` / `AllowAnonymous`, CORS, `ConfigureHttpConnection`, metadata/host/display name, plus an escape hatch
- **Topic auth** — `[Authorize]` / `[AllowAnonymous]` on handlers (checked before deserialize)

> **Honest framing:** this is not Microsoft “strongly typed hubs” (`Hub<TClient>` with named client methods). Clients still listen/invoke `Handle(string topic, string payload)`. The “typed” part is the **server-side binding** from topics and CLR types to handlers and serializers.

## Getting started

### 1. Install

```bash
dotnet add package ManagedDotNet.SignalR.Topics
```

Namespaces live under `ManagedDotNet.SignalR.Topics.*`. Current line is **0.1.0** (early but intentional).

### 2. Register hubs (often per module)

Example: an order-book hub — clients `subscribe` / `unsubscribe` with a symbol string, `terminate` with JSON (Administrators only); server pushes `update` JSON and a plain-text `alert` on connect:

```csharp
services.AddTopicHub<OrderBookHub>("/orderBook")
    .RequireAuthorization()

    .HandleOnServer<SubscribeToSymbolCommand>(cfg =>
        cfg.WithTopic("subscribe")
            .WithDeserializer(str => new SubscribeToSymbolCommand
            {
                Symbol = str.Trim().ToUpper()
            })
            .WithHandler<SubscribeToSymbolHubCommandHandler>())

    .HandleOnServer<UnsubscribeFromSymbolCommand>(cfg =>
        cfg.WithTopic("unsubscribe")
            .WithDeserializer(str => new UnsubscribeFromSymbolCommand
            {
                Symbol = str.Trim().ToUpper()
            })
            .WithHandler<UnsubscribeFromSymbolHubCommandHandler>())

    .HandleOnServer<TerminateCommand>(cfg =>
        cfg.WithTopic("terminate")
            .WithHandler<TerminateHubCommandHandler>())

    .HandleOnClient<ConnectionAlert>(cfg =>
        cfg.WithTopic("alert")
            .WithSerializer(alert => alert!.Message))

    .HandleOnClient<OrderBookUpdate>(cfg =>
        cfg.WithTopic("update")); // default Web JSON

// other modules:
// services.AddTopicHub<ChatHub>("/chat")...
```

Still register SignalR as usual:

```csharp
builder.Services.AddSignalR();
```

Map everything in one place on the host (replaces scattered `MapHub<T>` calls for topic hubs):

```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapTopicHubs();
});
```

`MapTopicHubs` validates incomplete bindings, maps each registered hub, applies auth/CORS/connection conventions from `AddTopicHub`, then seals configuration.

### 3. Create the hub

Inherit `TopicHub`. Use normal SignalR lifecycle overrides. Inside the hub, send typed outbound messages with `Clients.*.Handle(...)` — do **not** inject `ITopicHubContext` into the hub for that.

```csharp
public class OrderBookHub : TopicHub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        ConnectionAlert alert = new ConnectionAlert
        {
            Message = "Welcome! You are connected to the order book hub."
        };

        await Clients.Caller.Handle(alert);
    }
}
```

### 4. Implement handlers

Handlers are registered by `.WithHandler<T>()`. Inject `ITopicHubContext<THub>` to reach groups or reply from application code:

```csharp
[Authorize(Roles = "User,Administrator")]
public class SubscribeToSymbolHubCommandHandler : IHubCommandHandler<SubscribeToSymbolCommand>
{
    private readonly ITopicHubContext<OrderBookHub> _hubContext;

    public SubscribeToSymbolHubCommandHandler
    (
        ITopicHubContext<OrderBookHub> hubContext
    )
    {
        _hubContext = hubContext;
    }

    public async Task Handle(
        SubscribeToSymbolCommand request,
        HubCallerContext context,
        CancellationToken cancellationToken)
    {
        // validate symbol, then:
        await _hubContext.Groups.AddToGroupAsync(
            context.ConnectionId,
            request.Symbol,
            cancellationToken);
    }
}
```

**Authorization reminder**

| Layer | Configured how | Gates |
| --- | --- | --- |
| Hub | `.RequireAuthorization()` / `.AllowAnonymous()` on `AddTopicHub` | Connecting to the hub |
| Topic | `[Authorize]` / `[AllowAnonymous]` on the **handler** | Invoking that topic |

Hub auth does not replace per-topic roles. If a handler has no `[Authorize]`, any client allowed on the hub can call that topic.

### 5. Send from outside the hub

From a background job, controller, or service, inject `ITopicHubContext<THub>` (not raw `IHubContext<THub>` if you want topic routing):

```csharp
ITopicHubContext<OrderBookHub> hubContext =
    scope.ServiceProvider.GetRequiredService<ITopicHubContext<OrderBookHub>>();

OrderBookUpdate update = new OrderBookUpdate
{
    Symbol = "BTC/USDT",
    Price = 75_000m
};

await hubContext.Clients.Group(update.Symbol).Handle(update, stoppingToken);
```

Prefer `.Handle(message)` over inventing your own `SendAsync("Handle", ...)`. The library picks the topic and serializer from `HandleOnClient` registration.

## Client code

Clients always use the same wire contract:

| Direction | Method | Signature |
| --- | --- | --- |
| Listen (server → client) | `"Handle"` | `On<string, string>("Handle", (topic, payload) => …)` |
| Call (client → server) | `"Handle"` | `InvokeAsync("Handle", topic, message)` |

### JavaScript / TypeScript

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/orderBook", { accessTokenFactory: () => token })
    .build();

connection.on("Handle", (topic, payload) => {
    switch (topic) {
        case "alert":
            // custom serializer may send plain text
            console.log(`[alert] ${payload}`);
            break;
        case "update":
            const update = JSON.parse(payload);
            console.log(`${update.symbol}: ${update.price}`);
            break;
        default:
            console.log(`[unknown topic] ${topic} => ${payload}`);
    }
});

await connection.start();
await connection.invoke("Handle", "subscribe", "BTC/USDT");
```

### C#

```csharp
HubConnection connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5005/orderBook", o =>
        o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .WithAutomaticReconnect()
    .Build();

connection.On<string, string>("Handle", (string topic, string payload) =>
{
    switch (topic)
    {
        case "alert":
            Console.WriteLine($"[alert] {payload}");
            break;
        case "update":
            OrderBookUpdate? update =
                JsonSerializer.Deserialize<OrderBookUpdate>(payload);
            Console.WriteLine($"{update?.Symbol}: {update?.Price}");
            break;
    }
});

await connection.StartAsync();
await connection.InvokeAsync("Handle", "subscribe", "BTC/USDT");
```

## Coming from ManagedSignalR?

Rough rename map:

| ManagedSignalR | ManagedDotNet.SignalR.Topics |
| --- | --- |
| `ManagedHub` | `TopicHub` |
| `InvokeServer` / `InvokeClient` | `Handle` / `Handle` (both directions) |
| `AddManagedSignalR` + `AddManagedHub` | `AddTopicHub` (+ `MapTopicHubs`) |
| `ConfigureInvokeServer` / `ConfigureInvokeClient` | `HandleOnServer` / `HandleOnClient` |
| `OnTopic` / `RouteToTopic` | `WithTopic` |
| `UseDeserializer` / `UseSerializer` / `UseHandler` | `WithDeserializer` / `WithSerializer` / `WithHandler` |
| `IManagedHubContext` / `TryInvokeClientAsync` | `ITopicHubContext` / `Handle` |
| Manual `MapHub<T>` | `MapTopicHubs()` |
| Lifecycle “hooks” | Standard `OnConnectedAsync` / `OnDisconnectedAsync` |

Also new: hub-level fluent auth/CORS/conventions, topic `[Authorize]` on handlers, modular seal/validate at map time, and cancellation support on handlers / outbound `Handle`.

## When to use this (and when not to)

**Use it when** you have many message shapes, want handlers outside the hub, need per-topic payload formats, or register hubs from multiple modules and want one `MapTopicHubs()` on the host.

**Prefer plain SignalR** (or Hub → MediatR) when you only have a few hub methods and are happy with named methods / `Hub<TClient>`.

## Links

- NuGet: [ManagedDotNet.SignalR.Topics](https://www.nuget.org/packages/ManagedDotNet.SignalR.Topics)
- GitHub: [farazzbhn/SignalR.Topics](https://github.com/farazzbhn/SignalR.Topics)
- Library docs: [src/README.md](https://github.com/farazzbhn/SignalR.Topics/blob/master/src/README.md)
- Runnable demo: [Examples/README.md](https://github.com/farazzbhn/SignalR.Topics/blob/master/Examples/README.md)

Questions and issues welcome on the repository — the OrderBook sample (JWT roles + subscribe/update/terminate) is the fastest way to see the full loop.
