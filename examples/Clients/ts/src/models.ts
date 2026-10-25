export class SubscribeCommand {
  public readonly symbol: string;

  public constructor
  (
    symbol: string,
  )
  {
    this.symbol = symbol;
  }
}

export class UnsubscribeCommand {
  public readonly symbol: string;

  public constructor
  (
    symbol: string,
  )
  {
    this.symbol = symbol;
  }
}

export class TerminateCommand {
  public readonly Reason: string;

  public constructor
  (
    Reason: string,
  )
  {
    this.Reason = Reason;
  }
}

export type OrderBookUpdate = {
  Symbol: string;
  Price: number;
};
