using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using Template.Application.Behaviors;
using Template.Application.Consumers;
using Template.Application.Interfaces;
using Template.Application.Services.Elastic;
using Template.Application.Services.Product;
using Template.Application.Services.RabbitMq;
using Template.Application.Services.Time;

namespace Template.Application;
public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services, bool registerIntegrationHostedServices = true)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ServiceRegistration).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IRabbitMqService, RabbitMqService>();
        services.AddSingleton<IElasticService, ElasticService>();

        if (registerIntegrationHostedServices)
        {
            services.AddHostedService<OrderConsumerDirect>();
            services.AddHostedService<OrderConsumerFanoutA>();
            services.AddHostedService<OrderConsumerFanoutB>();
            services.AddHostedService<OrderConsumerTopic>();
        }

        return services;
    }
}
