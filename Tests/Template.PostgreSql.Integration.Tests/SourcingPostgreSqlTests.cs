using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Services.Integrations;
using Template.Application.Services.Procurement;
using Template.Domain.Auditing;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 29 sourcing on real PostgreSQL 16.15: additive migration and rollback, the buyer journey persisted and reloaded
/// (snapshot stability after supplier changes, SUP-004), the PTAX rate snapshot persisted and reused (CALC-004-c),
/// SRC-001 one active RFQ under concurrent creation, a quote racing the close (SRC-012), concurrent awards (SRC-016)
/// with one BUD-009 adjustment, atomic rollback of a failed award (SRC-019) and the notification Inbox (SRC-022,
/// INT-003). Each test uses its own synthetic year.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class SourcingPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const string Phase28Migration = "20261021010433_Phase28ReliableMessaging";
    private const string Reason = "Synthetic sourcing justification";
    private static int nextYear = 2300;

    private readonly int year = Interlocked.Increment(ref nextYear);
    private readonly Guid actor = Guid.NewGuid();
    private readonly CountingRates rates = new();
    private MutableClock clock = null!;
    private ServiceProvider provider = null!;
    private Guid costCenterId;
    private Guid categoryId;
    private Guid unitId;

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
        services.AddSingleton<IAccountDirectory>(new ActiveUsers());
        services.AddSingleton<IPermissionReader>(new AllPermissions());
        services.AddSingleton<IExchangeRateProvider>(rates);
        services.AddScoped<PtaxRateResolver>();
        services.AddScoped<SourcingExchangeRates>();
        services.AddScoped<SourcingAccess>();
        services.AddScoped<ProcurementWriter>();
        services.AddScoped<RequisitionAccess>();
        services.AddScoped<FiscalPeriodCommandHandlers>();
        services.AddScoped<BudgetAllocationCommandHandlers>();
        services.AddScoped<RequisitionDraftCommandHandlers>();
        services.AddScoped<RequisitionTransitionHandlers>();
        services.AddScoped<RfqCommandHandlers>();
        services.AddScoped<QuoteCommandHandlers>();
        services.AddScoped<RfqQueryHandlers>();
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
    public async Task Migration_IsAdditive_AndRollsBackToPhase28_ThenReappliesIdempotently()
    {
        await using var context = fixture.CreateContext();
        string[] tables =
        [
            "SourcingRfqs", "SourcingRfqLines", "SourcingRfqInvitations", "SourcingQuotes", "SourcingQuoteRevisions", "SourcingQuoteLines",
            "SourcingQuoteAttachments", "SourcingWaivers", "SourcingExchangeRates", "SourcingComparisons", "SourcingComparisonEntries",
            "SourcingAwards", "SourcingRfqHistory", "SourcingRfqNumberCounters", "MessagingNotificationDeliveries"
        ];
        var list = string.Join(',', tables.Select(table => $"'{table}'"));
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM information_schema.tables WHERE table_name IN ({list})")).ShouldBe(15);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_SourcingRfqs_RequisitionId_Active'")).ShouldContain("WHERE ((\"Status\")::text <> 'Cancelled'");
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_SourcingAwards_RfqId_Pending'")).ShouldContain("PendingApproval");
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM information_schema.columns
            WHERE (table_name = 'PurchaseRequisitions' AND column_name IN ('ActiveRfqId', 'AwardId', 'AwardedSupplierId', 'AwardedSupplierName', 'AwardAmount'))
               OR (table_name = 'BudgetReservations' AND column_name = 'LastAwardId')
               OR (table_name = 'BudgetReservationChanges' AND column_name = 'AwardId')
            """)).ShouldBe(7);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM information_schema.columns
            WHERE column_name IN ('ActiveRfqId', 'AwardId', 'AwardedSupplierId', 'AwardedSupplierName', 'AwardAmount', 'LastAwardId') AND is_nullable = 'NO'
            """)).ShouldBe(0);

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Phase28Migration);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM information_schema.tables WHERE table_name IN ({list})")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('PurchaseRequisitions', 'MessagingOutbox', 'MessagingInbox')")).ShouldBe(3);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // a second run is a no-op
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SC_01_JourneyPersisted_SnapshotsSurviveSupplierChanges_AndThePtaxRateIsReusedFromStorage()
    {
        await OpenBudgetAsync(50_000m);
        var requisition = await SubmittedAsync("10", "100"); // 1,000.00 BRL: one invitation suffices
        var supplier = await ActiveSupplierAsync("Original Legal Name Ltda");
        rates.SellRates["USD"] = 5.4321m;
        var rfq = await PublishedRfqAsync(requisition, supplier);
        (await Quotes(handlers => handlers.Handle(new EnterQuoteCommand(rfq.Id, Version(rfq), supplier, Quote("10", "USD")), CancellationToken.None))).IsSuccess.ShouldBeTrue();
        clock.UtcNow = Now.AddDays(3);
        rfq = await GetAsync(rfq.Id);
        var closed = await Rfqs(handlers => handlers.Handle(new CloseRfqCommand(rfq.Id, Version(rfq), null), CancellationToken.None));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        rates.Calls.ShouldBe(1);

        await ChangeSupplierAsync(supplier, stored =>
        {
            stored.UpdateDetails("Renamed After Close Ltda", null, "BR", stored.TaxId, clock.UtcNow).ShouldBeNull();
            stored.Suspend(Reason, null, clock.UtcNow).ShouldBeNull();
        });

        var reloaded = await GetAsync(rfq.Id);
        reloaded.Invitations.Single().SupplierLegalName.ShouldBe("Original Legal Name Ltda");
        reloaded.Invitations.Single().SupplierStatusAtInvitation.ShouldBe("Active");
        var entry = reloaded.Comparison!.Entries.Single();
        (entry.SupplierLegalName, entry.SupplierActiveAtClose, entry.Rank).ShouldBe(("Original Legal Name Ltda", true, (int?)1));
        entry.NormalizedTotal!.Amount.ShouldBe("543.21"); // round(10 × 10.00 × 5.4321)
        var snapshot = reloaded.ExchangeRates.Single();
        (snapshot.Source, snapshot.Rate, snapshot.FromCurrency, snapshot.QuoteDate).ShouldBe(("BcbPtax", "5.4321", "USD", new DateOnly(year, 3, 3)));

        // SUP-003/SRC-016: the supplier is now suspended, so the award is blocked although the frozen comparison stays valid.
        var blocked = await Rfqs(handlers => handlers.Handle(new SubmitAwardCommand(rfq.Id, Version(reloaded), entry.QuoteRevisionId, null, null, null), CancellationToken.None));
        blocked.Error!.Code.ShouldBe(ProblemCodes.SupplierNotEligible);
        await ChangeSupplierAsync(supplier, stored => stored.Reactivate(Reason, null, clock.UtcNow).ShouldBeNull());

        var awarded = await Rfqs(handlers => handlers.Handle(new SubmitAwardCommand(rfq.Id, Version(reloaded), entry.QuoteRevisionId, null, null, null), CancellationToken.None));
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        rates.Calls.ShouldBe(1); // CALC-004-c: the award used the stored snapshot, not a new provider call
        await using (var context = fixture.CreateContext())
        {
            var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisition.Id);
            (reservation.Amount, reservation.Status, reservation.Changes.Count).ShouldBe((543.21m, BudgetReservationStatus.Reserved, 2));
            reservation.Changes.Last().AwardId.ShouldBe(awarded.Value.Awards.Single().Id);
            (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisition.Id)).Status.ShouldBe(RequisitionStatus.PendingApproval);
            var outbox = await context.Set<OutboxMessage>().Where(message => message.AggregateId == rfq.Id || message.AggregateId == reservation.Id)
                .Select(message => message.EventType).ToListAsync();
            outbox.ShouldContain("procurement.award.submitted");
            outbox.ShouldContain("procurement.budget.reservation-adjusted");
            (await context.Set<AuditRecord>().CountAsync(record => record.ResourceId == rfq.Id.ToString() && record.Action == SourcingAudit.AwardSubmitted)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task T_SRC_001_b_ConcurrentRfqCreation_ForOneRequisition_LeavesOneActiveRfq()
    {
        await OpenBudgetAsync(10_000m);
        var requisition = await SubmittedAsync("1", "100");
        using var barrier = new Barrier(4);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<RfqCommandHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await handlers.Handle(new CreateRfqCommand(requisition.Id, null), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == ProblemCodes.RfqAlreadyActive);
        await using var context = fixture.CreateContext();
        (await context.Set<RequestForQuotation>().CountAsync(item => item.RequisitionId == requisition.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task T_SRC_012_QuoteRacingTheClose_OneWins_AndTheComparisonMatchesTheCommittedRevisions()
    {
        await OpenBudgetAsync(10_000m);
        var requisition = await SubmittedAsync("1", "100");
        var supplier = await ActiveSupplierAsync("Racing Supplier Ltda");
        var rfq = await PublishedRfqAsync(requisition, supplier);
        clock.UtcNow = Now.AddDays(3); // at the deadline: a quote received earlier is still accepted; the close is normal
        using var barrier = new Barrier(2);

        var quote = Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<QuoteCommandHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return (await handlers.Handle(new EnterQuoteCommand(rfq.Id, Version(rfq), supplier, Quote("50", receivedAt: Now.AddDays(2))), CancellationToken.None)).Error;
        });
        var close = Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<RfqCommandHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return (await handlers.Handle(new CloseRfqCommand(rfq.Id, Version(rfq), null), CancellationToken.None)).Error;
        });
        var errors = await Task.WhenAll(quote, close);

        errors.Count(error => error is null).ShouldBe(1);
        errors.Single(error => error is not null)!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        var stored = await GetAsync(rfq.Id);
        if (errors[0] is null)
        {
            stored.Status.ShouldBe("Published");
            stored.Quotes!.Single().Revisions.Count.ShouldBe(1);
        }
        else
        {
            stored.Status.ShouldBe("Closed");
            stored.Quotes!.ShouldBeEmpty();
            stored.Comparison!.Entries.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task T_SRC_016_b_ConcurrentAwards_OneWins_WithOneReservationAdjustment()
    {
        await OpenBudgetAsync(10_000m);
        var requisition = await SubmittedAsync("1", "100");
        var supplier = await ActiveSupplierAsync("Award Supplier Ltda");
        var rfq = await ClosedRfqAsync(requisition, supplier, "80");
        var revision = rfq.Comparison!.RecommendedQuoteRevisionId!.Value;
        using var barrier = new Barrier(3);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<RfqCommandHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await handlers.Handle(new SubmitAwardCommand(rfq.Id, Version(rfq), revision, null, null, null), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == ProblemCodes.VersionMismatch);
        await using var context = fixture.CreateContext();
        (await context.Set<RfqAward>().CountAsync(award => award.RfqId == rfq.Id)).ShouldBe(1);
        var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisition.Id);
        reservation.Amount.ShouldBe(800m); // 10 × 80.00
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Adjusted).ShouldBe(1);
    }

    [Fact]
    public async Task T_SRC_019_a_FailedAward_RollsBackEverything()
    {
        var allocation = await OpenBudgetAsync(150m);
        var requisition = await SubmittedAsync("1", "100");
        var supplier = await ActiveSupplierAsync("Too Expensive Ltda");
        var rfq = await ClosedRfqAsync(requisition, supplier, "200"); // increase of 100.00 > available 50.00

        var result = await Rfqs(handlers => handlers.Handle(
            new SubmitAwardCommand(rfq.Id, Version(rfq), rfq.Comparison!.RecommendedQuoteRevisionId!.Value, null, null, null), CancellationToken.None));

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetInsufficient);
        await using var context = fixture.CreateContext();
        (await context.Set<RequestForQuotation>().SingleAsync(item => item.Id == rfq.Id)).Status.ShouldBe(RfqStatus.Closed);
        (await context.Set<RfqAward>().CountAsync(award => award.RfqId == rfq.Id)).ShouldBe(0);
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisition.Id)).Status.ShouldBe(RequisitionStatus.InSourcing);
        (await context.Set<BudgetAllocation>().SingleAsync(item => item.Id == allocation)).ReservedAmount.ShouldBe(100m);
        (await context.Set<OutboxMessage>().CountAsync(message => message.AggregateId == rfq.Id && message.EventType == "procurement.award.submitted")).ShouldBe(0);
    }

    [Fact]
    public async Task T_SRC_022_a_NotificationDeliveries_AreRecordedOnce_UnderConcurrentDuplicates()
    {
        var suppliers = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var envelope = ProcurementIntegrationEventContract.FromDomainEvent(
            new RfqPublished(Now, Guid.NewGuid(), "RFQ-0000-000001", Guid.NewGuid(), Now.AddDays(3), suppliers, SourcingMethod.Competitive), "pg-sourcing");
        using var barrier = new Barrier(6);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<INotificationDeliveryStore>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await store.DeliverOnceAsync(ProcurementConsumers.Notification, envelope, RfqNotificationPlanner.Plan(envelope), Now, CancellationToken.None);
        })));

        results.Count(result => result == InboxApplyResult.Applied).ShouldBe(1);
        await using var context = fixture.CreateContext();
        var deliveries = await context.Set<NotificationDelivery>().Where(delivery => delivery.EventId == envelope.EventId).ToListAsync();
        deliveries.Select(delivery => delivery.RecipientId).ShouldBe(suppliers, ignoreOrder: true);
        deliveries.ShouldAllBe(delivery => delivery.Template == RfqNotificationPlanner.InvitationTemplate && delivery.Channel == "Local");
        (await context.Set<InboxMessage>().CountAsync(message => message.EventId == envelope.EventId && message.CompletedAtUtc != null)).ShouldBe(1);
    }

    private async Task<RfqView> ClosedRfqAsync(RequisitionView requisition, Guid supplier, string unitPrice)
    {
        var rfq = await PublishedRfqAsync(requisition, supplier);
        (await Quotes(handlers => handlers.Handle(new EnterQuoteCommand(rfq.Id, Version(rfq), supplier, Quote(unitPrice)), CancellationToken.None))).IsSuccess.ShouldBeTrue();
        clock.UtcNow = Now.AddDays(3);
        rfq = await GetAsync(rfq.Id);
        var closed = await Rfqs(handlers => handlers.Handle(new CloseRfqCommand(rfq.Id, Version(rfq), null), CancellationToken.None));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        return closed.Value;
    }

    private async Task<RfqView> PublishedRfqAsync(RequisitionView requisition, Guid supplier)
    {
        var created = await Rfqs(handlers => handlers.Handle(new CreateRfqCommand(requisition.Id, null), CancellationToken.None));
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        var invited = await Rfqs(handlers => handlers.Handle(new InviteSupplierCommand(created.Value.Id, Version(created.Value), supplier), CancellationToken.None));
        invited.IsSuccess.ShouldBeTrue(invited.Error?.Message);
        var published = await Rfqs(handlers => handlers.Handle(new PublishRfqCommand(invited.Value.Id, Version(invited.Value), Now.AddDays(3), null), CancellationToken.None));
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        return published.Value;
    }

    private QuoteRequest Quote(string unitPrice, string currency = "BRL", DateTime? receivedAt = null) => new(
        currency, [new QuoteLineRequest(1, "10", unitPrice)], null, null, null, new DateOnly(year, 5, 31), 7, "30 days", null, QuoteSource.Email,
        receivedAt ?? Now, []);

    private async Task<Guid> ActiveSupplierAsync(string legalName)
    {
        var supplier = Supplier.CreateDraft(Guid.NewGuid(), legalName, null, "BR", SyntheticCnpj(), null, Now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "Synthetic Contact", "contact@example.invalid", null, null)], Now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, new AddressValues(
            SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR"))], Now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(categoryId, true)], Now).ShouldBeNull();
        supplier.Activate(null, Now).ShouldBeNull();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISupplierRepository>().Add(supplier);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return supplier.Id;
    }

    private async Task ChangeSupplierAsync(Guid id, Action<Supplier> change)
    {
        await using var scope = provider.CreateAsyncScope();
        var supplier = await scope.ServiceProvider.GetRequiredService<ISupplierRepository>().FindAsync(id, CancellationToken.None);
        change(supplier!);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task<Guid> OpenBudgetAsync(decimal amount)
    {
        await using var scope = provider.CreateAsyncScope();
        var periods = scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>();
        var period = await periods.Handle(new CreateFiscalPeriodCommand(Code("FY"), "Synthetic fiscal year", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)), CancellationToken.None);
        period.IsSuccess.ShouldBeTrue(period.Error?.Message);
        (await periods.Handle(new ChangeFiscalPeriodStatusCommand(period.Value.Id, 1, FiscalPeriodTransition.Open), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var allocation = await scope.ServiceProvider.GetRequiredService<BudgetAllocationCommandHandlers>().Handle(
            new CreateBudgetAllocationCommand(period.Value.Id, costCenterId, "BRL", amount.ToString(CultureInfo.InvariantCulture)), CancellationToken.None);
        allocation.IsSuccess.ShouldBeTrue(allocation.Error?.Message);
        return allocation.Value.Id;
    }

    private async Task<RequisitionView> SubmittedAsync(string quantity, string price)
    {
        await using var scope = provider.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>();
        var draft = await drafts.Handle(new CreateRequisitionDraftCommand(costCenterId, "BRL", "Synthetic purpose for sourcing", new DateOnly(year, 6, 30)), CancellationToken.None);
        draft.IsSuccess.ShouldBeTrue(draft.Error?.Message);
        var line = await drafts.Handle(new AddRequisitionLineCommand(draft.Value.Id, Version(draft.Value),
            new RequisitionLineRequest(null, "Synthetic line", RequisitionLineKind.Good, categoryId, unitId, quantity, new MoneyInput(price, "BRL"))), CancellationToken.None);
        line.IsSuccess.ShouldBeTrue(line.Error?.Message);
        var submitted = await scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>()
            .Handle(new SubmitRequisitionCommand(line.Value.Id, Version(line.Value)), CancellationToken.None);
        submitted.IsSuccess.ShouldBeTrue(submitted.Error?.Message);
        return submitted.Value;
    }

    private async Task<RfqView> GetAsync(Guid id)
    {
        var result = await RfqQueries(handlers => handlers.Handle(new GetRfqQuery(id), CancellationToken.None));
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<T> Rfqs<T>(Func<RfqCommandHandlers, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RfqCommandHandlers>());
    }

    private async Task<T> Quotes<T>(Func<QuoteCommandHandlers, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<QuoteCommandHandlers>());
    }

    private async Task<T> RfqQueries<T>(Func<RfqQueryHandlers, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RfqQueryHandlers>());
    }

    private static long Version(RequisitionView view) => long.Parse(view.Version, CultureInfo.InvariantCulture);

    private static long Version(RfqView view) => long.Parse(view.Version, CultureInfo.InvariantCulture);

    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(20, prefix.Length + 11)].ToUpperInvariant();

    private static string SyntheticCnpj()
    {
        var digits = Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture) + "0001";
        int Digit(string value, int[] weights)
        {
            var sum = value.Select((character, index) => (character - '0') * weights[index]).Sum() % 11;
            return sum < 2 ? 0 : 11 - sum;
        }

        var first = Digit(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = Digit(digits + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{digits}{first}{second}";
    }

    private sealed class MutableClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private sealed class Actor(Guid id) : IAuthenticatedUserService
    {
        public string? UserId { get; } = id.ToString();
        public string? UserName => "pg-sourcing";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId => "pg-sourcing";
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

    private sealed class AllPermissions : IPermissionReader
    {
        private static readonly IReadOnlySet<string> Granted = SeededRoles.PermissionsByRole[SeededRoles.Requester]
            .Concat(SeededRoles.PermissionsByRole[SeededRoles.BudgetController])
            .Concat(SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]).ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Granted);
    }

    /// <summary>Fake BCB PTAX provider for the close date (no network); counts calls to prove snapshot reuse.</summary>
    private sealed class CountingRates : IExchangeRateProvider
    {
        private int calls;

        public Dictionary<string, decimal> SellRates { get; } = new(StringComparer.Ordinal);
        public int Calls => Volatile.Read(ref calls);

        public Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
            string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(SellRates.TryGetValue(currency, out var rate)
                ? EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Found(EnrichmentProviders.BcbPtax, DateTime.UtcNow,
                    [new ExchangeRateBulletin(currency, toDate.AddDays(-1), rate - 0.0005m, rate, "Fechamento PTAX", true)])
                : EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BcbPtax, "synthetic"));
        }
    }
}
