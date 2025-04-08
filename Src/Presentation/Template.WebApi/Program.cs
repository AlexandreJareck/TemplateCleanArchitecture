using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Template.Application;
using Template.Application.Interfaces;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Identity.Contexts;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Seeds;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Seeds;
using Template.Infrastructure.Resources;
using Template.WebApi.Consumers;
using Template.WebApi.Elastic;
using Template.WebApi.Infrastructure.Extensions;
using Template.WebApi.Infrastructure.Middlewares;
using Template.WebApi.Infrastructure.Services;
using Template.WebApi.Infrastructure.Settings;
using Template.WebApi.Service;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

bool useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

builder.Services.Configure<LocalizationSettings>(builder.Configuration.GetSection(nameof(LocalizationSettings)));

builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddIdentityInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddResourcesInfrastructure();
builder.Services.AddScoped<IAuthenticatedUserService, AuthenticatedUserService>();

builder.Services.Configure<ElasticSearchSettings>(builder.Configuration.GetSection("ElasticSearchSettings"));
builder.Services.AddSingleton<IElasticService, ElasticService>();

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddSwaggerWithVersioning();
builder.Services.AddAnyCors();

builder.Host.UseSerilog();
builder.Services.AddCustomLocalization();
builder.Services.AddHealthChecksService();
builder.Host.ConfigureLog();

builder.Services.AddSingleton<RabbitMqService>();
builder.Services.AddHostedService<OrderConsumer>(); 

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    if (!useInMemoryDatabase)
    {
        await services.GetRequiredService<IdentityContext>().Database.MigrateAsync();
        await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    await DefaultRoles.SeedAsync(services.GetRequiredService<RoleManager<ApplicationRole>>());
    await DefaultBasicUser.SeedAsync(services.GetRequiredService<UserManager<ApplicationUser>>());
    await DefaultData.SeedAsync(services.GetRequiredService<ApplicationDbContext>());
}

app.ConfigureHealthCheck();

app.UseCustomLocalization();
app.UseAnyCors();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwaggerWithVersioning();
app.UseMiddleware<ErrorHandlerMiddleware>();
app.MapControllers();

app.Run();

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
public partial class Program
{
}
