using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Template.Application;
using Template.Application.Interfaces;
using Template.Infrastructure.Files;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Reporting;
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
builder.Services.AddSingleton<Template.Application.Interfaces.Access.IPermissionReader, WorkerPermissions>();
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddProcurementMessagingInfrastructure(builder.Configuration, useBroker: true);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IValidateOptions<OutboxDispatchSettings>, OutboxDispatchSettingsValidator>();
builder.Services.AddOptions<OutboxDispatchSettings>()
    .Bind(builder.Configuration.GetSection(OutboxDispatchSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddHostedService<OutboxDispatcherService>();
// Phase 30: report execution (PDF/XLSX rendering, private blob storage) and the retention pass.
builder.Services.AddFilesInfrastructure(builder.Configuration, useInMemoryStorage: useInMemoryDatabase);
builder.Services.AddReportingInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IValidateOptions<ReportWorkerSettings>, ReportWorkerSettingsValidator>();
builder.Services.AddOptions<ReportWorkerSettings>()
    .Bind(builder.Configuration.GetSection(ReportWorkerSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddHostedService<ReportWorkerService>();
// An in-flight report attempt hands its job back on shutdown; allow it to finish that bounded step.
builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(45));

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
    }
    catch (Exception exception) when (exception is SocketException or TimeoutException or OperationCanceledException)
    {
        return Results.Ok(new { status = "Degraded", dependency = "message-broker" });
    }

    // Blob storage only affects report outputs; an unreachable store degrades readiness, never fails it.
    if (configuration.GetSection(FileStorageSettings.SectionName).GetValue<bool>(nameof(FileStorageSettings.Enabled)))
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var probe = scope.ServiceProvider.GetRequiredService<IFileStorageProbe>();
        if (!await probe.IsAvailableAsync(timeout.Token))
            return Results.Ok(new { status = "Degraded", dependency = "file-storage" });
    }

    return Results.Ok(new { status = "Healthy" });
});

await app.RunAsync();
return 0;

public partial class Program;
