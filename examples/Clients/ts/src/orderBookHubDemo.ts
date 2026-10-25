import {
  createTopicHubClient,
  type ITopicHubClient,
} from "manageddotnet-topichub-client";
import { createToken, Roles } from "./authService.ts";
import {
  SubscribeCommand,
  TerminateCommand,
  UnsubscribeCommand,
  type OrderBookUpdate,
} from "./models.ts";
import { PrettyPrint } from "./prettyPrint.ts";

const Symbols: readonly string[] = ["BTC/USDT", "ETH/USDT", "SOL/USDT", "XRP/USDT", "DOGE/USDT"];

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Port of examples/Clients/CSharpClient/OrderBookHubDemo.cs using manageddotnet-topichub-client.
 */
export async function runAsync(baseUrl: string): Promise<void> {
  PrettyPrint.banner("A) anonymous connect");
  await demoAsync(baseUrl, "anonymous", null);
  await delay(2000);

  PrettyPrint.banner("B) user connect (trader)");
  const userToken: string = createToken("trader", Roles.User);
  await demoAsync(baseUrl, "user", userToken);
  await delay(2000);

  PrettyPrint.banner("C) administrator connect");
  const adminToken: string = createToken("admin", Roles.Administrator);
  await demoAsync(baseUrl, "admin", adminToken);
  await delay(2000);
}

async function demoAsync(
  baseUrl: string,
  role: string,
  token: string | null,
): Promise<void> {
  const updatedSymbols: Set<string> = new Set<string>();
  let completed: (() => void) | undefined;
  const done: Promise<void> = new Promise<void>((resolve) => {
    completed = resolve;
  });

  let currentToken: string | null = token;

  const hub: ITopicHubClient = createTopicHubClient({
    getUrl: (): string => `${baseUrl}/orderBook`,
    getAccessToken: (): string | null => currentToken,
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
          PrettyPrint.inbound(context.topic, message);
          PrettyPrint.info(`alert received: ${message}`);
        }))
    .handleOnClient<OrderBookUpdate>((cfg) =>
      cfg
        .withTopic("update")
        .withHandler(async (update, context) => {
          PrettyPrint.inbound(context.topic, JSON.stringify(update));
          updatedSymbols.add(update.Symbol);
          await invoke(hub, new UnsubscribeCommand(update.Symbol));
          if (updatedSymbols.size === Symbols.length) {
            PrettyPrint.info(`${role}: all ${Symbols.length} symbols received`);
            completed?.();
          }
        }))
    .build();

  try {
    PrettyPrint.info(`${role}: connecting?`);
    await hub.connect();
    PrettyPrint.info(`${role}: connected`);

    for (const symbol of Symbols) {
      await invoke(hub, new SubscribeCommand(symbol));
    }

    await done;

    PrettyPrint.info(`${role}: invoking terminate`);
    await invoke(hub, new TerminateCommand(`${role} demo complete`));
    PrettyPrint.info(`${role}: terminate accepted`);
  } catch (error: unknown) {
    const message: string = error instanceof Error ? error.message : String(error);
    PrettyPrint.error(`${role}: ${message}`);
  } finally {
    await hub.disconnect();
    PrettyPrint.info(`${role}: disconnected`);
  }
}

async function invoke(hub: ITopicHubClient, message: object): Promise<void> {
  const topicHint: string = message.constructor.name;
  PrettyPrint.outbound(topicHint, JSON.stringify(message));
  await hub.handle(message);
}
