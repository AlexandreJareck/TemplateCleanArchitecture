using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Organization;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Auditing;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 27 budget and requisition behavior on real PostgreSQL 16.15 (never EF InMemory): migration shape and
/// reversibility, the BUD-006 row lock under 20 parallel submissions, atomic submission + reservation + audit (REQ-005,
/// AUD-001), release exactly once under concurrent cancellation (BUD-007, REQ-008), the exclusion constraint against
/// overlapping periods (BUD-001), the partial unique index of active reservations (BUD-008), the availability check
/// constraint, BUD-011 close-versus-submit serialization, gap-tolerant unique numbering and representative query plans.
/// Each test works in its own synthetic fiscal year so the shared database never mixes cases.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class RequisitionBudgetPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private const string Phase25Migration = "20261018135425_Phase25MasterData";
    private const string Reason = "Synthetic cancellation for the PostgreSQL suite";
    private static int nextYear = 2100;

    private readonly int year = Interlocked.Increment(ref nextYear);
    private readonly Guid requester = Guid.NewGuid();
    private ServiceProvider provider = null!;
    private Guid costCenterId;
    private Guid categoryId;
    private Guid unitId;

    private DateTime Now => new(year, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IAuthenticatedUserService>(new Actor(requester));
        services.AddSingleton<IRequestContext>(new Trace());
        services.AddSingleton<IAccountDirectory>(new ActiveUsers());
        services.AddSingleton<IPermissionReader>(new AllRequesterAndBudgetPermissions());
        services.AddScoped<ProcurementWriter>();
        services.AddScoped<RequisitionAccess>();
        services.AddScoped<FiscalPeriodCommandHandlers>();
        services.AddScoped<BudgetAllocationCommandHandlers>();
        services.AddScoped<RequisitionDraftCommandHandlers>();
        services.AddScoped<RequisitionTransitionHandlers>();
        services.AddScoped<RequisitionQueryHandlers>();
        provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        await using var context = fixture.CreateContext();
        var department = Department.Create(Guid.NewGuid(), Code("D"), "Synthetic department", null, null, Now).Department!;
        var costCenter = CostCenter.Create(Guid.NewGuid(), Code("C"), "Synthetic cost center", department, null, null, Now).CostCenter!;
        var category = Category.Create(Guid.NewGuid(), Code("G"), "Synthetic category", null, Now).Category!;
        var unit = UnitOfMeasure.Create(Guid.NewGuid(), Code("U"), "Synthetic unit", 0, null, Now).Unit!;
        context.AddRange(department, costCenter, category, unit);
        await context.SaveChangesAsync();
        (costCenterId, categoryId, unitId) = (costCenter.Id, category.Id, unit.Id);
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_CreatesTables_PartialUniqueIndex_Exclusion_AndCheckConstraints()
    {
        await using var context = fixture.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync()).ShouldContain(migration => migration.EndsWith("Phase27PurchaseRequisitionsBudget", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_name IN ('BudgetFiscalPeriods', 'BudgetAllocations', 'BudgetAllocationAmountChanges', 'BudgetReservations',
                                 'BudgetReservationChanges', 'PurchaseRequisitions', 'PurchaseRequisitionLines', 'PurchaseRequisitionAttachments',
                                 'PurchaseRequisitionStateChanges', 'PurchaseRequisitionSubmissions', 'PurchaseRequisitionSubmissionLines',
                                 'PurchaseRequisitionNumberCounters')
            """)).ShouldBe(12);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_BudgetReservations_RequisitionId_Active'"))
            .ShouldContain("WHERE ((\"Status\")::text = ANY");
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'EX_BudgetFiscalPeriods_NoOverlap'"))
            .ShouldContain("EXCLUDE USING gist");
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM pg_constraint
            WHERE contype = 'c' AND conname IN ('CK_BudgetAllocations_Amount', 'CK_BudgetAllocations_Totals', 'CK_BudgetReservations_Amount',
                  'CK_BudgetFiscalPeriods_DateRange', 'CK_PurchaseRequisitionLines_Quantity', 'CK_PurchaseRequisitionLines_EstimatedUnitPrice',
                  'CK_PurchaseRequisitionAttachments_Length', 'CK_PurchaseRequisitions_EstimatedTotal', 'CK_PurchaseRequisitions_Revision')
            """)).ShouldBe(9);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM pg_constraint
            WHERE contype = 'f' AND confdeltype = 'r'
              AND conname IN ('FK_BudgetAllocations_OrganizationCostCenters_CostCenterId', 'FK_BudgetAllocations_BudgetFiscalPeriods_FiscalPeriodId',
                              'FK_PurchaseRequisitions_OrganizationCostCenters_CostCenterId', 'FK_BudgetReservations_PurchaseRequisitions_RequisitionId',
                              'FK_PurchaseRequisitionLines_CatalogCategories_CategoryId', 'FK_PurchaseRequisitionLines_CatalogUnitsOfMeasure_UnitOfMeasur~')
            """)).ShouldBe(6);
    }

    [Fact]
    public async Task Migration_RollsBackToPhase25_AndReappliesIdempotently()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(Phase25Migration);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('BudgetAllocations', 'PurchaseRequisitions', 'BudgetFiscalPeriods')")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('OrganizationCostCenters', 'CatalogItems', 'AuditRecords')")).ShouldBe(3);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // a second run is a no-op
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM pg_constraint WHERE conname = 'EX_BudgetFiscalPeriods_NoOverlap'")).ShouldBe(1);
    }

    [Fact]
    public async Task T_BUD_006_a_TwentyParallelSubmissions_OnANearlyEmptyAllocation_NeverOverReserve()
    {
        var allocation = await OpenBudgetAsync(250m);
        var drafts = new List<RequisitionView>();
        for (var index = 0; index < 20; index++)
            drafts.Add(await SubmittableDraftAsync("100"));
        using var barrier = new Barrier(drafts.Count);

        var results = await Task.WhenAll(drafts.Select(draft => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await handlers.Handle(new SubmitRequisitionCommand(draft.Id, long.Parse(draft.Version, System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(2);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == ProblemCodes.BudgetInsufficient);
        await using var context = fixture.CreateContext();
        var stored = await context.Set<BudgetAllocation>().AsNoTracking().SingleAsync(item => item.Id == allocation);
        (stored.ReservedAmount, stored.AvailableAmount).ShouldBe((200m, 50m));
        (await context.Set<BudgetReservation>().CountAsync(reservation => reservation.AllocationId == allocation && reservation.Status == BudgetReservationStatus.Reserved))
            .ShouldBe(2);
        var ids = drafts.Select(draft => draft.Id).ToArray();
        (await context.Set<PurchaseRequisition>().CountAsync(item => ids.Contains(item.Id) && item.Status == RequisitionStatus.Submitted)).ShouldBe(2);
        (await context.Set<PurchaseRequisition>().CountAsync(item => ids.Contains(item.Id) && item.Status == RequisitionStatus.Draft)).ShouldBe(18);
        var reservationIds = await context.Set<BudgetReservation>().Where(reservation => reservation.AllocationId == allocation).Select(reservation => reservation.Id.ToString()).ToListAsync();
        (await context.Set<AuditRecord>().CountAsync(record => record.Action == BudgetMoney.ReservationCreated && reservationIds.Contains(record.ResourceId))).ShouldBe(2);
    }

    [Fact]
    public async Task T_REQ_005_a_FailedSubmission_RollsBack_AndLeavesTheDraftUnchanged()
    {
        await OpenBudgetAsync(50m);
        var draft = await SubmittableDraftAsync("100");

        var result = await Transitions(handlers => handlers.Handle(new SubmitRequisitionCommand(draft.Id, Version(draft)), CancellationToken.None));

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetInsufficient);
        await using var context = fixture.CreateContext();
        var stored = await context.Set<PurchaseRequisition>().AsNoTracking().Include(item => item.Submissions).SingleAsync(item => item.Id == draft.Id);
        (stored.Status, stored.Version, stored.Submissions.Count).ShouldBe((RequisitionStatus.Draft, Version(draft), 0));
        (await context.Set<BudgetReservation>().CountAsync(reservation => reservation.RequisitionId == draft.Id)).ShouldBe(0);
        (await context.Set<AuditRecord>().CountAsync(record => record.ResourceId == draft.Id.ToString() && record.Action == RequisitionAudit.Submitted)).ShouldBe(0);
    }

    [Fact]
    public async Task T_AUD_001_a_SubmissionReservationSnapshotAndAudit_CommitTogether()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var draft = await SubmittableDraftAsync("2", "123.4567");

        var result = await Transitions(handlers => handlers.Handle(new SubmitRequisitionCommand(draft.Id, Version(draft)), CancellationToken.None));

        result.IsSuccess.ShouldBeTrue();
        result.Value.EstimatedTotal.Amount.ShouldBe("246.91");
        await using var context = fixture.CreateContext();
        var reservation = await context.Set<BudgetReservation>().AsNoTracking().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == draft.Id);
        (reservation.AllocationId, reservation.Amount, reservation.Status).ShouldBe((allocation, 246.91m, BudgetReservationStatus.Reserved));
        reservation.Changes.Single().CorrelationId.ShouldBe("pg-procurement");
        var snapshot = await context.Set<RequisitionSubmission>().AsNoTracking().Include(item => item.Lines).SingleAsync(item => item.RequisitionId == draft.Id);
        snapshot.Lines.Single().EstimatedUnitPrice.ShouldBe(123.4567m);
        (await context.Set<AuditRecord>().CountAsync(record => record.ResourceId == draft.Id.ToString() && record.Action == RequisitionAudit.Submitted)).ShouldBe(1);
        (await context.Set<AuditRecord>().CountAsync(record => record.ResourceId == reservation.Id.ToString() && record.Action == BudgetMoney.ReservationCreated)).ShouldBe(1);
    }

    [Fact]
    public async Task SC_08_ConcurrentSubmissionsOfTheSameRequisition_OneWins_TheOtherIs412_AndOneReservationExists()
    {
        await OpenBudgetAsync(1000m);
        var draft = await SubmittableDraftAsync("100");
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await handlers.Handle(new SubmitRequisitionCommand(draft.Id, Version(draft)), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Single(result => !result.IsSuccess).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        await using var context = fixture.CreateContext();
        (await context.Set<BudgetReservation>().CountAsync(reservation => reservation.RequisitionId == draft.Id)).ShouldBe(1);
        (await context.Set<RequisitionSubmission>().CountAsync(submission => submission.RequisitionId == draft.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task T_BUD_007_a_And_T_REQ_008_b_ConcurrentCancellations_ReleaseExactlyOnce()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var submitted = await SubmittedAsync("300");
        using var barrier = new Barrier(3);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await handlers.Handle(new CancelRequisitionCommand(submitted.Id, Version(submitted), Reason), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == ProblemCodes.VersionMismatch);
        await using var context = fixture.CreateContext();
        var stored = await context.Set<BudgetAllocation>().AsNoTracking().SingleAsync(item => item.Id == allocation);
        (stored.ReservedAmount, stored.AvailableAmount).ShouldBe((0m, 1000m));
        var reservation = await context.Set<BudgetReservation>().AsNoTracking().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == submitted.Id);
        (reservation.Status, reservation.ReleaseReason, reservation.Changes.Count).ShouldBe((BudgetReservationStatus.Released, BudgetReleaseReason.Cancelled, 2));
        (await context.Set<AuditRecord>().CountAsync(record => record.ResourceId == reservation.Id.ToString() && record.Action == BudgetMoney.ReservationReleased)).ShouldBe(1);
    }

    [Fact]
    public async Task T_REQ_007_b_WithdrawThenResubmit_ReleasesOnce_AndCreatesANewRevisionReservation()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var submitted = await SubmittedAsync("400");

        var withdrawn = await Transitions(handlers => handlers.Handle(new WithdrawRequisitionCommand(submitted.Id, Version(submitted)), CancellationToken.None));
        var resubmitted = await Transitions(handlers => handlers.Handle(new SubmitRequisitionCommand(submitted.Id, Version(withdrawn.Value)), CancellationToken.None));

        resubmitted.Value.Revision.ShouldBe(2);
        await using var context = fixture.CreateContext();
        var reservations = await context.Set<BudgetReservation>().AsNoTracking().Where(item => item.RequisitionId == submitted.Id).OrderBy(item => item.RequisitionRevision).ToListAsync();
        reservations.Select(item => (item.RequisitionRevision, item.Status, item.ReleaseReason)).ShouldBe(
            [(1, BudgetReservationStatus.Released, (BudgetReleaseReason?)BudgetReleaseReason.Withdrawn), (2, BudgetReservationStatus.Reserved, null)]);
        (await context.Set<BudgetAllocation>().AsNoTracking().SingleAsync(item => item.Id == allocation)).ReservedAmount.ShouldBe(400m);
    }

    [Fact]
    public async Task T_BUD_004_b_ClosedPeriod_RejectsNewReservations()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var draft = await SubmittableDraftAsync("10");
        var periodId = await PeriodOfAsync(allocation);
        var period = await Periods(handlers => handlers.Handle(new ChangeFiscalPeriodStatusCommand(periodId, 2, FiscalPeriodTransition.Close), CancellationToken.None));
        period.Value.Status.ShouldBe("Closed");

        var result = await Transitions(handlers => handlers.Handle(new SubmitRequisitionCommand(draft.Id, Version(draft)), CancellationToken.None));

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetNoOpenAllocation);
    }

    [Fact]
    public async Task T_BUD_011_CloseRacingASubmission_NeverLeavesAReservationInAClosedPeriod()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var periodId = await PeriodOfAsync(allocation);
        var draft = await SubmittableDraftAsync("10");
        using var barrier = new Barrier(2);

        var submit = Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return (Result)await scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>()
                .Handle(new SubmitRequisitionCommand(draft.Id, Version(draft)), CancellationToken.None);
        });
        var close = Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return (Result)await scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>()
                .Handle(new ChangeFiscalPeriodStatusCommand(periodId, 2, FiscalPeriodTransition.Close), CancellationToken.None);
        });
        var (submitted, closed) = (await submit, await close);

        (submitted.IsSuccess && closed.IsSuccess).ShouldBeFalse();
        (submitted.IsSuccess || closed.IsSuccess).ShouldBeTrue();
        await using var context = fixture.CreateContext();
        var status = (await context.Set<FiscalPeriod>().AsNoTracking().SingleAsync(item => item.Id == periodId)).Status;
        var reserved = (await context.Set<BudgetAllocation>().AsNoTracking().SingleAsync(item => item.Id == allocation)).ReservedAmount;
        (status == FiscalPeriodStatus.Closed && reserved > 0).ShouldBeFalse();
        output.WriteLine($"close-vs-submit outcome: submitted={submitted.IsSuccess} closed={closed.IsSuccess} reserved={reserved}");
    }

    [Fact]
    public async Task T_BUD_001_b_OverlappingPeriods_CreatedConcurrently_OnlyOneWins()
    {
        using var barrier = new Barrier(3);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(index => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>()
                .Handle(new CreateFiscalPeriodCommand(Code($"P{index}"), "Overlap", new DateOnly(year, 1, 1 + index), new DateOnly(year, 12, 31)), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Where(result => !result.IsSuccess).Select(result => result.Error!.Code).ShouldAllBe(code => code == ProblemCodes.BudgetPeriodOverlap);
    }

    [Fact]
    public async Task T_BUD_002_a_AllocationsAreUniquePerPeriodCostCenterCurrency_UnderConcurrency()
    {
        var period = await Periods(handlers => handlers.Handle(new CreateFiscalPeriodCommand(Code("FY"), "Synthetic", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)), CancellationToken.None));
        using var barrier = new Barrier(3);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await scope.ServiceProvider.GetRequiredService<BudgetAllocationCommandHandlers>()
                .Handle(new CreateBudgetAllocationCommand(period.Value.Id, costCenterId, "BRL", "100.00"), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == ProblemCodes.BudgetAllocationDuplicate);
    }

    [Fact]
    public async Task T_BUD_008_a_TheDatabaseRejectsASecondActiveReservationForOneRequisition()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var submitted = await SubmittedAsync("10");
        await using var context = fixture.CreateContext();

        var error = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "BudgetReservations" ("Id", "AllocationId", "RequisitionId", "RequisitionRevision", "Amount", "Currency", "Status", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({Guid.NewGuid()}, {allocation}, {submitted.Id}, 9, 1.00, 'BRL', 'Reserved', {Now}, {Now})
            """));

        (error.SqlState, error.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "IX_BudgetReservations_RequisitionId_Active"));
    }

    [Fact]
    public async Task T_CALC_003_TheDatabaseNeverAllowsNegativeAvailability()
    {
        var allocation = await OpenBudgetAsync(100m);
        await using var context = fixture.CreateContext();

        var error = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"BudgetAllocations\" SET \"ReservedAmount\" = 100.01 WHERE \"Id\" = {allocation}"));

        (error.SqlState, error.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, "CK_BudgetAllocations_Totals"));
    }

    [Fact]
    public async Task SC_08_ConcurrentDraftLineEdits_OneWins_TheOtherIs412()
    {
        var draft = await DraftAsync();
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>()
                .Handle(new AddRequisitionLineCommand(draft.Id, Version(draft), Line((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Single(result => !result.IsSuccess).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        await using var context = fixture.CreateContext();
        (await context.Set<RequisitionLine>().CountAsync(line => line.RequisitionId == draft.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task RequisitionNumbers_AreUnique_UnderConcurrentDraftCreation()
    {
        using var barrier = new Barrier(10);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>()
                .Handle(new CreateRequisitionDraftCommand(costCenterId, "BRL", null, null), CancellationToken.None);
        })));

        results.ShouldAllBe(result => result.IsSuccess);
        results.Select(result => result.Value.Number).Distinct().Count().ShouldBe(10);
        results.ShouldAllBe(result => result.Value.Number.StartsWith($"REQ-{year}-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task T_REQ_010_a_NeverSubmittedDraft_IsDeleted_AndSubmittedHistoryIsKept()
    {
        var draft = await DraftAsync();
        await OpenBudgetAsync(1000m);
        var submitted = await SubmittedAsync("10");
        var withdrawn = await Transitions(handlers => handlers.Handle(new WithdrawRequisitionCommand(submitted.Id, Version(submitted)), CancellationToken.None));

        await using (var scope = provider.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>();
            (await drafts.Handle(new DiscardRequisitionDraftCommand(draft.Id, Version(draft)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await drafts.Handle(new DiscardRequisitionDraftCommand(submitted.Id, Version(withdrawn.Value)), CancellationToken.None)).Error!.Code
                .ShouldBe(ProblemCodes.RequisitionImmutable);
        }

        await using var context = fixture.CreateContext();
        (await context.Set<PurchaseRequisition>().AnyAsync(item => item.Id == draft.Id)).ShouldBeFalse();
        (await context.Set<RequisitionStateChange>().CountAsync(change => change.RequisitionId == draft.Id)).ShouldBe(0);
        (await context.Set<PurchaseRequisition>().AnyAsync(item => item.Id == submitted.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task RepresentativeQueryPlans_UseTheExpectedIndexes()
    {
        var allocation = await OpenBudgetAsync(1000m);
        var periodId = await PeriodOfAsync(allocation);
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET enable_seqscan = off");

        var allocationPlan = await PlanAsync(context, $"""
            SELECT "Id" FROM "BudgetAllocations" WHERE "FiscalPeriodId" = '{periodId}' AND "CostCenterId" = '{costCenterId}' AND "Currency" = 'BRL'
            """);
        var requesterPlan = await PlanAsync(context, $"""
            SELECT "Id" FROM "PurchaseRequisitions" WHERE "RequesterId" = '{requester}' ORDER BY "CreatedAtUtc" DESC LIMIT 20
            """);
        var reservationPlan = await PlanAsync(context, $"""
            SELECT "Id" FROM "BudgetReservations" WHERE "RequisitionId" = '{Guid.NewGuid()}' AND "Status" IN ('Reserved', 'Committed')
            """);

        allocationPlan.ShouldContain("IX_BudgetAllocations_FiscalPeriodId_CostCenterId_Currency");
        requesterPlan.ShouldContain("IX_PurchaseRequisitions_RequesterId_CreatedAtUtc");
        reservationPlan.ShouldContain("IX_BudgetReservations_RequisitionId");
        output.WriteLine(allocationPlan);
        output.WriteLine(requesterPlan);
        output.WriteLine(reservationPlan);
    }

    private static async Task<string> PlanAsync(DbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN " + sql;
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<Guid> OpenBudgetAsync(decimal amount)
    {
        var period = await Periods(handlers => handlers.Handle(
            new CreateFiscalPeriodCommand(Code("FY"), "Synthetic fiscal year", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)), CancellationToken.None));
        period.IsSuccess.ShouldBeTrue(period.Error?.Message);
        (await Periods(handlers => handlers.Handle(new ChangeFiscalPeriodStatusCommand(period.Value.Id, 1, FiscalPeriodTransition.Open), CancellationToken.None)))
            .IsSuccess.ShouldBeTrue();
        await using var scope = provider.CreateAsyncScope();
        var allocation = await scope.ServiceProvider.GetRequiredService<BudgetAllocationCommandHandlers>().Handle(
            new CreateBudgetAllocationCommand(period.Value.Id, costCenterId, "BRL", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None);
        allocation.IsSuccess.ShouldBeTrue(allocation.Error?.Message);
        return allocation.Value.Id;
    }

    private async Task<Guid> PeriodOfAsync(Guid allocation)
    {
        await using var context = fixture.CreateContext();
        return (await context.Set<BudgetAllocation>().AsNoTracking().SingleAsync(item => item.Id == allocation)).FiscalPeriodId;
    }

    private async Task<RequisitionView> DraftAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>().Handle(
            new CreateRequisitionDraftCommand(costCenterId, "BRL", "Synthetic purpose for PostgreSQL", new DateOnly(year, 6, 30)), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<RequisitionView> SubmittableDraftAsync(string quantity, string price = "1")
    {
        var draft = await DraftAsync();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>()
            .Handle(new AddRequisitionLineCommand(draft.Id, Version(draft), Line(quantity, price)), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<RequisitionView> SubmittedAsync(string quantity)
    {
        var draft = await SubmittableDraftAsync(quantity);
        var result = await Transitions(handlers => handlers.Handle(new SubmitRequisitionCommand(draft.Id, Version(draft)), CancellationToken.None));
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<T> Transitions<T>(Func<RequisitionTransitionHandlers, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>());
    }

    private async Task<T> Periods<T>(Func<FiscalPeriodCommandHandlers, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>());
    }

    private RequisitionLineRequest Line(string quantity, string price = "1") =>
        new(null, "Synthetic line", RequisitionLineKind.Good, categoryId, unitId, quantity, new MoneyInput(price, "BRL"));

    private static long Version(RequisitionView view) => long.Parse(view.Version, System.Globalization.CultureInfo.InvariantCulture);

    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(20, prefix.Length + 11)].ToUpperInvariant();

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class Actor(Guid id) : IAuthenticatedUserService
    {
        public string? UserId { get; } = id.ToString();
        public string? UserName => "pg-procurement";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId => "pg-procurement";
    }

    private sealed class ActiveUsers : IAccountDirectory
    {
        public Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken) => Task.FromResult<VerifiedAccount?>(null);
        public Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<AccountProfile?>(null);
        public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<PagedResult<UserSummary>> SearchUsersAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], page.Page, page.PageSize, 0));
    }

    private sealed class AllRequesterAndBudgetPermissions : IPermissionReader
    {
        private static readonly IReadOnlySet<string> Granted = SeededRoles.PermissionsByRole[SeededRoles.Requester]
            .Concat(SeededRoles.PermissionsByRole[SeededRoles.BudgetController]).ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Granted);
    }
}
