using Microsoft.Extensions.DependencyInjection;
using Template.Application.Interfaces;
using Template.Infrastructure.Resources.Services;

namespace Template.Infrastructure.Resources
{
    public static class ServiceRegistration
    {
        public static IServiceCollection AddResourcesInfrastructure(this IServiceCollection services)
        {            
            services.AddSingleton<ITranslator, Translator>();

            return services;
        }
    }
}
