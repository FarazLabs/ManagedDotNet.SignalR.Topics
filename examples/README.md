# ManagedDotNet.SignalR.Topics Demo

Modular monolith server plus a C# client for **ManagedDotNet.SignalR.Topics**.

## Layout

```text
Examples/
  Shared/                   # IModule, AuthService (JWT mint/validate), PrettyPrint, OrderBookUpdate
  Clients/CSharpClient/     # OrderBookHubDemo (JWT roles + topics)
  Server/
    Modules/Orders/         # OrderBookHub, handlers, OrderBookUpdateJob, Symbols
    App/                    # hosts Orders module on http://localhost:5005
```

## Run

From the repo root, in order (two terminals — leave App running):

```powershell
dotnet run --project .\Examples\Server\App\App.csproj
dotnet run --project .\Examples\Clients\CSharpClient\
```

1. Start `App` first (`http://localhost:5005`).
2. Then start `CSharpClient`.



### What the client does

`OrderBookHubDemo.RunAsync`:

1. **Anonymous** — connect without a token → hub auth fails 
2. **User** — JWT with `User`; subscribe to all symbols; on each `update`, unsubscribe that symbol; after all symbols seen, send topic `terminate` with a `Reason` → role auth fails (Administrator only).
3. **Administrator** — JWT with `Administrator`; same subscribe/update/unsubscribe loop, then topic `terminate` with a `Reason` → server soft-stops via `IHostApplicationLifetime.StopApplication()`.



## Authorization (demo)


| Layer                       | Rule                                                                                              |
| --------------------------- | ------------------------------------------------------------------------------------------------- |
| Hub connect                 | `RequireAuthorization()` — any valid JWT                                                          |
| `subscribe` / `unsubscribe` | `.RequireAuthorization(new AuthorizeAttribute { Roles = "User,Administrator" })` on `HandleOnServer` |
| `terminate`                 | `.RequireAuthorization(new AuthorizeAttribute { Roles = "Administrator" })` on `HandleOnServer`   |


Fluent hub auth matches `MapHub` (`string` / `IAuthorizeData` / `AuthorizationPolicy` / policy builder). Topic auth uses `RequireAuthorization()` / named policies / `IAuthorizeData` on `HandleOnServer`. This demo uses default-policy hub auth plus role-based topic auth — see `src/README.md` → Authorization.

Other MapHub-parity options (`RequireCors`, `ConfigureHttpConnection`, `WithMetadata`, `RequireHost`, `WithDisplayName`, `ConfigureEndpoint`) are also available on `AddTopicHub` — see `src/README.md` → Endpoint conventions.

Tokens are minted with the hardcoded key in `AuthService` (demo only).

## Hub: `/orderBook`


| Direction       | Topic         | Payload                        | Notes                                          |
| --------------- | ------------- | ------------------------------ | ---------------------------------------------- |
| Client → server | `subscribe`   | symbol string, e.g. `BTC/USDT` | User or Administrator; deserializer uppercases |
| Client → server | `unsubscribe` | symbol string                  | User or Administrator                          |
| Client → server | `terminate`   | JSON `{"Reason": "..."}`       | Administrator only; soft host shutdown         |
| Server → client | `update`      | JSON `OrderBookUpdate`         | Symbol SignalR group                           |
| Server → client | `alert`       | plain string message           | Sent to caller on connect                      |




### Registration (server)

```csharp
services.AddTopicHub<OrderBookHub>("/orderBook")
    .RequireAuthorization()                                    // default policy (demo)
    // .RequireAuthorization("TradingPolicy")                  // named policy
    // .RequireAuthorization("TradingPolicy", "AdminPolicy")   // multiple named policies
    // .RequireAuthorization(new AuthorizeAttribute { Roles = "User,Administrator" })
    // .RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("User").Build())
    // .RequireAuthorization(b => b.RequireRole("User").RequireClaim("scope", "orders"))
    // .AllowAnonymous()
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

    .HandleOnClient<ConnectionAlert>(cfg =>
        cfg.WithTopic("alert")
            .WithSerializer(alert => alert!.Message))

    .HandleOnClient<OrderBookUpdate>(cfg =>
        cfg.WithTopic("update")
            .WithSerializer(update => JsonSerializer.Serialize(update)));

// and DI services

services.AddHostedService<OrderBookUpdateJob>();
```



### Client JWT

```csharp
// Create a token 
string token = AuthService.CreateToken("trader", AuthService.Roles.User);

HubConnection orderBook = new HubConnectionBuilder()
    .WithUrl("http://localhost:5005/orderBook", o =>
        o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .Build();
```



### Symbols

Defined in `Modules/Orders/Symbols.cs` (price ranges for `OrderBookUpdateJob`):

`BTC/USDT`, `ETH/USDT`, `SOL/USDT`, `XRP/USDT`, `DOGE/USDT`