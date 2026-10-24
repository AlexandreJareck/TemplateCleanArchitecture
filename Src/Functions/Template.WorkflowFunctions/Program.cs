using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Template.Application;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;
using Template.WorkflowFunctions.Hosting;

// Phase 31 (ADR-0003): third composition root. It composes Application, Persistence (business truth, Outbox) and the
// read-only Identity approver directory; it never migrates or seeds (WebApi and Worker own that) and holds no broker,
// storage-of-files or HTTP-public dependency. Durable state lives in the Azure Storage task hub (Azurite locally).
var builder = FunctionsApplication.CreateBuilder(args);

var configuration = builder.Configuration;
var useInMemoryDatabase = configuration.GetValue<bool>("UseInMemoryDatabase");
builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase);
builder.Services.AddIdentityDirectoryInfrastructure(configuration, useInMemoryDatabase);
builder.Services.AddSingleton<IAuthenticatedUserService, WorkflowSystemUser>();
builder.Services.AddSingleton<IPermissionReader, WorkflowSystemPermissions>();
builder.Services.AddScoped<WorkflowRequestContext>();
builder.Services.AddScoped<IRequestContext>(provider => provider.GetRequiredService<WorkflowRequestContext>());
builder.Services.AddSingleton<IValidateOptions<WorkflowHostSettings>, WorkflowHostSettingsValidator>();
builder.Services.AddOptions<WorkflowHostSettings>().Bind(configuration.GetSection(WorkflowHostSettings.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<InternalApiAuthorization>();

builder.Build().Run();
