using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Services.Reporting;
using Template.Infrastructure.Persistence;

namespace Template.Worker.Tests;

/// <summary>
/// The Worker composition root resolves every Phase 30 use case and adapter (report attempts, retention pass, Chromium
/// renderer, spreadsheet writer, storage) with the system actor. Runs on the in-memory database without a broker or
/// storage; hosted services are removed so nothing connects. Found by the Phase 30 Compose gate: the Worker lacked a
/// permission reader, so every report attempt failed to resolve.
/// </summary>
public sealed class WorkerCompositionTests : IClassFixture<WorkerCompositionTests.WorkerFactory>
{
    private readonly WorkerFactory factory;

    public WorkerCompositionTests(WorkerFactory factory) => this.factory = factory;

    [Fact]
    public async Task ReportUseCases_ResolveAndRun_WithTheSystemActor()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<ReportJobProcessor>();
        (await processor.ProcessNextAsync("worker-composition-test", CancellationToken.None)).Outcome.ShouldBe(ReportProcessingOutcome.Idle);
        var maintenance = scope.ServiceProvider.GetRequiredService<ReportMaintenance>();
        (await maintenance.RunAsync(10, CancellationToken.None)).ShouldBe(new ReportMaintenanceResult(0, 0, 0));
        scope.ServiceProvider.GetRequiredService<IPdfRenderer>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ISpreadsheetWriter>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IFileStorage>().ShouldNotBeNull();
        scope.ServiceProvider.GetServices<IReportBuilder>().Select(builder => builder.Type).Distinct().Count().ShouldBe(5);
    }

    /// <summary>Phase 32: the purchasing consumer and its ports (Inbox, order repository, supplier notifier, approver directory) resolve.</summary>
    [Fact]
    public async Task PurchasingConsumer_ResolvesAndSettlesAnUnrelatedReport_WithTheSystemActor()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Template.Application.Interfaces.Purchasing.ISupplierOrderNotifier>().ShouldNotBeNull();
        var handler = scope.ServiceProvider.GetRequiredService<Template.Application.Interfaces.Messaging.IProcurementIntegrationEventHandler>();
        var envelope = Template.Application.Messaging.Procurement.ProcurementIntegrationEventContract.FromDomainEvent(
            new Template.Domain.Reporting.ReportReady(DateTime.UtcNow, Guid.NewGuid(), Template.Domain.Reporting.ReportType.RequisitionPdf, Guid.NewGuid(), Guid.NewGuid()),
            "worker-composition-test");
        (await handler.HandleAsync(Template.Application.Messaging.Procurement.ProcurementConsumers.Purchasing, envelope, CancellationToken.None))
            .ShouldBe(Template.Application.Interfaces.Messaging.InboxApplyResult.Applied);
        (await handler.HandleAsync(Template.Application.Messaging.Procurement.ProcurementConsumers.Purchasing, envelope, CancellationToken.None))
            .ShouldBe(Template.Application.Interfaces.Messaging.InboxApplyResult.Duplicate);
    }

    /// <summary>Phase 33 (D33-1): both scheduled sweeps resolve their use cases and run without a failure.</summary>
    [Fact]
    public async Task ScheduledSweeps_ResolveAndRun_WithoutFailures()
    {
        var failures = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == WorkerMetrics.MeterName && instrument.Name == "worker.sweep_failures")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref failures, value));
        listener.Start();
        // The WebApi owns seeding (organization settings); the Worker only migrates.
        await using (var seed = factory.Services.CreateAsyncScope())
            await seed.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: false, seedDefaultData: true);

        await factory.Services.GetRequiredService<ScheduledSweepService>().RunOnceAsync(10, CancellationToken.None);

        Interlocked.Read(ref failures).ShouldBe(0);
    }

    /// <summary>Phase 33: a consumer scope keeps one correlation ID and adopts only a safe inbound one.</summary>
    [Fact]
    public async Task RequestContext_IsStablePerScope_AndAdoptsOnlySafeInboundCorrelation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Template.Application.Interfaces.IRequestContext>();
        var first = context.CorrelationId;
        context.CorrelationId.ShouldBe(first);

        var inbound = scope.ServiceProvider.GetRequiredService<Template.Application.Interfaces.IInboundCorrelation>();
        inbound.Adopt("0HNPI2F1R3GUN:00000001");
        context.CorrelationId.ShouldBe("0HNPI2F1R3GUN:00000001");
        inbound.Adopt("<script>alert(1)</script>");
        context.CorrelationId.ShouldBe("0HNPI2F1R3GUN:00000001");
    }

    public sealed class WorkerFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("TestDatabaseName", Guid.NewGuid().ToString("N"));
            builder.UseSetting("RabbitMqSettings:Enabled", "false");
            builder.UseSetting("RabbitMqSettings:ConsumersEnabled", "false");
            builder.UseSetting("RabbitMqSettings:UserName", "synthetic");
            builder.UseSetting("RabbitMqSettings:Password", Guid.NewGuid().ToString("N"));
            builder.UseSetting("FileStorageSettings:Enabled", "false");
            builder.UseSetting("ReportWorkerSettings:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                foreach (var hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToArray())
                    services.Remove(hosted);
            });
        }
    }
}
