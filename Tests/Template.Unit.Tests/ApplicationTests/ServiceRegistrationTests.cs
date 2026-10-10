using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application;
using Template.Application.Behaviors;
using Template.Application.Interfaces;
using Template.Application.Services.Time;

namespace Template.Unit.Tests.ApplicationTests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddApplicationLayer_RegistersSystemClockAsSingleton()
    {
        var services = new ServiceCollection().AddApplicationLayer(registerIntegrationHostedServices: false);

        var descriptor = services.Single(service => service.ServiceType == typeof(IClock));
        descriptor.ImplementationType.ShouldBe(typeof(SystemClock));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddApplicationLayer_RegistersValidationBehaviorAsOpenPipelineBehavior()
    {
        var services = new ServiceCollection().AddApplicationLayer(registerIntegrationHostedServices: false);

        services.ShouldContain(service =>
            service.ServiceType == typeof(IPipelineBehavior<,>) &&
            service.ImplementationType == typeof(ValidationBehavior<,>));
    }
}
