using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Domain.Files;
using Template.Domain.Reporting;
using Template.Infrastructure.Persistence;
using Template.Integration.Tests.Reporting;

namespace Template.Integration.Tests.Data;

/// <summary>
/// Phase 30 report and file adapters on EF InMemory through the real Persistence registration: job round trip, active
/// lookup, list filters, the in-memory branch of the due-job queue, expired-ready selection, FIL-005 cleanup selection
/// and the report source reader. Row locks, the partial unique index and SKIP LOCKED are PostgreSQL semantics covered by
/// the PostgreSQL suite.
/// </summary>
public sealed class ReportingPersistenceAdapterTests : IAsyncDisposable
{
    private static readonly DateTime Now = ReportSampleData.Now;
    private readonly ServiceProvider provider;

    public ReportingPersistenceAdapterTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestDatabaseName"] = Guid.NewGuid().ToString("N") })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "integration"));
        provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Jobs_RoundTrip_ActiveLookup_ListFilters_AndQueueOrder()
    {
        var requester = Guid.NewGuid();
        var older = ReportSampleData.Job(ReportType.RequisitionPdf, new(RequisitionId: Guid.NewGuid(), RequisitionVersion: 1), requester);
        var newer = ReportSampleData.Job(ReportType.BudgetConsumptionXlsx, new(FiscalPeriodId: Guid.NewGuid()), Guid.NewGuid());
        await SaveAsync(scope =>
        {
            var jobs = scope.GetRequiredService<IReportJobRepository>();
            jobs.Add(older);
            jobs.Add(newer);
        });

        await using (var read = provider.CreateAsyncScope())
        {
            var jobs = read.ServiceProvider.GetRequiredService<IReportJobRepository>();
            var stored = (await jobs.FindAsync(older.Id, CancellationToken.None))!;
            stored.ParametersJson.ShouldBe(older.ParametersJson);
            stored.TemplateVersion.ShouldBe("requisition-pdf.v1");
            (await jobs.FindActiveAsync(requester, ReportType.RequisitionPdf, older.ParametersHash, CancellationToken.None))!.Id.ShouldBe(older.Id);
            (await jobs.FindActiveAsync(requester, ReportType.QuoteComparisonPdf, older.ParametersHash, CancellationToken.None)).ShouldBeNull();
            (await jobs.GetVersionAsync(older.Id, CancellationToken.None)).ShouldBe(older.Version);
            var page = PageRequest.Create(1, 10, null, IReportJobRepository.SortFields, "-requestedAt").Value;
            (await jobs.ListAsync(new ReportJobFilter(RequestedBy: requester), page, CancellationToken.None)).Items.Single().Id.ShouldBe(older.Id);
            (await jobs.ListAsync(new ReportJobFilter(Types: [ReportType.BudgetConsumptionXlsx]), page, CancellationToken.None)).Items.Single().Id.ShouldBe(newer.Id);
            (await jobs.ListAsync(new ReportJobFilter(Statuses: [ReportJobStatus.Ready]), page, CancellationToken.None)).TotalItems.ShouldBe(0);

            var queue = read.ServiceProvider.GetRequiredService<IReportJobQueue>();
            (await queue.LockNextDueAsync(Now.AddMinutes(-1), CancellationToken.None)).ShouldBeNull();
            var due = (await queue.LockNextDueAsync(Now, CancellationToken.None))!;
            due.Claim("worker", ReportPolicy.V1, Now).Claimed.ShouldBeTrue();
            await read.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var next = provider.CreateAsyncScope();
        var nextJob = (await next.ServiceProvider.GetRequiredService<IReportJobQueue>().LockNextDueAsync(Now, CancellationToken.None))!;
        nextJob.Status.ShouldBe(ReportJobStatus.Pending); // the claimed one is Running with a live lease
        var leaseExpired = await next.ServiceProvider.GetRequiredService<IReportJobQueue>().LockNextDueAsync(Now.AddMinutes(10), CancellationToken.None);
        leaseExpired.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExpiredReadyJobs_AndFileCleanupCandidates_AreSelectedByPolicy()
    {
        var job = ReportSampleData.Job(ReportType.RequisitionPdf, new(RequisitionId: Guid.NewGuid(), RequisitionVersion: 1));
        var output = File(Now, Now.AddDays(7));
        output.MarkAvailable(10, new string('b', 64), 100, Now);
        job.Claim("w", ReportPolicy.V1, Now);
        job.Complete("w", output.Id, "r.pdf", 10, ReportPolicy.V1, Now);
        var orphan = File(Now.AddDays(-2), null);
        var fresh = File(Now, null);
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IStoredFileRepository>().Add(output);
            scope.GetRequiredService<IStoredFileRepository>().Add(orphan);
            scope.GetRequiredService<IStoredFileRepository>().Add(fresh);
            scope.GetRequiredService<IReportJobRepository>().Add(job);
        });

        await using var read = provider.CreateAsyncScope();
        var queue = read.ServiceProvider.GetRequiredService<IReportJobQueue>();
        (await queue.ListExpiredReadyAsync(Now.AddDays(6), 10, CancellationToken.None)).ShouldBeEmpty();
        (await queue.ListExpiredReadyAsync(Now.AddDays(7), 10, CancellationToken.None)).ShouldBe([job.Id]);
        var files = read.ServiceProvider.GetRequiredService<IStoredFileRepository>();
        (await files.ListCleanupCandidatesAsync(FileRetentionPolicy.V1, Now, 10, CancellationToken.None)).Select(file => file.Id).ShouldBe([orphan.Id]);
        (await files.ListCleanupCandidatesAsync(FileRetentionPolicy.V1, Now.AddDays(7), 10, CancellationToken.None)).Select(file => file.Id)
            .ShouldBe([orphan.Id, output.Id, fresh.Id], ignoreOrder: true);
        (await files.FindAsync(output.Id, CancellationToken.None))!.Sha256.ShouldBe(new string('b', 64));
    }

    [Fact]
    public async Task SourceReader_LoadsAllocationsWithHistory_AndRfqsByQuoteWindow()
    {
        var costCenterId = Guid.NewGuid();
        var (period, allocation) = ReportSampleData.Budget(costCenterId);
        var rfq = ReportSampleData.ClosedRfq();
        await SaveAsync(scope =>
        {
            var context = scope.GetRequiredService<Template.Infrastructure.Persistence.Contexts.ApplicationDbContext>();
            context.AddRange(period, allocation, rfq);
        });

        await using var read = provider.CreateAsyncScope();
        var reader = read.ServiceProvider.GetRequiredService<IReportSourceReader>();
        (await reader.FindFiscalPeriodAsync(period.Id, CancellationToken.None))!.Code.ShouldBe("FY27");
        var loaded = (await reader.ListAllocationsAsync(period.Id, "BRL", null, 10, CancellationToken.None)).Single();
        loaded.Reservations.Single().Changes.ShouldNotBeEmpty();
        loaded.AmountHistory.ShouldNotBeEmpty();
        (await reader.ListAllocationsAsync(period.Id, "USD", null, 10, CancellationToken.None)).ShouldBeEmpty();
        (await reader.ListAllocationsAsync(period.Id, null, Guid.NewGuid(), 10, CancellationToken.None)).ShouldBeEmpty();
        (await reader.GetCostCenterLabelsAsync([costCenterId], CancellationToken.None)).ShouldBeEmpty(); // labels only for existing cost centers

        var window = await reader.ListRfqsWithQuotesAsync(Now.AddDays(-3), Now, null, 10, CancellationToken.None);
        window.Single().Quotes.Count.ShouldBe(3);
        window.Single().Comparisons.Single().Entries.Count.ShouldBe(3);
        (await reader.ListRfqsWithQuotesAsync(Now, Now.AddDays(1), null, 10, CancellationToken.None)).ShouldBeEmpty();
        (await reader.ListRfqsWithQuotesAsync(Now.AddDays(-3), Now, Guid.NewGuid(), 10, CancellationToken.None)).ShouldBeEmpty();
    }

    public async ValueTask DisposeAsync() => await provider.DisposeAsync();

    private static StoredFile File(DateTime createdAt, DateTime? expires) =>
        StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", Guid.NewGuid(), "r.pdf", "application/pdf", FileClassification.Confidential, null, createdAt, expires).File!;

    private async Task SaveAsync(Action<IServiceProvider> arrange)
    {
        await using var scope = provider.CreateAsyncScope();
        arrange(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }
}
