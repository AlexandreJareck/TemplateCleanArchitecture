using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application;
using Template.Application.Behaviors;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Search;
using Template.Application.Services.Time;

namespace Template.Unit.Tests.ApplicationTests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddApplicationLayer_RegistersSystemClockAsSingleton()
    {
        var services = new ServiceCollection().AddApplicationLayer();

        var descriptor = services.Single(service => service.ServiceType == typeof(IClock));
        descriptor.ImplementationType.ShouldBe(typeof(SystemClock));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddApplicationLayer_RegistersValidationBehaviorAsOpenPipelineBehavior()
    {
        var services = new ServiceCollection().AddApplicationLayer();

        services.ShouldContain(service =>
            service.ServiceType == typeof(IPipelineBehavior<,>) &&
            service.ImplementationType == typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void AddApplicationLayer_RegistersTheOrderHandler_AndNoHostedService()
    {
        var services = new ServiceCollection().AddApplicationLayer();

        var handler = services.Single(service => service.ServiceType == typeof(IOrderPlacedEventHandler));
        handler.ImplementationType.ShouldBe(typeof(OrderPlacedEventHandler));
        handler.Lifetime.ShouldBe(ServiceLifetime.Scoped);
        services.ShouldNotContain(service => service.ServiceType.FullName == "Microsoft.Extensions.Hosting.IHostedService");
        services.ShouldNotContain(service => service.ServiceType == typeof(IOrderEventPublisher));
    }

    [Fact]
    public void AddApplicationLayer_LeavesThePersonSearchIndexToInfrastructure()
    {
        var services = new ServiceCollection().AddApplicationLayer();

        services.ShouldNotContain(service => service.ServiceType == typeof(IPersonSearchIndex));
        services.ShouldNotContain(service => service.ServiceType.Namespace != null && service.ServiceType.Namespace.StartsWith("Elastic", StringComparison.Ordinal));
    }
}
