using System.Text.Json;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Models;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Services;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using Microsoft.AspNetCore.SignalR.Client;

namespace ManagedDotNet.SignalR.Topics.Examples.Clients.CSharpClient;

public static class OrderBookHubDemo
{
    private static readonly string[] Symbols = ["BTC/USDT", "ETH/USDT", "SOL/USDT", "XRP/USDT", "DOGE/USDT"];

    public static async Task RunAsync(string baseUrl)
    {
        PrettyPrint.Banner("A) anonymous connect");
        await DemoAsync(baseUrl, role: "anonymous", token: null);
        await Task.Delay(2000);

        PrettyPrint.Banner("B) user connect (trader)");
        await DemoAsync(baseUrl, role: "user", token: AuthService.CreateToken("trader", AuthService.Roles.User));
        await Task.Delay(2000);

        PrettyPrint.Banner("C) administrator connect");
        await DemoAsync(baseUrl, role: "admin", token: AuthService.CreateToken("admin", AuthService.Roles.Administrator));
        await Task.Delay(2000);
    }



    
    private static async Task DemoAsync(string baseUrl, string role, string? token)
    {
        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/orderBook", options =>
            {
                if (token is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        try
        {
            HashSet<string> updatedSymbols = new HashSet<string>();

            // completion signal
            TaskCompletionSource completed = new TaskCompletionSource();

            // handle messages from the server
            connection.On<string, string>("Handle", async (string topic, string payload) =>
            {
                PrettyPrint.Inbound(topic, payload);

                switch (topic)
                {
                    case "alert":
                        PrettyPrint.Info($"alert received: {payload}");
                        break;

                    case "update":
                        OrderBookUpdate? update = JsonSerializer.Deserialize<OrderBookUpdate>(payload);
                        updatedSymbols.Add(update!.Symbol);
                        await InvokeAsync(connection, "unsubscribe", update.Symbol);
                        if (updatedSymbols.Count == Symbols.Length)
                        {
                            PrettyPrint.Info($"{role}: all {Symbols.Length} symbols received");
                            completed.TrySetResult();
                        }
                        break;

                    default:
                        PrettyPrint.Error($"unexpected topic: {topic}");
                        break;
                }
            });

            PrettyPrint.Info($"{role}: connecting…");
            await connection.StartAsync();
            PrettyPrint.Info($"{role}: connected ({connection.ConnectionId})");

            foreach (string symbol in Symbols)
                await InvokeAsync(connection, "subscribe", symbol);

            await completed.Task;


            PrettyPrint.Info($"{role}: invoking terminate");
            await InvokeAsync(connection, "terminate", JsonSerializer.Serialize(new TerminateCommand{Reason = $"{role} demo complete"}));
            PrettyPrint.Info($"{role}: terminate accepted");
        }
        catch (Exception ex)
        {
            PrettyPrint.Error($"{role}: {ex.Message}");
        }
        finally
        {
            await connection.DisposeAsync();
            PrettyPrint.Info($"{role}: disconnected");
        }
    }

    private static async Task InvokeAsync(HubConnection connection, string topic, string payload)
    {
        PrettyPrint.Outbound(topic, payload);
        await connection.InvokeAsync("Handle", topic, payload);
    }
}
