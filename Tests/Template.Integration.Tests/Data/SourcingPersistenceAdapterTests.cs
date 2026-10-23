using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Messaging.Procurement;
using Template.Domain.Sourcing;
using Template.Infrastructure.Persistence;

namespace Template.Integration.Tests.Data;

/// <summary>
/// Phase 29 sourcing adapters on EF InMemory through the real Persistence registration: the RFQ aggregate round trip with
/// every owned collection (invitation snapshots, revisions, comparison, history), list filters, the SRC-012 due-for-close
/// query, number allocation and the in-memory branch of the notification Inbox. Constraints, locks and concurrency are
/// PostgreSQL semantics covered by the PostgreSQL suite.
/// </summary>
public sealed class SourcingPersistenceAdapterTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private readonly ServiceProvider provider;

    public SourcingPersistenceAdapterTests()
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
    public async Task Rfq_RoundTrips_WithSnapshotsRevisionsComparisonAndHistory()
    {
        var rfq = ClosedRfq(out var supplier);
        await SaveAsync(scope => scope.GetRequiredService<IRequestForQuotationRepository>().Add(rfq));

        await using var read = provider.CreateAsyncScope();
        var repository = read.ServiceProvider.GetRequiredService<IRequestForQuotationRepository>();
        var stored = (await repository.FindAsync(rfq.Id, CancellationToken.None))!;

        stored.Status.ShouldBe(RfqStatus.Closed);
        stored.Lines.Single().Quantity.ShouldBe(10m);
        stored.Invitations.Single().SupplierLegalName.ShouldBe(supplier.LegalName);
        stored.Quotes.Single().Revisions.Single().Lines.Single().LineTotal.ShouldBe(1000m);
        stored.CurrentComparison!.Entries.Single().Rank.ShouldBe(1);
        stored.History.Select(entry => entry.Action).ShouldBe(["created", "supplier-invited", "published", "quote-recorded", "closed"]);
        (await repository.GetVersionAsync(rfq.Id, CancellationToken.None)).ShouldBe(rfq.Version);
        (await repository.FindActiveByRequisitionAsync(rfq.RequisitionId, CancellationToken.None))!.Id.ShouldBe(rfq.Id);
        (await repository.CountRoundsAsync(rfq.RequisitionId, CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task List_FiltersByStatusRequisitionNumberAndInvitedSupplier_AndFindsDueRfqs()
    {
        var closed = ClosedRfq(out var supplier);
        var published = PublishedRfq(out _);
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IRequestForQuotationRepository>().Add(closed);
            scope.GetRequiredService<IRequestForQuotationRepository>().Add(published);
        });

        await using var read = provider.CreateAsyncScope();
        var repository = read.ServiceProvider.GetRequiredService<IRequestForQuotationRepository>();
        var page = PageRequest.Create(1, 20, "-createdAt", IRequestForQuotationRepository.SortFields, "-createdAt").Value;
        (await repository.ListAsync(new RfqFilter([RfqStatus.Published]), page, CancellationToken.None)).Items.Single().Id.ShouldBe(published.Id);
        (await repository.ListAsync(new RfqFilter(SupplierId: supplier.SupplierId), page, CancellationToken.None)).Items.Single().Id.ShouldBe(closed.Id);
        (await repository.ListAsync(new RfqFilter(Number: "REQ-2027"), page, CancellationToken.None)).TotalItems.ShouldBe(2);
        (await repository.ListDueForCloseAsync(Now.AddDays(1), 10, CancellationToken.None)).ShouldBeEmpty();
        (await repository.ListDueForCloseAsync(Now.AddDays(3), 10, CancellationToken.None)).ShouldBe([published.Id]);
    }

    [Fact]
    public async Task NumberAllocator_AndNotificationInbox_WorkWithoutARelationalProvider()
    {
        await using var scope = provider.CreateAsyncScope();
        var numbers = scope.ServiceProvider.GetRequiredService<IRfqNumberAllocator>();
        var first = await numbers.NextAsync(3027, CancellationToken.None);
        (await numbers.NextAsync(3027, CancellationToken.None)).ShouldBe(first + 1);

        var envelope = ProcurementIntegrationEventContract.FromDomainEvent(
            new RfqPublished(Now, Guid.NewGuid(), "RFQ-2027-000001", Guid.NewGuid(), Now.AddDays(3), [Guid.NewGuid()], SourcingMethod.Competitive), "integration");
        var store = scope.ServiceProvider.GetRequiredService<INotificationDeliveryStore>();
        var requests = RfqNotificationPlanner.Plan(envelope);
        (await store.DeliverOnceAsync(ProcurementConsumers.Notification, envelope, requests, Now, CancellationToken.None)).ShouldBe(InboxApplyResult.Applied);
        (await store.DeliverOnceAsync(ProcurementConsumers.Notification, envelope, requests, Now, CancellationToken.None)).ShouldBe(InboxApplyResult.Duplicate);
    }

    public async ValueTask DisposeAsync() => await provider.DisposeAsync();

    private static RequestForQuotation PublishedRfq(out SupplierSnapshot supplier)
    {
        var rfq = RequestForQuotation.Create(Guid.NewGuid(), $"RFQ-2027-{Random.Shared.Next(1, 999_999):D6}", 1, SourcingMethod.Competitive,
            new RequisitionSourcingSnapshot(Guid.NewGuid(), "REQ-2027-000001", 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", 1000m,
                new DateOnly(2027, 4, 1), [new RfqLineSnapshot(1, null, "Laptop", "Good", Guid.NewGuid(), "IT", Guid.NewGuid(), "UN", 10m, 100m, 1000m)]),
            "BRL", null, SourcingPolicy.V1, null, Now, "integration").Rfq!;
        supplier = new SupplierSnapshot(Guid.NewGuid(), "Synthetic Supplier Ltda", "BR", "11222333000181", "Active", true);
        rfq.Invite(supplier, null, Now, "integration").Error.ShouldBeNull();
        rfq.Publish(Now.AddDays(3), new Dictionary<Guid, bool> { [supplier.SupplierId] = true }, null, SourcingPolicy.V1, null, Now, "integration").Error.ShouldBeNull();
        return rfq;
    }

    private static RequestForQuotation ClosedRfq(out SupplierSnapshot supplier)
    {
        var rfq = PublishedRfq(out supplier);
        rfq.EnterQuote(supplier.SupplierId, new QuoteInput("BRL", true, [new QuoteLineInput(1, 10m, 100m)], 0m, 0m, 0m, new DateOnly(2027, 4, 30), 5,
            "30 days", null, QuoteSource.Email, Now, []), new DateOnly(2027, 3, 4), null, Now, "integration").Error.ShouldBeNull();
        rfq.Close(null, new Dictionary<Guid, bool> { [supplier.SupplierId] = true }, [], new DateOnly(2027, 3, 4), SourcingPolicy.V1, null, Now.AddDays(3), "integration")
            .Error.ShouldBeNull();
        return rfq;
    }

    private async Task SaveAsync(Action<IServiceProvider> arrange)
    {
        await using var scope = provider.CreateAsyncScope();
        arrange(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }
}
