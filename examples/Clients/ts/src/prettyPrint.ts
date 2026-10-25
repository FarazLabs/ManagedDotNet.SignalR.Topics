type Color = "magenta" | "cyan" | "green" | "yellow" | "red" | "reset";

const ansi: Record<Color, string> = {
  magenta: "\u001b[35m",
  cyan: "\u001b[36m",
  green: "\u001b[32m",
  yellow: "\u001b[33m",
  red: "\u001b[31m",
  reset: "\u001b[0m",
};

function line(color: Color, message: string): void {
  console.log(`${ansi[color]}${message}${ansi.reset}`);
}

function pair(topic: string, body: string): string {
  return `[${JSON.stringify(topic)}, ${JSON.stringify(body)}]`;
}

/** Console helpers matching examples/Shared/Utilities/PrettyPrint.cs */
export const PrettyPrint = {
  banner(title: string): void {
    line("magenta", "");
    line("magenta", "════════════════════════════");
    line("magenta", `═══ ${title} ═══`);
    line("magenta", "════════════════════════════");
  },

  outbound(topic: string, body: string): void {
    line("cyan", `← ${pair(topic, body)}`);
  },

  inbound(topic: string, body: string): void {
    line("green", `→ ${pair(topic, body)}`);
  },

  info(message: string): void {
    line("yellow", `** ${message} **`);
  },

  error(message: string): void {
    line("red", `! ${message}`);
  },
};
