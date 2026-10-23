using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning.Builder;
using Serilog;
using Template.Application;
using Template.Application.Interfaces;
using Template.Application.Logging;
using Template.Application.Settings;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Files;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Integrations;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Resources;
using Template.Infrastructure.Search;
using Template.WebApi.Infrastructure.Extensions;
using Template.WebApi.Infrastructure.HealthChecks;
using Template.WebApi.Infrastructure.Logging;
using Template.WebApi.Infrastructure.Middlewares;
using Template.WebApi.Infrastructure.Services;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);
var isTestEnvironment = builder.Environment.IsEnvironment("Test");

Log.Logger = isTestEnvironment
    ? new LoggerConfiguration().Enrich.With<SensitiveDataRedactionEnricher>().WriteTo.Console().CreateLogger()
    : new LoggerConfiguration().Enrich.With<SensitiveDataRedactionEnricher>().ReadFrom.Configuration(builder.Configuration).CreateLogger();

bool useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddIdentityInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddCachingInfrastructure(builder.Configuration, useInMemoryCache: isTestEnvironment);
builder.Services.AddMessagingInfrastructure(builder.Configuration, useBroker: !isTestEnvironment);
builder.Services.AddSearchInfrastructure(builder.Configuration, useSearchEngine: !isTestEnvironment);
builder.Services.AddIntegrationsInfrastructure(builder.Configuration, useExternalProviders: !isTestEnvironment);
builder.Services.AddFilesInfrastructure(builder.Configuration, useInMemoryStorage: isTestEnvironment);
builder.Services.AddResourcesInfrastructure();
builder.Services.AddScoped<IAuthenticatedUserService, AuthenticatedUserService>();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddOptions<SessionSettings>()
    .Bind(builder.Configuration.GetSection(SessionSettings.SectionName))
    .ValidateOnStart();

// Phase 24: permission policies (SEC-008), authentication rate limiting (SEC-007) and sampled boundary audit.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<AuthenticationRateLimiter>();
builder.Services.AddSingleton<SecurityEventSampler>();
builder.Services.AddScoped<BoundarySecurityAudit>();

builder.Services.AddSingleton<ApiResultMapper>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationMiddlewareResultHandler>();
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
        context.HttpContext.RequestServices.GetRequiredService<ApiResultMapper>().FromModelState(context);
});
builder.Services.AddSwaggerWithVersioning();
builder.Services.AddConfiguredCors(builder.Configuration);

builder.Host.ConfigureLog(isTestEnvironment);
builder.Services.AddCustomLocalization(builder.Configuration);
builder.Services.AddHealthChecksService(builder.Configuration, includeElasticsearch: !isTestEnvironment);
builder.Services.AddDependencyHealthChecks(builder.Configuration, includeMessageBroker: !isTestEnvironment, includeFileStorage: !isTestEnvironment);

builder.Services.AddRouting(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    await services.InitializeIdentityDatabaseAsync(applyMigrations: !useInMemoryDatabase);
    await services.InitializeApplicationDatabaseAsync(applyMigrations: !useInMemoryDatabase);
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.ConfigureHealthCheck();

app.UseCustomLocalization();
app.UseConfiguredCors();
// Before routing and authentication, so a failure anywhere in the pipeline maps to Problem Details and is logged once.
app.UseMiddleware<ErrorHandlerMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
if (isTestEnvironment)
{
    app.MapGet("/_tests/forbidden", () => Results.NoContent())
        .RequireAuthorization(policy => policy.RequireRole("role-not-assigned-to-test-user"))
        .ExcludeFromDescription();
    app.MapGet("/_tests/admin", () => Results.NoContent())
        .RequireAuthorization(policy => policy.RequireRole("Admin"))
        .ExcludeFromDescription();
    app.MapGet(
            "/_tests/exception",
            () => Task.FromException<IResult>(new InvalidOperationException("internal-sensitive-marker")))
        .ExcludeFromDescription();
    app.MapGet(
            "/_tests/canceled",
            async (HttpContext context) =>
            {
                context.Abort();
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
                return Results.NoContent();
            })
        .ExcludeFromDescription();
}
app.DescribeApiVersions();
app.UseSwaggerWithVersioning();
app.MapControllers();

var lifecycleLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Template.WebApi.Lifecycle");
app.Lifetime.ApplicationStarted.Register(() => lifecycleLogger.LogInformation(
    LogEvents.ApplicationStarted, "Application started in {EnvironmentName}", app.Environment.EnvironmentName));
app.Lifetime.ApplicationStopping.Register(() => lifecycleLogger.LogInformation(
    LogEvents.ApplicationStopping, "Application stopping"));
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);

try
{
    app.Run();
}
catch (Exception error) when (error is not HostAbortedException)
{
    lifecycleLogger.LogCritical(
        LogEvents.ApplicationStartupFailed, error, "Application terminated unexpectedly ({FailureCategory})", error.GetType().Name);
    throw;
}
public partial class Program
{
}
