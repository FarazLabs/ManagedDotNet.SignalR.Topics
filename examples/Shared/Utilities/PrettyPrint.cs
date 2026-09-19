using System.Text.Encodings.Web;
using System.Text.Json;

namespace ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;

public static class PrettyPrint
{
    private static readonly object Gate = new object();

    // Default STJ encoder turns " into \u0022; fine for HTML, ugly in a console demo.
    private static readonly JsonSerializerOptions LogJson = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Banner(string title)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine();
            Console.WriteLine($"════════════════════════════");
            Console.WriteLine($"═══ {title} ═══");
            Console.WriteLine($"════════════════════════════");
            Console.ResetColor();
        }
    }

    public static void Outbound(string topic, string body)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"← {Pair(topic, body)}");
            Console.ResetColor();
        }
    }

    public static void Inbound(string topic, string body)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"→ {Pair(topic, body)}");
            Console.ResetColor();
        }
    }

    public static void Info(string message)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"** {message} **");
            Console.ResetColor();
        }
    }

    public static void Error(string message)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"! {message}");
            Console.ResetColor();
        }
    }

    private static string Pair(string topic, string body) =>
        $"[{JsonSerializer.Serialize(topic, LogJson)}, {JsonSerializer.Serialize(body, LogJson)}]";
}
