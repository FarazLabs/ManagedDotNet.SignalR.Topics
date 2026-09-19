using ManagedDotNet.SignalR.Topics.Examples.Server.Modules.Orders;
using ManagedDotNet.SignalR.Topics.Examples.Shared;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Services;
using ManagedDotNet.SignalR.Topics.Examples.Shared.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ManagedDotNet.SignalR.Topics.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = AuthService.TokenValidationParameters;
        
        // SignalR WebSockets send the JWT as ?access_token=
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                string? accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/orderBook"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();



foreach (IModule module in new IModule[] { new OrdersModule() })
{
    module.Register(builder.Services);
    PrettyPrint.Info($"Module registered: {module.GetType().Name}");
}

builder.Services.AddSignalR();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapTopicHubs();
});

app.Logger.LogInformation("App listening on http://localhost:5005 (hub: /orderBook, JWT required)");

app.Run();
