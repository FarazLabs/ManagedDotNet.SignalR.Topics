# TopicHubs (ManagedDotNet.SignalR.Topics)

Topic-based SignalR hubs: route by string topic, handle with DI-registered command handlers, serialize per message type.

## Communication flow

Two directions, two configs:


| Direction       | Client / server call     | Registration                                                  |
| --------------- | ------------------------ | ------------------------------------------------------------- |
| Client → server | `Handle(topic, payload)` | `HandleOnServer` — inbound topic → deserializer → handler     |
| Server → client | `Handle(topic, payload)` | `HandleOnClient` — outbound message type → topic → serializer |


- `**Handle(topic, payload)**` — client → server (library hub method)
![Client-To-Server Flow](https://raw.githubusercontent.com/FarazLabs/ManagedDotNet.SignalR.Topics/main/handleOnServer.svg)
- `**Handle(topic, payload)**` — server → client (listen on the client)
![Server-to-Client Flow](https://raw.githubusercontent.com/FarazLabs/ManagedDotNet.SignalR.Topics/main/handleOnClient.svg)

---



## ✨ Features

- 📫 **Topic-based hubs** — type-safe bindings between topics and message types for incoming/outgoing messages
- 🧩 **Decoupled message handling** — Native MediatR-style command handlers keep business logic (application layer) isolated from hub logic (infrastructure)
- 🛠️ **Custom (de)serializers** — per message type, or default `System.Text.Json` (`JsonSerializerDefaults.Web`: camelCase, case-insensitive)
- 🔐 **Authorization** — hub-level via `RequireAuthorization` / `AllowAnonymous` on `AddTopicHub` (MapHub-style); topic-level via `[Authorize]` / `[AllowAnonymous]` on handlers
- 🌐 **MapHub parity** — `RequireCors`, `ConfigureHttpConnection`, `WithMetadata`, `RequireHost`, `WithDisplayName`, and `ConfigureEndpoint` on `AddTopicHub` (applied by `MapTopicHubs`)
- 🔌**Modular setup** — `AddTopicHub` per module; `MapTopicHubs()` validates bindings and maps all hubs on the host

---



## 📦 Installation

```bash
dotnet add package ManagedDotNet.SignalR.Topics
```

Namespaces are `ManagedDotNet.SignalR.Topics.*`.

---



## 🏁 Quick Start



### 1. Register hubs

**OrderBookHub** — client sends a symbol on `subscribe` / `unsubscribe`, or JSON `{ "Reason": "..." }` on `terminate` (Administrator only); server pushes `OrderBookUpdate` on `update` and a plain-string `alert` on connect:

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
        cfg.WithTopic("update")
            .WithSerializer(update => JsonSerializer.Serialize(update)));

services.AddHostedService<OrderBookUpdateJob>();

// and within other modules
// services.AddTopicHub<SomeOtherModuleHub>("/SomeOtherModuleHub")
```

and of course :

```csharp
builder.Services.AddSignalR();
```



### 2. Create the hub

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

        // Inside the hub: use Clients (no ITopicHubContext inject)
        await Clients.Caller.Handle(alert);
    }
}
```



### 3. Create the handler

`IHubCommandHandler<T>` is registered automatically via `.WithHandler<T>()`.

**Inject** `ITopicHubContext<OrderBookHub>` **to access groups and reply to hub clients**

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

    public async Task Handle(SubscribeToSymbolCommand request, HubCallerContext context)
    {
        if (string.IsNullOrEmpty(request.Symbol))
            throw new ArgumentException("Symbol is null or empty");

        string? group = Symbols.GetAll()
            .FirstOrDefault(s => s.Equals(request.Symbol, StringComparison.OrdinalIgnoreCase));

        if (group == null)
            throw new Exception($"Symbol not found: {request.Symbol}");

        await _hubContext.Groups.AddToGroupAsync(context.ConnectionId, group);
    }
}

[Authorize(Roles = "Administrator")]
public class TerminateHubCommandHandler : IHubCommandHandler<TerminateCommand>
{
    private readonly IHostApplicationLifetime _lifetime;

    public TerminateHubCommandHandler
    (
        IHostApplicationLifetime lifetime
    )
    {
        _lifetime = lifetime;
    }

    public Task Handle(TerminateCommand request, HubCallerContext context, CancellationToken cancellationToken)
    {
        // request.Reason is required — soft-stop the host
        _lifetime.StopApplication();
        return Task.CompletedTask;
    }
}
```



### 4. Map hubs

Call once after all `AddTopicHub` registrations — validates every `HandleOnServer` / `HandleOnClient` binding (`WithTopic`, `WithHandler`, …), then maps every registered hub (replaces multiple `MapHub<T>` calls). Fluent options from `AddTopicHub` (auth, CORS, connection options, endpoint conventions) are applied here:

```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapTopicHubs();
});
```

---



## Authorization

Two layers (both must pass when both are set — AND):


| Layer                       | How you configure it                                                                              | When it runs                              |
| --------------------------- | ------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| **Hub** (connection)        | Fluent on `AddTopicHub`, same shape as `MapHub(...).RequireAuthorization(...)`                    | `MapTopicHubs` → endpoint conventions     |
| **Topic** (inbound message) | `[Authorize]` / `[AllowAnonymous]` on the **handler class** (SignalR method-attribute equivalent) | `HubCommandDispatcher` before deserialize |




### Hub (fluent)

```csharp
services.AddTopicHub<OrderBookHub>("/orderBook")
    .RequireAuthorization()                                    // default policy
    // .RequireAuthorization("TradingPolicy")                  // named policy
    // .RequireAuthorization("TradingPolicy", "AdminPolicy")   // multiple named policies
    // .RequireAuthorization(new AuthorizeAttribute { Roles = "User,Administrator" })
    // .RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("User").Build())
    // .RequireAuthorization(b => b.RequireRole("User").RequireClaim("scope", "orders"))
    // .AllowAnonymous()
    // MapHub parity (see Endpoint conventions):
    // .RequireCors()                                          // default CORS policy
    // .RequireCors("SignalRPolicy")                           // named CORS policy
    // .RequireCors(p => p.WithOrigins("https://app.example").AllowAnyHeader().AllowAnyMethod())
    // .ConfigureHttpConnection(o => { o.Transports = HttpTransportType.WebSockets; })
    // .WithDisplayName("Order book")
    // .RequireHost("localhost:5005")
    // .WithMetadata(new MyMetadata())
    // .ConfigureEndpoint(hub => hub.WithMetadata(...))        // escape hatch
    .HandleOnServer<SubscribeToSymbolCommand>(cfg =>
        cfg.WithTopic("subscribe")
            .WithHandler<SubscribeToSymbolHubCommandHandler>());
```

Same overloads as `MapHub(...).RequireAuthorization(...)`. `[Authorize]` on the hub class itself is still honored by SignalR and is **not** merged or overridden by fluent — if both are present, both apply.

### Topic (attributes on the handler)

There is no fluent `RequireAuthorization` on `HandleOnServer`. Put attributes on the handler:

```csharp
[Authorize(Roles = "User,Administrator")]
public class SubscribeToSymbolHubCommandHandler : IHubCommandHandler<SubscribeToSymbolCommand>
{
    public Task Handle(SubscribeToSymbolCommand request, HubCallerContext context) { /* ... */ }
}

[AllowAnonymous]
public class SomePublicCommandHandler : IHubCommandHandler<SomePublicCommand>
{
    public Task Handle(SomePublicCommand request, HubCallerContext context) { /* ... */ }
}
```

Attributes are baked at `.WithHandler<T>()` registration time.

---



## Endpoint conventions (MapHub parity)

Configure on `AddTopicHub` (not after `MapTopicHubs`). `MapTopicHubs` applies them when mapping each hub.


| Region                   | Fluent API                                                             | MapHub equivalent                          |
| ------------------------ | ---------------------------------------------------------------------- | ------------------------------------------ |
| **CORS**                 | `RequireCors()` / `RequireCors(name)` / `RequireCors(configurePolicy)` | `.RequireCors(...)`                        |
| **Connection**           | `ConfigureHttpConnection(configureOptions)`                            | `MapHub(path, configureOptions)`           |
| **Endpoint conventions** | `WithMetadata`, `RequireHost`, `WithDisplayName`, `ConfigureEndpoint`  | chained builder methods / escape hatch     |


```csharp
services.AddTopicHub<OrderBookHub>("/orderBook")
    .RequireAuthorization()
    .RequireCors("SignalRPolicy")                              // named CORS policy
    // .RequireCors()                                          // default CORS policy
    // .RequireCors(p => p.WithOrigins("https://app.example").AllowAnyHeader().AllowAnyMethod())
    .ConfigureHttpConnection(o =>
    {
        o.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
    })
    .WithDisplayName("Order book")
    // .RequireHost("localhost:5005")
    // .WithMetadata(new MyMetadata())
    // .ConfigureEndpoint(hub => hub.WithMetadata(...))        // anything else on HubEndpointConventionBuilder
    .HandleOnServer<SubscribeToSymbolCommand>(cfg =>
        cfg.WithTopic("subscribe")
            .WithHandler<SubscribeToSymbolHubCommandHandler>());
```

Still register CORS in the host (`AddCors` / `UseCors`) the same way you would with `MapHub`. Last `RequireCors` call wins.

---



## Messaging API



#### Outside the hub (controller / job / service)

Same inject as handlers: `ITopicHubContext<THub>` — not `IHubContext<THub>`.

```csharp
public class OrderBookUpdateJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public OrderBookUpdateJob
    (
        IServiceProvider serviceProvider
    )
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using IServiceScope scope = _serviceProvider.CreateScope();

            ITopicHubContext<OrderBookHub> hubContext =
                scope.ServiceProvider.GetRequiredService<ITopicHubContext<OrderBookHub>>();

            OrderBookUpdate update = new OrderBookUpdate
            {
                Symbol = "BTC/USDT",
                Price = 75_000m
            };

            await hubContext.Clients.Group(update.Symbol).Handle(update);
        }
    }
}
```



#### Inside the hub

No injection : invoke `Clients.Caller.Handle(alert);`

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

        // Inside the hub: use Clients (no ITopicHubContext inject)
        await Clients.Caller.Handle(alert);
    }
}
```

---



## Client (C#)

Wire signatures (always `string topic` + `string` payload):


| Direction                | Method name | Signature the client uses                                                          |
| ------------------------ | ----------- | ---------------------------------------------------------------------------------- |
| Listen (server → client) | `"Handle"`  | `void Handle(string topic, string payload)` via `HubConnection.On<string, string>` |
| Call (client → server)   | `"Handle"`  | `Task Handle(string topic, string message)` via `InvokeAsync`                      |


When the hub uses `RequireAuthorization()`, pass a JWT (demo: `AuthService.CreateToken`):

```csharp
string token = AuthService.CreateToken("trader", AuthService.Roles.User);

HubConnection orderBook = new HubConnectionBuilder()
    .WithUrl("http://localhost:5005/orderBook", o =>
        o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .Build();

// Must match: Handle(string topic, string payload)
orderBook.On<string, string>("Handle", async (string topic, string payload) =>
{
    switch (topic)
    {
        case "alert":
            // WithSerializer sends plain text (ConnectionAlert.Message), not JSON
            Console.WriteLine($"[alert] {payload}");
            break;

        case "update":
            OrderBookUpdate? update = JsonSerializer.Deserialize<OrderBookUpdate>(payload);
            if (update is null || string.IsNullOrWhiteSpace(update.Symbol))
                return;
            Console.WriteLine($"{update.Symbol}: {update.Price}");
            break;

        default:
            Console.WriteLine($"Unknown topic: {topic}");
            break;
    }
});

await orderBook.StartAsync();

// Must match: Handle(string topic, string message)
await orderBook.InvokeAsync("Handle", "subscribe", "BTC/USDT");
await orderBook.InvokeAsync("Handle", "unsubscribe", "BTC/USDT");

// Administrator-only topic — JSON TerminateCommand { Reason }
await orderBook.InvokeAsync("Handle", "terminate",
    JsonSerializer.Serialize(new TerminateCommand { Reason = "demo complete" }));
```

---



## Requirements

- .NET 8.0 or later
- Microsoft.AspNetCore.SignalR



## License

MIT

## Examples

See `[/Examples](../Examples/README.md)` for the OrderBook modular server + C# client. Start **App**, then **CSharpClient**.