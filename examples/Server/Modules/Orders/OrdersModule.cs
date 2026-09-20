using System.Text.Json;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Application.BackgroundJobs;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Application.HubCommandHandlers;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Infrastructure;
using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders.Models;
using ManagedDotNet.SignalR.Topics.Examples.Shared;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Models;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ManagedDotNet.SignalR.Topics.Configuration;
using static ManagedDotNet.SignalR.Topics.Examples.Shared.Services.AuthService;

namespace ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders;

public class OrdersModule : IModule
{
    public void Register(IServiceCollection services)
    {
        services.AddTopicHub<OrderBookHub>("/orderBook")
            .RequireAuthorization()

            /****** INBOUND MESSAGES ******/
            .HandleOnServer<SubscribeToSymbolCommand>(cfg =>
                cfg.WithTopic("subscribe")
                    .RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.User},{Roles.Administrator}" })
                    .WithDeserializer(str =>
                    {
                        PrettyPrint.Inbound("subscribe", str);
                        return new SubscribeToSymbolCommand
                        {
                            Symbol = str.Trim().ToUpper()
                        };
                    })
                    .WithHandler<SubscribeToSymbolHubCommandHandler>())

            .HandleOnServer<UnsubscribeFromSymbolCommand>(cfg =>
                cfg.WithTopic("unsubscribe")
                    .RequireAuthorization(new AuthorizeAttribute { Roles = $"{Roles.User},{Roles.Administrator}" })
                    .WithDeserializer(str =>
                    {
                        PrettyPrint.Inbound("unsubscribe", str);
                        return new UnsubscribeFromSymbolCommand
                        {
                            Symbol = str.Trim().ToUpper()
                        };
                    })
                    .WithHandler<UnsubscribeFromSymbolHubCommandHandler>())

            .HandleOnServer<TerminateCommand>(cfg =>
                cfg.WithTopic("terminate")
                    .RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Administrator })
                    .WithDeserializer(str =>
                    {
                        PrettyPrint.Inbound("terminate", str);
                        return JsonSerializer.Deserialize<TerminateCommand>(str);
                    })
                    .WithHandler<TerminateHubCommandHandler>())

            /****** OUTBOUND MESSAGES ******/
            .HandleOnClient<ConnectionAlert>(cfg =>
                cfg.WithTopic("alert")
                    .WithSerializer(alert =>
                    {
                        string body = alert!.Message;
                        PrettyPrint.Outbound("alert", body);
                        return body;
                    }))

            .HandleOnClient<OrderBookUpdate>(cfg =>
                cfg.WithTopic("update")
                    .WithSerializer(update =>
                    {
                        string body = JsonSerializer.Serialize(update);
                        PrettyPrint.Outbound("update", body);
                        return body;
                    }));

        services.AddHostedService<OrderBookUpdateJob>();
    }
}
