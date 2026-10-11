using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using Template.Application.Behaviors;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Services.Product;
using Template.Application.Services.Time;

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

        return services;
    }
}
