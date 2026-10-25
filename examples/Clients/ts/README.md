# TypeScript OrderBook client

Same flow as [`../CSharpClient`](../CSharpClient): anonymous → User → Administrator against `http://localhost:5005/orderBook`, using [`manageddotnet-topichub-client`](https://www.npmjs.com/package/manageddotnet-topichub-client).

## Prerequisites

Start the demo server from the Topics repo root:

```powershell
dotnet run --project .\examples\Server\App\App.csproj --no-restore
```

## Run

```powershell
cd <Topics>\examples\Clients\ts
pnpm install
pnpm start
```

Uses Node `--experimental-strip-types`. Outbound message classes must **not** use TypeScript constructor parameter properties (`public symbol: string` in the ctor params) — assign fields in the constructor body (see [`src/models.ts`](src/models.ts)).

## Registration (mirrors server Orders module)

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

const baseUrl: string = "http://localhost:5005";
const token: string | null = /* JWT or null */;

const hub = createTopicHubClient({
  getUrl: () => `${baseUrl}/orderBook`,
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

## What it does

Matches `OrderBookHubDemo.RunAsync`:

1. **Anonymous** — connect without JWT → hub auth fails  
2. **User** — JWT role `User`; subscribe all symbols; on each `update`, unsubscribe; then `terminate` → role auth fails  
3. **Administrator** — same loop, then `terminate` → server soft-stops  

Wire topics: `subscribe` / `unsubscribe` (plain symbol string), `terminate` (JSON `{ Reason }`). Inbound: `alert` (plain text), `update` (JSON `OrderBookUpdate`).
