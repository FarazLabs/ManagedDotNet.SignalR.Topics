# ManagedDotNet.SignalR.Topics Demo

Modular monolith server plus C# and TypeScript clients for **ManagedDotNet.SignalR.Topics**.

## Layout

```text
Examples/
  Shared/                   # IModule, AuthService (JWT mint/validate), PrettyPrint, OrderBookUpdate
  Clients/CSharpClient/     # OrderBookHubDemo (raw SignalR.Client + JWT roles + topics)
  Clients/ts/               # same demo via manageddotnet-topichub-client (constructor + withTopic)
  Server/
    Modules/Orders/         # OrderBookHub, handlers, OrderBookUpdateJob, Symbols
    App/                    # hosts Orders module on http://localhost:5005
```

## Run

From the Topics repo root, in order (two terminals — leave App running):

```powershell
dotnet run --project .\examples\Server\App\App.csproj --no-restore
dotnet run --project .\examples\Clients\CSharpClient\ --no-restore
```

TypeScript client (same flow; uses published `manageddotnet-topichub-client`):

```powershell
# terminal 1 — server
dotnet run --project .\examples\Server\App\App.csproj --no-restore

# terminal 2 — example
cd .\examples\Clients\ts
pnpm install
pnpm start
```

1. Start `App` first (`http://localhost:5005`).
2. Then start `CSharpClient` **or** `Clients/ts` (see [`Clients/ts/README.md`](Clients/ts/README.md)).



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


Fluent hub auth matches `MapHub` (`string` / `IAuthorizeData` / `AuthorizationPolicy` / policy builder). Topic auth uses `RequireAuthorization()` / named policies / `IAuthorizeData` on `HandleOnServer`. Handler-class authorize attributes are not read. This demo uses default-policy hub auth plus role-based topic auth — see `src/README.md` → Authorization.

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



### Client JWT (C#)

```csharp
// Create a token 
string token = AuthService.CreateToken("trader", AuthService.Roles.User);

HubConnection orderBook = new HubConnectionBuilder()
    .WithUrl("http://localhost:5005/orderBook", o =>
        o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .Build();
```

### Registration (TypeScript client)

Uses [`manageddotnet-topichub-client`](https://www.npmjs.com/package/manageddotnet-topichub-client). Fluent names match the server; transforms are inverted. Outbound messages are **classes**; `hub.handle(instance)` resolves topic via `instance.constructor`.

```ts
import { createTopicHubClient } from "manageddotnet-topichub-client";

class SubscribeCommand {
  public readonly symbol: string;

  public constructor
  (
    symbol: string,
  )
  {
    this.symbol = symbol;
  }
}

class UnsubscribeCommand {
  public readonly symbol: string;

  public constructor
  (
    symbol: string,
  )
  {
    this.symbol = symbol;
  }
}

class TerminateCommand {
  public readonly Reason: string;

  public constructor
  (
    Reason: string,
  )
  {
    this.Reason = Reason;
  }
}

type OrderBookUpdate = {
  Symbol: string;
  Price: number;
};

const token: string | null = /* JWT or null */;

const hub = createTopicHubClient({
  getUrl: () => "http://localhost:5005/orderBook",
  getAccessToken: () => token,
})
  .handleOnServer(SubscribeCommand, (cfg) =>
    cfg
      .withTopic("subscribe")
      .withSerializer((cmd) => cmd.symbol))
  .handleOnServer(UnsubscribeCommand, (cfg) =>
    cfg
      .withTopic("unsubscribe")
      .withSerializer((cmd) => cmd.symbol))
  .handleOnServer(TerminateCommand, (cfg) =>
    cfg
      .withTopic("terminate"))
  .handleOnClient<string>((cfg) =>
    cfg
      .withTopic("alert")
      .withDeserializer((payload) => payload)
      .withHandler((message, context) => {
        console.log(context.topic, message);
      }))
  .handleOnClient<OrderBookUpdate>((cfg) =>
    cfg
      .withTopic("update")
      .withHandler(async (update, context) => {
        console.log(context.topic, update.Symbol, update.Price);
        await hub.handle(new UnsubscribeCommand(update.Symbol));
      }))
  .build();

await hub.connect();
await hub.handle(new SubscribeCommand("BTC/USDT"));
await hub.handle(new TerminateCommand("demo complete"));
```

See [`Clients/ts/README.md`](Clients/ts/README.md) for run steps.

### Symbols

Defined in `Modules/Orders/Symbols.cs` (price ranges for `OrderBookUpdateJob`):

`BTC/USDT`, `ETH/USDT`, `SOL/USDT`, `XRP/USDT`, `DOGE/USDT`