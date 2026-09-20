using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Infrastructure;
using ManagedDotNet.SignalR.Topics.Core;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Models;

namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Application.BackgroundJobs;

public class OrderBookUpdateJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderBookUpdateJob> _logger;

    public OrderBookUpdateJob
    (
        IServiceProvider serviceProvider,
        ILogger<OrderBookUpdateJob> logger
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrettyPrint.Info("OrderBookUpdateJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(GetInterval(), stoppingToken);

            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();

                ITopicHubContext<OrderBookHub> hubContext =
                    scope.ServiceProvider.GetRequiredService<ITopicHubContext<OrderBookHub>>();

                (string symbol, decimal price) = GetNextOrderBookUpdate();

                // Wire log happens in HandleOnClient WithSerializer
                await hubContext.Clients.Group(symbol).Handle(new OrderBookUpdate
                {
                    Symbol = symbol,
                    Price = price
                }, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PrettyPrint.Error($"OrderBookUpdateJob publish failed; continuing. {ex.Message}");
            }
        }
    }

    private static TimeSpan GetInterval() => TimeSpan.FromSeconds(Random.Shared.Next(1, 2));

    private static (string Symbol, decimal Price) GetNextOrderBookUpdate()
    {
        string[] symbols = Symbols.GetAll();

        // pick a random symbol from the list
        string symbol = symbols[Random.Shared.Next(symbols.Length)];

        // get the range for the symbol
        Range range = Symbols.GetRange(symbol);

        // Generate a random decimal price within the given range for the symbol.
        // Random.Shared.NextDouble() returns a value between 0.0 and 1.0.
        // Multiplying this value by (range.Max - range.Min) produces a random offset within the range.
        // Adding it to range.Min shifts the value into the range [Min, Max].
        decimal price = range.Min + (decimal)Random.Shared.NextDouble() * (range.Max - range.Min);

        return (symbol, decimal.Round(price, 2));
    }
}
