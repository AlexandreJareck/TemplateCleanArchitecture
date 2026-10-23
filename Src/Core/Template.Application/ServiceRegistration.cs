using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using Template.Application.Behaviors;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Services.Access;
using Template.Application.Services.Product;
using Template.Application.Services.Time;
using Template.Application.Settings;
using Microsoft.Extensions.Options;

namespace Template.Application;
public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ServiceRegistration).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderPlacedEventHandler, OrderPlacedEventHandler>();

        services.AddScoped<SecurityAuditRecorder>();
        services.AddScoped<Services.MasterData.MasterDataWriter>();
        services.AddScoped<Services.Integrations.PtaxRateResolver>();
        services.AddScoped<Services.Procurement.ProcurementWriter>();
        services.AddScoped<IProcurementIntegrationEventHandler, Services.Procurement.ProcurementIntegrationEventHandler>();
        services.AddScoped<Features.Requisitions.RequisitionAccess>();
        services.AddScoped<ISessionRevocationService, SessionRevocationService>();
        services.AddSingleton<IValidateOptions<SessionSettings>, SessionSettingsValidator>();
        // The composition root binds SessionSettings from configuration and validates it on start.

        return services;
    }
}
