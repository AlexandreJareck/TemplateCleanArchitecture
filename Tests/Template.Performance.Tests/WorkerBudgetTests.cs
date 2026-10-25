extern alias worker;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Application.Services.Reporting;
using Template.Domain.Reporting;
using Template.Infrastructure.Files;
using Template.Infrastructure.Persistence.Contexts;
using Testcontainers.RabbitMq;
using Xunit.Abstractions;

namespace Template.Performance.Tests;

/// <summary>
/// NFR-PERF-5 (one Worker drains a backlog of 1,000 Outbox events within 60 s, with RabbitMQ publisher confirms) and the
/// XLSX part of NFR-PERF-6 (a workbook of about 10,000 rows within 30 s), on the real Worker host and the seeded dataset.
/// </summary>
[Collection(PerformanceCollection.Name)]
public sealed class WorkerBudgetTests(PerformanceFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private const string RabbitMqImage = "rabbitmq:4.3.6-management@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd";
    private readonly string rabbitPassword = $"ephemeral-{Guid.NewGuid():N}";
    private RabbitMqContainer rabbit = null!;

    public async Task InitializeAsync()
    {
        rabbit = new RabbitMqBuilder(RabbitMqImage).WithUsername("nfr_user").WithPassword(rabbitPassword).Build();
        await rabbit.StartAsync();
    }

    public async Task DisposeAsync() => await rabbit.DisposeAsync();

    [Fact]
    public async Task NFR_PERF_5_OneWorker_DrainsAThousandOutboxEvents_WithinSixtySeconds()
    {
        const int events = 1_000;
        await using (var scope = fixture.Api.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutbox>();
            for (var index = 0; index < events; index++)
            {
                outbox.Add(ProcurementIntegrationEventContract.FromDomainEvent(
                    new ReportReady(DateTime.UtcNow, Guid.NewGuid(), ReportType.RequisitionPdf, Guid.NewGuid(), Guid.NewGuid()), $"nfr-perf-5-{index}"));
            }

            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        (await PendingAsync()).ShouldBeGreaterThanOrEqualTo(events);
        var clock = Stopwatch.StartNew();
        await using var host = WorkerHost(dispatch: true);
        _ = host.Services; // starts the Worker: dispatcher and consumers begin
        while (await PendingAsync() > 0 && clock.Elapsed < TimeSpan.FromSeconds(120))
            await Task.Delay(250);
        clock.Stop();

        var pending = await PendingAsync();
        var measurement = PerformanceEvidence.Record(new Measurement("NFR-PERF-5", $"drain {events} Outbox events (one Worker)", [clock.Elapsed.TotalMilliseconds], 60_000));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Outbox drain: {clock.Elapsed.TotalSeconds:F1} s, pending after: {pending}"));
        pending.ShouldBe(0);
        measurement.Passed.ShouldBeTrue($"drain took {clock.Elapsed.TotalSeconds:F1} s");
    }

    [Fact]
    public async Task NFR_PERF_6_BudgetConsumptionWorkbook_OfAboutTenThousandRows_WithinThirtySeconds()
    {
        var auditor = await fixture.SignInAsync("demo.auditor");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/report-jobs")
        {
            Content = JsonContent.Create(new
            {
                reportType = "BudgetConsumptionXlsx",
                fiscalPeriodId = fixture.Dataset.FiscalPeriodId,
                costCenterId = fixture.Dataset.XlsxCostCenterId,
                currency = "BRL"
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var accepted = await auditor.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        var jobId = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        await using var host = WorkerHost(dispatch: false);
        await using var scope = host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<ReportJobProcessor>();
        var clock = Stopwatch.StartNew();
        var (outcome, processed) = await processor.ProcessNextAsync("nfr-perf-6", CancellationToken.None);
        clock.Stop();

        await using var context = fixture.CreateContext();
        var job = await context.Set<ReportJob>().AsNoTracking().SingleAsync(item => item.Id == jobId);
        var file = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var content = (await file.OpenReadAsync(job.OutputFileId!.Value, CancellationToken.None))!;
        var rows = WorkbookRows(content);
        var measurement = PerformanceEvidence.Record(new Measurement("NFR-PERF-6", $"budget-consumption XLSX ({rows} rows)", [clock.Elapsed.TotalMilliseconds], 30_000));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"XLSX: {outcome} {processed} in {clock.Elapsed.TotalSeconds:F1} s, {rows} rows, status {job.Status}"));
        job.Status.ShouldBe(ReportJobStatus.Ready, job.FailureCode);
        rows.ShouldBeGreaterThanOrEqualTo(9_900);
        measurement.Passed.ShouldBeTrue($"render took {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Data rows across every sheet of the workbook (ClosedXML is a Worker dependency, so it is available here).</summary>
    private static int WorkbookRows(Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        copy.Position = 0;
        using var workbook = new ClosedXML.Excel.XLWorkbook(copy);
        return workbook.Worksheets.Sum(sheet => sheet.RowsUsed().Count());
    }

    private async Task<long> PendingAsync()
    {
        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"MessagingOutbox\" WHERE \"DispatchedAtUtc\" IS NULL AND \"PoisonedAtUtc\" IS NULL";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private WebApplicationFactory<worker::Program> WorkerHost(bool dispatch) =>
        new WorkerFactory(fixture.ApplicationConnectionString, rabbit.Hostname, rabbit.GetMappedPublicPort(5672), rabbitPassword, dispatch);

    private sealed class WorkerFactory(string connectionString, string rabbitHost, int rabbitPort, string rabbitPassword, bool dispatch)
        : WebApplicationFactory<worker::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("UseInMemoryDatabase", "false");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("RabbitMqSettings:Enabled", dispatch ? "true" : "false");
            builder.UseSetting("RabbitMqSettings:ConsumersEnabled", dispatch ? "true" : "false");
            builder.UseSetting("RabbitMqSettings:HostName", rabbitHost);
            builder.UseSetting("RabbitMqSettings:Port", rabbitPort.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMqSettings:UserName", "nfr_user");
            builder.UseSetting("RabbitMqSettings:Password", rabbitPassword);
            builder.UseSetting("FileStorageSettings:Enabled", "false");
            builder.UseSetting("ApprovalWorkflowSettings:Enabled", "false");
            builder.UseSetting("ReportWorkerSettings:Enabled", "false");
            builder.UseSetting("ScheduledSweepSettings:Enabled", "false");
            builder.UseSetting("ApprovalReconciliationSettings:Enabled", "false");
            builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<InMemoryFileStorage>();
                services.AddSingleton<IFileStorage>(provider => provider.GetRequiredService<InMemoryFileStorage>());
                if (!dispatch)
                {
                    foreach (var hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToArray())
                        services.Remove(hosted);
                }
            });
        }
    }
}
