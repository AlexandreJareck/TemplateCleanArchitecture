using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Template.Application.Consumers;
using Template.Application.Services.Elastic;
using Template.Application.Services.Product;
using Template.Application.Services.RabbitMq;

namespace Template.Application;
public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceRegistration).Assembly));

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IRabbitMqService, RabbitMqService>();
        services.AddSingleton<IElasticService, ElasticService>();
        services.AddHostedService<OrderConsumerDirect>();
        services.AddHostedService<OrderConsumerFanoutA>();
        services.AddHostedService<OrderConsumerFanoutB>();
        services.AddHostedService<OrderConsumerTopic>();

        return services;
    }
}
