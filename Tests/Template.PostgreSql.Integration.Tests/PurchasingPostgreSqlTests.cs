using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Features.Purchasing;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Services.Integrations;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Infrastructure.Files;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 32 purchasing and receiving on real PostgreSQL 16.15: additive migration and rollback to Phase 31, exactly one
/// draft per award under concurrent consumers (PUR-001), unique and gapless numbers under parallel issues with the
/// counter returned by a rollback (PUR-005, SP-2), concurrent receipts that never exceed the cap (RCV-003, row lock),
/// cancellation releasing the commitment once (PUR-008) and the persisted short close (PUR-010). Each test uses its own
/// synthetic year, so counters and budgets never collide; data is synthetic.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PurchasingPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const string Phase31Migration = "20261023234947_Phase31ApprovalWorkflow";
    private const string Reason = "Synthetic purchasing reason for PostgreSQL";
    private static int nextYear = 2800;

    private readonly int year = Interlocked.Increment(ref nextYear);
    private readonly SwitchableActor actor = new();
    private readonly Approvers approvers = new();
    private readonly Guid requester = Guid.NewGuid();
    private readonly Guid buyer = Guid.NewGuid();
    private readonly Guid manager = Guid.NewGuid();
    private readonly Guid receiver = Guid.NewGuid();
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
        services.AddSingleton<IClock>(clock);
        services.AddApplicationLayer();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddFilesInfrastructure(configuration, useInMemoryStorage: true);
        services.AddSingleton<IAuthenticatedUserService>(actor);
        services.AddSingleton<IRequestContext>(new Trace());
        services.AddSingleton<IAccountDirectory>(new ActiveUsers());
        services.AddSingleton<IPermissionReader>(new AllPermissions());
        services.AddSingleton<IApproverDirectory>(approvers);
        services.AddSingleton<IApprovalWorkflowClient>(new RunningWorkflow());
        services.AddSingleton<IExchangeRateProvider>(new NoRates());
        foreach (var handler in new[]
                 {
                     typeof(FiscalPeriodCommandHandlers), typeof(BudgetAllocationCommandHandlers), typeof(RequisitionDraftCommandHandlers),
                     typeof(RequisitionTransitionHandlers), typeof(RfqCommandHandlers), typeof(QuoteCommandHandlers), typeof(ApprovalDecisionHandlers),
                     typeof(PurchaseOrderHandlers), typeof(ReceiptHandlers)
                 })
            services.AddScoped(handler);
        provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        await using var context = fixture.CreateContext();
        var department = Department.Create(Guid.NewGuid(), Code("D"), "Synthetic department", null, null, Now).Department!;
        var costCenter = CostCenter.Create(Guid.NewGuid(), Code("C"), "Synthetic cost center", department, manager, null, Now).CostCenter!;
        var category = Category.Create(Guid.NewGuid(), Code("G"), "Synthetic category", null, Now).Category!;
        var unit = UnitOfMeasure.Create(Guid.NewGuid(), Code("U"), "Synthetic unit", 0, null, Now).Unit!;
        context.AddRange(department, costCenter, category, unit);
        await context.SaveChangesAsync();
        (costCenterId, categoryId, unitId) = (costCenter.Id, category.Id, unit.Id);
        approvers.Add(manager, SeededRoles.CostCenterManager);
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_IsAdditive_AndRollsBackToPhase31_ThenReapplies()
    {
        await using var context = fixture.CreateContext();
        string[] tables =
        [
            "PurchasingOrders", "PurchasingOrderLines", "PurchasingOrderHistory", "PurchasingOrderNumberCounters", "ReceivingReceipts",
            "ReceivingReceiptLines", "ReceivingReceiptCorrections", "ReceivingReceiptAttachments", "ReceivingReceiptNumberCounters"
        ];
        var list = string.Join(',', tables.Select(table => $"'{table}'"));
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM information_schema.tables WHERE table_name IN ({list})")).ShouldBe(9);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context, "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_PurchasingOrders_AwardId'"))
            .ShouldContain("UNIQUE");
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context, "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_PurchasingOrders_Number'"))
            .ShouldContain("WHERE (\"Number\" IS NOT NULL)");
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM pg_constraint WHERE conname IN ('CK_ReceivingReceiptLines_Quantities', 'CK_ReceivingReceiptLines_Discrepancy', 'CK_PurchasingOrders_IssuedNumber')"))
            .ShouldBe(3);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, "SELECT count(*) FROM pg_class WHERE relkind = 'S' AND relname ILIKE '%purchas%'")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'PurchaseRequisitions' AND column_name = 'PurchaseOrderId'")).ShouldBe("YES");

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Phase31Migration);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM information_schema.tables WHERE table_name IN ({list})")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'PurchaseRequisitions' AND column_name = 'PurchaseOrderId'")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('ApprovalInstances', 'SourcingAwards', 'ReportingReportJobs')")).ShouldBe(3);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task T_PUR_001_a_ConcurrentConsumers_CreateExactlyOneDraftPerAward()
    {
        var (award, requisitionId) = await ApprovedAsync("10", "500");
        var completed = await ApprovalCompletedAsync(award);

        var deliveries = new[] { completed, completed, completed with { EventId = Guid.NewGuid() }, completed with { EventId = Guid.NewGuid() } };
        var results = await Task.WhenAll(deliveries.Select(envelope => Task.Run(() => DeliverAsync(envelope))));

        results.Count(result => result == InboxApplyResult.Applied).ShouldBeGreaterThanOrEqualTo(1);
        await using var context = fixture.CreateContext();
        (await context.Set<PurchaseOrder>().CountAsync(order => order.AwardId == award)).ShouldBe(1);
        var order = await context.Set<PurchaseOrder>().Include(item => item.Lines).SingleAsync(item => item.AwardId == award);
        (order.Status, order.Total, order.Lines.Single().Quantity).ShouldBe((PurchaseOrderStatus.Draft, 5_000m, 10m));
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisitionId)).PurchaseOrderId.ShouldBe(order.Id);
        (await context.Set<OutboxMessage>().CountAsync(message => message.AggregateId == order.Id && message.EventType == "procurement.purchase-order.drafted"))
            .ShouldBe(1);
        (await context.Set<InboxMessage>().CountAsync(message => message.ConsumerName == ProcurementConsumers.Purchasing && message.EventId == completed.EventId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task T_PUR_005_a_ParallelIssues_GetUniqueGaplessNumbers_AndARollbackReturnsTheNumber()
    {
        var orders = new List<PurchaseOrderView>();
        for (var index = 0; index < 4; index++)
            orders.Add(await DraftWithAddressAsync());

        actor.Current = buyer;
        var issued = await Task.WhenAll(orders.Concat([orders[0]]).Select(order => Task.Run(() =>
            Scoped<PurchaseOrderHandlers, Result<PurchaseOrderView>>(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, Version(order.Version)), default)))));

        issued.ShouldAllBe(result => result.IsSuccess);
        var numbers = issued.Take(4).Select(result => result.Value.Number!).ToArray();
        numbers.Order(StringComparer.Ordinal).ShouldBe(Enumerable.Range(1, 4).Select(sequence => PurchaseOrder.FormatNumber(year, sequence)).ToArray());
        issued[4].Value.Number.ShouldBe(issued[0].Value.Number);
        await using var context = fixture.CreateContext();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT \"LastValue\" FROM \"PurchasingOrderNumberCounters\" WHERE \"Year\" = {year}"))
            .ShouldBe(4);

        // SP-2: a number allocated inside a transaction that rolls back is handed out again.
        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IPurchaseOrderNumberAllocator>().NextAsync(year, default)).ShouldBe(5);
            await unitOfWork.RollbackTransactionAsync();
        }

        var fifth = await DraftWithAddressAsync();
        actor.Current = buyer;
        (await Scoped<PurchaseOrderHandlers, Result<PurchaseOrderView>>(handlers =>
            handlers.Handle(new IssuePurchaseOrderCommand(fifth.Id, Version(fifth.Version)), default))).Value.Number.ShouldBe(PurchaseOrder.FormatNumber(year, 5));
        await using (var scope = provider.CreateAsyncScope())
            await Should.ThrowAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IPurchaseOrderNumberAllocator>().NextAsync(year, default));
    }

    [Fact]
    public async Task T_RCV_003_c_ConcurrentReceipts_SerializeOnTheOrderRow_AndNeverExceedTheCap()
    {
        var order = await IssuedAsync();

        actor.Current = receiver;
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => ReceiveAsync(order.Id, "4"))));

        results.Count(result => result.IsSuccess).ShouldBe(2);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == PurchasingErrors.OverReceiptCode);
        await using var context = fixture.CreateContext();
        var stored = await context.Set<PurchaseOrder>().Include(item => item.Lines).SingleAsync(item => item.Id == order.Id);
        (stored.Status, stored.Lines.Single().AcceptedQuantity, stored.Lines.Single().ReceivedQuantity).ShouldBe((PurchaseOrderStatus.PartiallyReceived, 8m, 8m));
        (await context.Set<Receipt>().CountAsync(receipt => receipt.PurchaseOrderId == order.Id)).ShouldBe(2);
        (await context.Set<Receipt>().Where(receipt => receipt.PurchaseOrderId == order.Id).Select(receipt => receipt.Number).Distinct().CountAsync()).ShouldBe(2);

        var final = await ReceiveAsync(order.Id, "2");
        final.Value.PurchaseOrder.Status.ShouldBe("Completed");
        var receipt = await context.Set<Receipt>().AsNoTracking().FirstAsync(item => item.PurchaseOrderId == order.Id);
        var corrected = await Scoped<ReceiptHandlers, Result<ReceiptPostingView>>(handlers =>
            handlers.Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version, 1, "-1", Reason), default));
        corrected.IsSuccess.ShouldBeTrue(corrected.Error?.Message);
        corrected.Value.PurchaseOrder.Status.ShouldBe("PartiallyReceived");
        await using var reread = fixture.CreateContext();
        (await reread.Set<ReceiptCorrection>().CountAsync(item => item.ReceiptId == receipt.Id)).ShouldBe(1);
        (await reread.Set<PurchaseRequisition>().SingleAsync(item => item.Id == order.RequisitionId)).Status.ShouldBe(RequisitionStatus.Ordered);
    }

    [Fact]
    public async Task T_PUR_008_b_ConcurrentIssuedCancels_ReleaseTheCommitmentOnce()
    {
        var order = await IssuedAsync();
        actor.Current = buyer;
        await using var context = fixture.CreateContext();
        var current = await context.Set<PurchaseOrder>().SingleAsync(item => item.Id == order.Id);

        var cancels = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            Scoped<PurchaseOrderHandlers, Result<PurchaseOrderView>>(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, current.Version, Reason), default)))));
        cancels.Count(result => result.IsSuccess).ShouldBeGreaterThanOrEqualTo(1);
        await using var reread = fixture.CreateContext();
        (await reread.Set<PurchaseOrder>().SingleAsync(item => item.Id == order.Id)).Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        var reservation = await reread.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == order.RequisitionId);
        (reservation.Status, reservation.ReleaseReason).ShouldBe((BudgetReservationStatus.Released, BudgetReleaseReason.OrderCancelled));
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Released).ShouldBe(1);
        (await reread.Set<OutboxMessage>().CountAsync(message => message.AggregateId == order.Id && message.EventType == "procurement.purchase-order.cancelled"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task T_PUR_010_b_ShortClose_PersistsThePartialReleaseAndTheShortClosedLines()
    {
        var order = await IssuedAsync();
        actor.Current = receiver;
        (await ReceiveAsync(order.Id, "3")).IsSuccess.ShouldBeTrue();
        await using var before = fixture.CreateContext();
        var version = (await before.Set<PurchaseOrder>().SingleAsync(item => item.Id == order.Id)).Version;

        actor.Current = manager;
        var closed = await Scoped<PurchaseOrderHandlers, Result<PurchaseOrderView>>(handlers =>
            handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, version, Reason), default));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);

        await using var context = fixture.CreateContext();
        var stored = await context.Set<PurchaseOrder>().Include(item => item.Lines).SingleAsync(item => item.Id == order.Id);
        (stored.Status, stored.ShortClosed, stored.ShortCloseReleasedAmount, stored.Lines.Single().IsShortClosed).ShouldBe((PurchaseOrderStatus.Completed, true, 3_500m, true));
        var allocation = await context.Set<BudgetAllocation>().SingleAsync(item => item.CostCenterId == costCenterId);
        allocation.CommittedAmount.ShouldBe(1_500m);
        var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == order.RequisitionId);
        (reservation.Status, reservation.Amount).ShouldBe((BudgetReservationStatus.Committed, 1_500m));
        reservation.Changes.Single(change => change.Operation == BudgetReservationOperation.PartiallyReleased).Reason.ShouldBe(BudgetReleaseReason.ShortClosed);
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == order.RequisitionId)).Status.ShouldBe(RequisitionStatus.Closed);
    }

    private async Task<PurchaseOrderView> IssuedAsync()
    {
        var draft = await DraftWithAddressAsync();
        actor.Current = buyer;
        var issued = await Scoped<PurchaseOrderHandlers, Result<PurchaseOrderView>>(handlers =>
            handlers.Handle(new IssuePurchaseOrderCommand(draft.Id, Version(draft.Version)), default));
        issued.IsSuccess.ShouldBeTrue(issued.Error?.Message);
        return issued.Value;
    }

    private async Task<PurchaseOrderView> DraftWithAddressAsync()
    {
        var (award, _) = await ApprovedAsync("10", "500");
        (await DeliverAsync(await ApprovalCompletedAsync(award))).ShouldBe(InboxApplyResult.Applied);
        await using var context = fixture.CreateContext();
        var order = await context.Set<PurchaseOrder>().AsNoTracking().SingleAsync(item => item.AwardId == award);
        actor.Current = buyer;
        var updated = await Scoped<PurchaseOrderHandlers, Result<PurchaseOrderView>>(handlers =>
            handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, "Rua Sintética 100, São Paulo", null, null), default));
        updated.IsSuccess.ShouldBeTrue(updated.Error?.Message);
        return updated.Value;
    }

    private Task<Result<ReceiptPostingView>> ReceiveAsync(Guid orderId, string quantity) =>
        Scoped<ReceiptHandlers, Result<ReceiptPostingView>>(handlers => handlers.Handle(new PostReceiptCommand(
            orderId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"))), "Dock",
            null, null, [new ReceiptLineRequest(1, quantity, quantity, "0", null, null)], null), default));

    private async Task<InboxApplyResult> DeliverAsync(IntegrationEventEnvelope envelope)
    {
        actor.Current = null;
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PurchasingEventHandler>().HandleAsync(envelope, default);
    }

    private async Task<IntegrationEventEnvelope> ApprovalCompletedAsync(Guid award)
    {
        await using var context = fixture.CreateContext();
        var rows = await context.Set<OutboxMessage>().AsNoTracking().Where(message => message.EventType == "procurement.approval.completed").ToListAsync();
        var row = rows.Single(message => message.PayloadJson.Contains(award.ToString(), StringComparison.OrdinalIgnoreCase));
        using var document = System.Text.Json.JsonDocument.Parse(row.PayloadJson);
        return new IntegrationEventEnvelope(row.EventId, row.EventType, row.EventVersion, DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
            row.CorrelationId, row.CausationId, row.AggregateType, row.AggregateId, row.AggregateVersion, document.RootElement.Clone());
    }

    /// <summary>Single-level approval (≤ 10,000.00 BRL) of a one-supplier competitive RFQ, finalized through the activities.</summary>
    private async Task<(Guid AwardId, Guid RequisitionId)> ApprovedAsync(string quantity, string unitPrice)
    {
        clock.UtcNow = Now;
        actor.Current = requester;
        await OpenBudgetAsync(1_000_000m);
        var requisition = await SubmittedAsync(quantity, unitPrice);
        actor.Current = buyer;
        var supplier = await ActiveSupplierAsync($"PO Supplier {Guid.NewGuid():N}"[..30]);
        var rfq = await RfqAsync(requisition, supplier, quantity, unitPrice);
        var awarded = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers => handlers.Handle(
            new SubmitAwardCommand(rfq.Id, Version(rfq.Version), rfq.Comparison!.RecommendedQuoteRevisionId!.Value, null, null, null), default));
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        var award = awarded.Value.Awards.Single().Id;

        actor.Current = null;
        await Scoped<ApprovalWorkflowActivities, ApprovalProgress>(activities => activities.StartAsync(
            new ApprovalStartRequest(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, award, requisition.Id, clock.UtcNow, "pg-purchasing"), "1.0"), default));
        await Scoped<ApprovalWorkflowActivities, ApprovalProgress>(activities => activities.AssignStepAsync(new ApprovalActivityRequest(award, "pg"), default));
        await using (var context = fixture.CreateContext())
        {
            var instance = await context.Set<Template.Domain.Approvals.ApprovalInstance>().SingleAsync(item => item.AwardId == award);
            actor.Current = manager;
            (await Scoped<ApprovalDecisionHandlers, Result<ApprovalInstanceView>>(handlers =>
                handlers.Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default))).IsSuccess.ShouldBeTrue();
        }

        actor.Current = null;
        (await Scoped<ApprovalWorkflowActivities, ApprovalProgress>(activities => activities.FinalizeAsync(new ApprovalActivityRequest(award, "pg"), default)))
            .Status.ShouldBe("Approved");
        return (award, requisition.Id);
    }

    private async Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<RfqView> RfqAsync(RequisitionView requisition, Guid supplier, string quantity, string unitPrice)
    {
        var created = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers => handlers.Handle(new CreateRfqCommand(requisition.Id, null), default));
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        var invited = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new InviteSupplierCommand(created.Value.Id, Version(created.Value.Version), supplier), default));
        var published = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new PublishRfqCommand(invited.Value.Id, Version(invited.Value.Version), Now.AddDays(3), null), default));
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        var quote = new QuoteRequest("BRL", [new QuoteLineRequest(1, quantity, unitPrice)], null, null, null, new DateOnly(year, 5, 31), 7, "30 days", null,
            QuoteSource.Email, Now, []);
        var entered = await Scoped<QuoteCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new EnterQuoteCommand(published.Value.Id, Version(published.Value.Version), supplier, quote), default));
        entered.IsSuccess.ShouldBeTrue(entered.Error?.Message);
        clock.UtcNow = Now.AddDays(3);
        var closed = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new CloseRfqCommand(entered.Value.Id, Version(entered.Value.Version), null), default));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        return closed.Value;
    }

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

    private async Task OpenBudgetAsync(decimal amount)
    {
        await using var context = fixture.CreateContext();
        if (await context.Set<BudgetAllocation>().AnyAsync(item => item.CostCenterId == costCenterId))
            return;
        await using var scope = provider.CreateAsyncScope();
        var periods = scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>();
        var period = await periods.Handle(new CreateFiscalPeriodCommand(Code("FY"), "Synthetic fiscal year", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)), default);
        period.IsSuccess.ShouldBeTrue(period.Error?.Message);
        (await periods.Handle(new ChangeFiscalPeriodStatusCommand(period.Value.Id, 1, FiscalPeriodTransition.Open), default)).IsSuccess.ShouldBeTrue();
        var allocation = await scope.ServiceProvider.GetRequiredService<BudgetAllocationCommandHandlers>().Handle(
            new CreateBudgetAllocationCommand(period.Value.Id, costCenterId, "BRL", amount.ToString(CultureInfo.InvariantCulture)), default);
        allocation.IsSuccess.ShouldBeTrue(allocation.Error?.Message);
    }

    private async Task<RequisitionView> SubmittedAsync(string quantity, string price)
    {
        await using var scope = provider.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>();
        var draft = await drafts.Handle(new CreateRequisitionDraftCommand(costCenterId, "BRL", "Synthetic purpose for purchasing", new DateOnly(year, 6, 30)), default);
        draft.IsSuccess.ShouldBeTrue(draft.Error?.Message);
        var line = await drafts.Handle(new AddRequisitionLineCommand(draft.Value.Id, Version(draft.Value.Version),
            new RequisitionLineRequest(null, "Synthetic line", RequisitionLineKind.Good, categoryId, unitId, quantity, new MoneyInput(price, "BRL"))), default);
        line.IsSuccess.ShouldBeTrue(line.Error?.Message);
        var submitted = await scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>()
            .Handle(new SubmitRequisitionCommand(line.Value.Id, Version(line.Value.Version)), default);
        submitted.IsSuccess.ShouldBeTrue(submitted.Error?.Message);
        return submitted.Value;
    }

    private static long Version(string version) => long.Parse(version, CultureInfo.InvariantCulture);

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

    /// <summary>The acting user per async flow, so parallel tasks act as different users.</summary>
    private sealed class SwitchableActor : IAuthenticatedUserService
    {
        private readonly AsyncLocal<Guid?> current = new();

        public Guid? Current
        {
            get => current.Value;
            set => current.Value = value;
        }

        public string? UserId => current.Value?.ToString();
        public string? UserName => current.Value is null ? null : "pg-purchasing";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId => "pg-purchasing";
    }

    private sealed class Approvers : IApproverDirectory
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> users = new();

        public void Add(Guid user, string role) => users[user] = role;

        public Task<ApprovalCandidate?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(users.ContainsKey(userId) ? new ApprovalCandidate(userId, true, true) : null);

        public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(users.TryGetValue(userId, out var role) ? new HashSet<string> { role } : new HashSet<string>());

        public Task<PagedResult<UserSummary>> SearchApproversAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], page.Page, page.PageSize, 0));

        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Synthetic Buyer"));
    }

    private sealed class RunningWorkflow : IApprovalWorkflowClient
    {
        public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowStartResult.Started);

        public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowSignalResult.Delivered);

        public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalWorkflowStatus(ApprovalWorkflowNames.InstanceIdFor(awardId), ApprovalWorkflowRuntimeStatus.Running, "1.0", null, null));

        public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken) => Task.FromResult(true);
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
        private static readonly IReadOnlySet<string> Granted = SeededRoles.PermissionsByRole.Values.SelectMany(permissions => permissions)
            .ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Granted);
    }

    private sealed class NoRates : IExchangeRateProvider
    {
        public Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
            string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BcbPtax, "synthetic"));
    }
}
