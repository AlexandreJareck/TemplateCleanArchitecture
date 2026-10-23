using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Template.Application;
using Template.Application.Interfaces;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Persistence;
using Template.Worker;

if (args is ["--health-probe", var probeUrl])
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await client.GetAsync(probeUrl);
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception) { return 1; }
}

var builder = WebApplication.CreateBuilder(args);
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
builder.Services.AddApplicationLayer();
builder.Services.AddSingleton<IAuthenticatedUserService, WorkerAuthenticatedUser>();
builder.Services.AddScoped<IRequestContext, WorkerRequestContext>();
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddProcurementMessagingInfrastructure(builder.Configuration, useBroker: true);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IValidateOptions<OutboxDispatchSettings>, OutboxDispatchSettingsValidator>();
builder.Services.AddOptions<OutboxDispatchSettings>()
    .Bind(builder.Configuration.GetSection(OutboxDispatchSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddHostedService<OutboxDispatcherService>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: !useInMemoryDatabase, seedDefaultData: false);

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (IServiceScopeFactory scopeFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    await using var scope = scopeFactory.CreateAsyncScope();
    if (!await scope.ServiceProvider.CanConnectToApplicationDatabaseAsync(cancellationToken))
        return Results.Json(new { status = "Unhealthy", dependency = "application-database" }, statusCode: 503);
    var endpoint = Template.Infrastructure.Messaging.ServiceRegistration.GetMessageBrokerEndpoint(configuration);
    if (endpoint is null)
        return Results.Ok(new { status = "Degraded", dependency = "message-broker" });
    try
    {
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        return Results.Ok(new { status = "Healthy" });
    }
    catch (Exception exception) when (exception is SocketException or TimeoutException or OperationCanceledException)
    {
        return Results.Ok(new { status = "Degraded", dependency = "message-broker" });
    }
});

await app.RunAsync();
return 0;

public partial class Program;
