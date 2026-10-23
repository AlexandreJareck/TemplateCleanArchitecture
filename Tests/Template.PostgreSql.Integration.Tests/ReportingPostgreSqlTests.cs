using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Features.Reporting;
using Template.Application.Features.Requisitions;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Reporting;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Application.Services.Reporting;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Files;
using Template.Domain.Organization;
using Template.Domain.Reporting;
using Template.Infrastructure.Files;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 30 files and reports on real PostgreSQL 16.15: additive migration and rollback, no byte column (FIL-001), the
/// partial unique index under concurrent identical requests (RPT-001), concurrent Workers claiming with
/// <c>FOR UPDATE SKIP LOCKED</c> so every job runs once with one output (RPT-005), crash recovery through the expired
/// lease, and the FIL-005 cleanup selection. Rendering uses a signed fake output; storage is the in-memory adapter.
/// Each test uses its own synthetic year.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const string Phase29Migration = "20261023025916_Phase29SourcingQuotations";
    private static int nextYear = 2400;

    private readonly int year = Interlocked.Increment(ref nextYear);
    private readonly Guid actor = Guid.NewGuid();
    private readonly InMemoryFileStorage storage = new();
    private readonly SignedRenderer renderer = new();
    private MutableClock clock = null!;
    private ServiceProvider provider = null!;
    private Guid periodId;

    private DateTime Now => new(year, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        clock = new MutableClock(Now);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IAuthenticatedUserService>(new Actor(actor));
        services.AddSingleton<IRequestContext>(new Trace());
        services.AddSingleton<IPermissionReader>(new ControllerPermissions());
        services.AddSingleton<IFileStorage>(storage);
        services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();
        services.AddSingleton<IPdfRenderer>(renderer);
        services.AddSingleton<ISpreadsheetWriter>(renderer);
        services.AddScoped<ProcurementWriter>();
        services.AddScoped<RequisitionAccess>();
        services.AddScoped<ReportAccess>();
        services.AddScoped<ReportJobHandlers>();
        services.AddScoped<IReportBuilder, BudgetConsumptionXlsxBuilder>();
        services.AddScoped<ReportJobProcessor>();
        services.AddScoped<ReportMaintenance>();
        provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        await using var context = fixture.CreateContext();
        if (!await context.Set<OrganizationSettings>().AnyAsync())
            context.Add(OrganizationSettings.CreateDefault(Now));
        var period = FiscalPeriod.Create(Guid.NewGuid(), $"R{year}", $"Synthetic report year {year}", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), null, Now).Period!;
        context.Add(period);
        await context.SaveChangesAsync();
        periodId = period.Id;
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_IsAdditive_HasNoByteColumns_AndRollsBackToPhase29_ThenReapplies()
    {
        await using var context = fixture.CreateContext();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('FilesStoredFiles', 'ReportingReportJobs')")).ShouldBe(2);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name IN ('FilesStoredFiles', 'ReportingReportJobs') AND (data_type = 'bytea' OR column_name ILIKE '%storagekey%' OR column_name ILIKE '%url%')
            """)).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_ReportingReportJobs_Active'")).ShouldContain("UNIQUE");
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_ReportingReportJobs_Active'")).ShouldContain("'Pending'");
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM pg_constraint WHERE conname IN ('CK_FilesStoredFiles_Available', 'CK_ReportingReportJobs_ReadyOutput', 'CK_ReportingReportJobs_Attempts')")).ShouldBe(3);

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Phase29Migration);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('FilesStoredFiles', 'ReportingReportJobs')")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('SourcingRfqs', 'MessagingOutbox', 'PurchaseRequisitions')")).ShouldBe(3);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task T_RPT_001_e_ConcurrentIdenticalRequests_CreateOneActiveJob()
    {
        using var barrier = new Barrier(6);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<ReportJobHandlers>();
            barrier.SignalAndWait();
            return await handlers.Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: periodId), CancellationToken.None);
        })));

        results.ShouldAllBe(result => result.IsSuccess);
        results.Select(result => result.Value.Job.Id).Distinct().Count().ShouldBe(1);
        results.Count(result => !result.Value.Reused).ShouldBe(1);
        await using var context = fixture.CreateContext();
        (await context.Set<ReportJob>().CountAsync(job => job.RequestedBy == actor)).ShouldBe(1);
        (await context.Set<OutboxMessage>().CountAsync(message => message.EventType == "procurement.report.requested" && message.AggregateId == results[0].Value.Job.Id))
            .ShouldBe(1);
    }

    [Fact]
    public async Task T_RPT_005_n_ConcurrentWorkers_ProcessEveryJobOnce_WithOneVerifiedOutputEach()
    {
        var jobIds = await SeedJobsAsync(12);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            var processed = new List<(ReportProcessingOutcome Outcome, Guid? JobId)>();
            while (true)
            {
                await using var scope = provider.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ReportJobProcessor>().ProcessNextAsync($"pg-worker-{worker}", CancellationToken.None);
                if (result.Outcome == ReportProcessingOutcome.Idle)
                    return processed;
                processed.Add(result);
            }
        })));

        var all = outcomes.SelectMany(list => list).ToArray();
        all.ShouldAllBe(result => result.Outcome == ReportProcessingOutcome.Ready);
        all.Select(result => result.JobId!.Value).Order().ShouldBe(jobIds.Order());
        await using var context = fixture.CreateContext();
        var jobs = await context.Set<ReportJob>().Where(job => jobIds.Contains(job.Id)).ToListAsync();
        jobs.ShouldAllBe(job => job.Status == ReportJobStatus.Ready && job.AttemptCount == 1 && job.OutputFileId != null);
        var files = await context.Set<StoredFile>().Where(file => jobIds.Contains(file.OwnerId)).ToListAsync();
        files.Count.ShouldBe(12);
        files.ShouldAllBe(file => file.Status == StoredFileStatus.Available && file.Sha256 != null && file.Classification == FileClassification.Confidential);
        files.Select(file => file.Id).Order().ShouldBe(jobs.Select(job => job.OutputFileId!.Value).Order());
        (await context.Set<OutboxMessage>().CountAsync(message => message.EventType == "procurement.report.ready" && jobIds.Contains(message.AggregateId))).ShouldBe(12);
    }

    [Fact]
    public async Task T_RPT_005_o_CrashedWorkerLease_IsRecoveredByAnotherWorker_AfterExpiry()
    {
        var jobId = (await SeedJobsAsync(1)).Single();
        await using (var context = fixture.CreateContext())
        {
            // The crashed worker claimed the job and died without completing or releasing it.
            var job = await context.Set<ReportJob>().SingleAsync(item => item.Id == jobId);
            job.Claim("crashed-worker", ReportPolicy.V1, Now).Claimed.ShouldBeTrue();
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<ReportJobProcessor>().ProcessNextAsync("survivor", CancellationToken.None)).Outcome
                .ShouldBe(ReportProcessingOutcome.Idle); // lease still valid

        clock.UtcNow = Now + ReportPolicy.V1.Lease;
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<ReportJobProcessor>().ProcessNextAsync("survivor", CancellationToken.None)).Outcome
                .ShouldBe(ReportProcessingOutcome.Ready);

        await using var verify = fixture.CreateContext();
        var recovered = await verify.Set<ReportJob>().SingleAsync(item => item.Id == jobId);
        (recovered.Status, recovered.AttemptCount).ShouldBe((ReportJobStatus.Ready, 2));
    }

    [Fact]
    public async Task T_FIL_005_b_RetentionPass_ExpiresJobs_AndDeletesOrphanAndExpiredFiles()
    {
        var jobId = (await SeedJobsAsync(1)).Single();
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<ReportJobProcessor>().ProcessNextAsync("w", CancellationToken.None)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var orphan = StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", Guid.NewGuid(), "orphan.pdf", "application/pdf", FileClassification.Confidential, null, Now, null).File!;
        await using (var context = fixture.CreateContext())
        {
            context.Add(orphan);
            await context.SaveChangesAsync();
        }

        await storage.WriteAsync(orphan.Id, new MemoryStream("%PDF-orphan"u8.ToArray()), "application/pdf", 1024, CancellationToken.None);
        clock.UtcNow = Now.AddDays(8);
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ReportMaintenance>().RunAsync(1000, CancellationToken.None);
            result.ExpiredJobs.ShouldBeGreaterThanOrEqualTo(1);
            result.DeletedFiles.ShouldBeGreaterThanOrEqualTo(2);
        }

        await using var verify = fixture.CreateContext();
        (await verify.Set<ReportJob>().SingleAsync(job => job.Id == jobId)).Status.ShouldBe(ReportJobStatus.Expired);
        (await verify.Set<StoredFile>().SingleAsync(file => file.Id == orphan.Id)).Status.ShouldBe(StoredFileStatus.Deleted);
        (await storage.ExistsAsync(orphan.Id, CancellationToken.None)).ShouldBeFalse();
        var output = await verify.Set<StoredFile>().SingleAsync(file => file.OwnerId == jobId);
        output.Status.ShouldBe(StoredFileStatus.Deleted);
        (await storage.ExistsAsync(output.Id, CancellationToken.None)).ShouldBeFalse();
    }

    private async Task<Guid[]> SeedJobsAsync(int count)
    {
        await using var context = fixture.CreateContext();
        var ids = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            var parameters = new ReportParameters(FiscalPeriodId: periodId, CostCenterId: Guid.NewGuid()); // distinct fingerprints
            var json = parameters.ToCanonicalJson();
            var job = ReportJob.Request(Guid.NewGuid(), ReportType.BudgetConsumptionXlsx, actor, json, ReportParameters.Fingerprint(ReportType.BudgetConsumptionXlsx, json),
                Now, "America/Sao_Paulo", ReportPolicy.V1, Now).Job!;
            context.Add(job);
            ids.Add(job.Id);
        }

        await context.SaveChangesAsync();
        return [.. ids];
    }

    private sealed class SignedRenderer : IPdfRenderer, ISpreadsheetWriter
    {
        public Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken) =>
            output.WriteAsync("%PDF-1.7 pg"u8.ToArray(), cancellationToken).AsTask();

        public Task WriteAsync(WorkbookDocument workbook, Stream output, CancellationToken cancellationToken) =>
            output.WriteAsync(new byte[] { 0x50, 0x4B, 0x03, 0x04, 9, 9 }, cancellationToken).AsTask();
    }

    private sealed class MutableClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private sealed class Actor(Guid id) : IAuthenticatedUserService
    {
        public string? UserId { get; } = id.ToString();
        public string? UserName => "pg-reports";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId => "pg-reports";
    }

    private sealed class ControllerPermissions : IPermissionReader
    {
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(SeededRoles.PermissionsByRole[SeededRoles.BudgetController].ToHashSet(StringComparer.Ordinal));
    }
}
