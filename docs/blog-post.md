# Introducing ManagedDotNet.SignalR.Topics — topic-based SignalR hubs that stay clean as you grow

This post introduces a library I developed — **ManagedDotNet.SignalR.Topics** — providing topic-based SignalR hubs with typed message bindings, custom serialization, and DI-based mediator-style handlers.

---

SignalR is fantastic for real-time communication — but once your application grows beyond a handful of message types, things can get complicated.

## Issues & motivation

- **No built-in message routing** — You either cram everything into a single receive method with a giant switch/if block, or scatter your logic across dozens of hub methods. Neither approach scales well.
- **One-size-fits-all serialization** — SignalR uses a single global serializer by default, making it hard to fine-tune formats or honor different data contracts for different message types.
- **Poor separation of concerns** — Business logic often ends up living inside the hub, which makes testing, maintenance, and scaling far more painful than it needs to be.
- **Hub registration fights the modular monolith** — Each feature wants its own realtime surface, but classic `MapHub` wiring tends to pile up in the host. Modules can’t cleanly own “register my hub + its messages” without leaking SignalR setup across composition roots, and the composition root becomes the place every module must touch to go live.

I set out to fix that — with typed messages, centralized topic routing, and clean, testable handlers.

The result? SignalR hubs that are simpler, cleaner, **and a joy to maintain!**

## ManagedDotNet.SignalR.Topics to the rescue!

ManagedDotNet.SignalR.Topics extends SignalR with one primary communication method in both directions:

**`Handle(string topic, string payload)`** — used by clients to send structured messages to the server (implemented by the library on `TopicHub`).

![Client → server](https://raw.githubusercontent.com/FarazLabs/ManagedDotNet.SignalR.Topics/main/handleOnServer.svg)

**`Handle(string topic, string payload)`** — used by the server to deliver structured messages back to clients. Clients listen for this method on the connection.

![Server → client](https://raw.githubusercontent.com/FarazLabs/ManagedDotNet.SignalR.Topics/main/handleOnClient.svg)

The core concept behind both is **topics**. Every message is tied to a topic, which defines its intent and determines how it should be deserialized, routed, and handled. By enforcing this topic-oriented model, ManagedDotNet.SignalR.Topics gives you a predictable, type-bound pipeline for both directions of communication.

Under the hood you also get:

- MediatR-style `IHubCommandHandler<T>` handlers (registered automatically via `.WithHandler<T>()`)
- Per-message (de)serializers — or default `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase, case-insensitive)
- Modular setup — `AddTopicHub` per feature module, one `MapTopicHubs()` on the host
- MapHub-style auth, CORS, connection options, and endpoint conventions
- Topic-level `RequireAuthorization` / `AllowAnonymous` on `HandleOnServer`

## Getting started

### 1. Install ManagedDotNet.SignalR.Topics

Add the NuGet package to your project:

```bash
dotnet add package ManagedDotNet.SignalR.Topics
```

Namespaces live under `ManagedDotNet.SignalR.Topics.*`.

### 2. Configure incoming / outgoing topic bindings

In your module (or `Program.cs`), define the topic bindings that map message types to topics and handlers. This tells the server how to route incoming messages from clients — and how to send messages back:

```csharp
services.AddTopicHub<OrderBookHub>("/orderBook")
    .RequireAuthorization()                                    // default policy
    // .RequireAuthorization("TradingPolicy")                  // named policy
    // .RequireAuthorization(b => b.RequireRole("User"))       // ...
    // .AllowAnonymous()
    // .RequireCors("SignalRPolicy")                           // ... CORS, connection options, metadata, etc.

    // --- CLIENT → SERVER ---
    // Clients send "subscribe" with a plain symbol string (e.g. "btc/usdt").
    // 1. Bind the topic
    // 2. Deserialize into your command type
    // 3. Assign the handler (also registers it in DI)
    .HandleOnServer<SubscribeToSymbolCommand>(cfg =>
        cfg.WithTopic("subscribe")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "User,Administrator" })
            .WithDeserializer(str => new SubscribeToSymbolCommand
            {
                Symbol = str.Trim().ToUpper()
            })
            .WithHandler<SubscribeToSymbolHubCommandHandler>())

    .HandleOnServer<UnsubscribeFromSymbolCommand>(cfg =>
        cfg.WithTopic("unsubscribe")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "User,Administrator" })
            .WithDeserializer(str => new UnsubscribeFromSymbolCommand
            {
                Symbol = str.Trim().ToUpper()
            })
            .WithHandler<UnsubscribeFromSymbolHubCommandHandler>())

    .HandleOnServer<TerminateCommand>(cfg =>
        cfg.WithTopic("terminate")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Administrator" })
            .WithHandler<TerminateHubCommandHandler>())

    // --- SERVER → CLIENT ---
    // Plain-text welcome alert on connect
    .HandleOnClient<ConnectionAlert>(cfg =>
        cfg.WithTopic("alert")
            .WithSerializer(alert => alert!.Message))

    // JSON order-book updates (default Web JSON if you omit WithSerializer)
    .HandleOnClient<OrderBookUpdate>(cfg =>
        cfg.WithTopic("update"));

// and within other modules
// services.AddTopicHub<ChatHub>("/chat")...
```

Not to forget the default SignalR registration:

```csharp
builder.Services.AddSignalR();
// add Redis backplane for distributed SignalR ...
```

And of course — map all topic hubs in one shot on the host:

```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapTopicHubs();
});
```

`MapTopicHubs()` validates your bindings, maps every hub registered with `AddTopicHub`, and applies the fluent conventions you configured (auth, CORS, connection options, and more).

### 3. Create the TopicHub

Implement your hub by inheriting from `TopicHub`. Override the usual SignalR lifecycle methods when you need connect/disconnect logic:

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

### 4. Implement the topic command handlers

`IHubCommandHandler<T>` handlers process incoming commands once they have been deserialized. They are registered with DI via `.WithHandler<T>()` and can take constructor dependencies. Put topic auth on the `HandleOnServer` chain, not on the handler class:

```csharp
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
        // validate symbol, then join the SignalR group for that symbol
        await _hubContext.Groups.AddToGroupAsync(
            context.ConnectionId,
            request.Symbol,
            cancellationToken);
    }
}
```

At this point, your server knows how to receive messages, handle serialization, and route each topic to the right handler.

**Authorization tip:** hub-level `.RequireAuthorization()` gates *connecting*. Topic-level `.RequireAuthorization()` / `.AllowAnonymous()` on `HandleOnServer` gates *invoking that topic*. Both can apply (AND). Handler-class `[Authorize]` attributes are ignored — use fluent APIs. Empty `RequireAuthorization(params …)` throws; message types and handlers must be `class`es. Hub auth + topic `AllowAnonymous` still requires an authenticated connection (`MapTopicHubs` warns).

### 5. Sending messages from outside the hub

But what if you want to send messages from outside the hub — like from a controller or a background service? That’s where `ITopicHubContext<THub>` comes in. Just inject it, then call:

```csharp
await hubContext.Clients.All.Handle(configuredMessage);
```

**Important:** prefer `.Handle(message)` over calling `SendAsync("Handle", …)` yourself — doing so bypasses ManagedDotNet.SignalR.Topics routing and serialization.

For example, from a job:

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

Or from an API controller:

```csharp
[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly ITopicHubContext<OrderBookHub> _hubContext;

    public NotificationController
    (
        ITopicHubContext<OrderBookHub> hubContext
    )
    {
        _hubContext = hubContext;
    }

    [HttpPost("broadcast")]
    public async Task<IActionResult> BroadcastAlert([FromBody] ConnectionAlert alert)
    {
        await _hubContext.Clients.All.Handle(alert);
        return Ok();
    }
}
```

## Client code

SignalR requires that you implement the client-side listener as well. Use the snippets below to handle server-sent events.

### JavaScript / TypeScript

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/orderBook", { accessTokenFactory: () => token })
    .build();

// Listen for messages from server
connection.on("Handle", (topic, payload) => {
    switch (topic) {
        case "alert":
            // WithSerializer may send plain text — not always JSON
            console.log(`ALERT!!!\t${payload}`);
            break;
        case "update":
            const update = JSON.parse(payload);
            console.log(`UPDATE*\t${update.symbol}: ${update.price}`);
            break;
        default:
            console.log(`[unexpected topic]\t${topic} => ${payload}`);
            break;
    }
});

// Send message to server
await connection.start();
await connection.invoke("Handle", "subscribe", "BTC/USDT");
```

### C# clients

```csharp
HubConnection connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5005/orderBook", o =>
        o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .WithAutomaticReconnect()
    .Build();

// Handle server-initiated messages via Handle(topic, payload)
connection.On<string, string>("Handle", (string topic, string payload) =>
{
    switch (topic)
    {
        case "alert":
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"ALERT!!!\t{payload}");
            Console.ResetColor();
            break;

        case "update":
            OrderBookUpdate? update =
                System.Text.Json.JsonSerializer.Deserialize<OrderBookUpdate>(payload);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"UPDATE*\t{update?.Symbol}: {update?.Price}");
            Console.ResetColor();
            break;

        default:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[unexpected topic]\t{topic} => {payload}");
            Console.ResetColor();
            break;
    }
});

await connection.StartAsync();
await connection.InvokeAsync("Handle", "subscribe", "BTC/USDT");
```

## Wrap-up

This blog post is associated with the ManagedDotNet.SignalR.Topics NuGet package:  
https://www.nuget.org/packages/ManagedDotNet.SignalR.Topics  

and the GitHub repository:  
https://github.com/FarazLabs/ManagedDotNet.SignalR.Topics  

For more details, examples, and to ask questions, check out the repository — everything you need to get started is there (including a full OrderBook server + C# client demo). Happy building!
