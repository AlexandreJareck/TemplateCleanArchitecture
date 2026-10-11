using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning.Builder;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Template.Application;
using Template.Application.Interfaces;
using Template.Application.Settings;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Seeds;
using Template.Infrastructure.Resources;
using Template.Infrastructure.Search;
using Template.WebApi.Infrastructure.Extensions;
using Template.WebApi.Infrastructure.HealthChecks;
using Template.WebApi.Infrastructure.Logging;
using Template.WebApi.Infrastructure.Middlewares;
using Template.WebApi.Infrastructure.Services;
using Template.WebApi.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);
var isTestEnvironment = builder.Environment.IsEnvironment("Test");

Log.Logger = isTestEnvironment
    ? new LoggerConfiguration().WriteTo.Console().CreateLogger()
    : new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();

bool useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

builder.Services.Configure<LocalizationSettings>(builder.Configuration.GetSection(nameof(LocalizationSettings)));

builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddIdentityInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddCachingInfrastructure(builder.Configuration, useInMemoryCache: isTestEnvironment);
builder.Services.AddMessagingInfrastructure(builder.Configuration, useBroker: !isTestEnvironment);
builder.Services.AddSearchInfrastructure(builder.Configuration, useSearchEngine: !isTestEnvironment);
builder.Services.AddResourcesInfrastructure();
builder.Services.AddScoped<IAuthenticatedUserService, AuthenticatedUserService>();

builder.Services.AddSingleton<ApiResultMapper>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationMiddlewareResultHandler>();
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
        context.HttpContext.RequestServices.GetRequiredService<ApiResultMapper>().FromModelState(context);
});
builder.Services.AddSwaggerWithVersioning();
builder.Services.AddAnyCors();

builder.Host.UseSerilog();
builder.Services.AddCustomLocalization();
builder.Services.AddHealthChecksService(builder.Configuration, includeElasticsearch: !isTestEnvironment);
if (!isTestEnvironment)
    builder.Host.ConfigureLog();

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
    if (!useInMemoryDatabase)
        await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

    await DefaultData.SeedAsync(services.GetRequiredService<ApplicationDbContext>());
}

app.ConfigureHealthCheck();

app.UseCustomLocalization();
app.UseAnyCors();
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
}
app.DescribeApiVersions();
app.UseSwaggerWithVersioning();
app.UseMiddleware<ErrorHandlerMiddleware>();
app.MapControllers();

app.Run();

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
public partial class Program
{
}
