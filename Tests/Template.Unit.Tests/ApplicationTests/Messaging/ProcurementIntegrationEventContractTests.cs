using System.Text;
using Shouldly;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Application.Services.Procurement;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Requisitions;

namespace Template.Unit.Tests.ApplicationTests.Messaging;

public sealed class ProcurementIntegrationEventContractTests
{
    private static readonly DateTime Occurred = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SubmittedV1_HasStableMinimalEnvelopeAndRoundTrips()
    {
        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var requisitionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var costCenterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var envelope = ProcurementIntegrationEventContract.FromDomainEvent(
            new RequisitionSubmitted(Occurred, requisitionId, "REQ-2027-000001", 1, costCenterId,
                new Money(246.91m, "BRL"), new DateOnly(2027, 4, 1)),
            "trace-28", eventId: eventId);

        var json = Encoding.UTF8.GetString(ProcurementIntegrationEventContract.Serialize(envelope));
        json.ShouldBe("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"eventType\":\"procurement.requisition.submitted\",\"eventVersion\":1,\"occurredAtUtc\":\"2027-03-01T13:00:00Z\",\"correlationId\":\"trace-28\",\"aggregateType\":\"PurchaseRequisition\",\"aggregateId\":\"22222222-2222-2222-2222-222222222222\",\"aggregateVersion\":\"1\",\"payload\":{\"requisitionId\":\"22222222-2222-2222-2222-222222222222\",\"number\":\"REQ-2027-000001\",\"revision\":1,\"costCenterId\":\"33333333-3333-3333-3333-333333333333\",\"estimatedTotal\":{\"amount\":246.91,\"currency\":\"BRL\"},\"neededBy\":\"2027-04-01\"}}");
        var roundTrip = ProcurementIntegrationEventContract.Deserialize(ProcurementIntegrationEventContract.Serialize(envelope));
        roundTrip.EventId.ShouldBe(envelope.EventId);
        roundTrip.EventType.ShouldBe(envelope.EventType);
        roundTrip.Payload.GetRawText().ShouldBe(envelope.Payload.GetRawText());
        json.ToLowerInvariant().ShouldNotContain("purpose");
        json.ToLowerInvariant().ShouldNotContain("description");
    }

    [Fact]
    public async Task Handler_ValidatesThenDelegatesToTheAtomicEffectPort()
    {
        var envelope = ProcurementIntegrationEventContract.FromDomainEvent(
            new RequisitionExpired(Occurred, Guid.NewGuid(), 2), "trace-28");
        var effects = new Effects();
        var handler = new ProcurementIntegrationEventHandler(effects, new Clock());

        (await handler.HandleAsync("projection-v1", envelope, CancellationToken.None)).ShouldBe(InboxApplyResult.Applied);
        effects.Calls.ShouldBe(1);
        effects.LastEventId.ShouldBe(envelope.EventId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(4, 16)]
    [InlineData(20, 300)]
    [InlineData(50, 300)]
    public void DispatchPolicy_UsesBoundedExponentialBackoff(int previousFailures, int expectedSeconds)
    {
        var next = OutboxDispatchPolicy.NextAttemptAtUtc(
            Occurred, previousFailures, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));

        next.ShouldBe(Occurred.AddSeconds(expectedSeconds));
    }

    [Fact]
    public void EveryApprovedCatalogEvent_MapsToItsStableV1Type()
    {
        var requisitionId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        DomainEvent[] events =
        [
            new RequisitionSubmitted(Occurred, requisitionId, "REQ-2027-000002", 1, Guid.NewGuid(),
                new Money(10m, "BRL"), new DateOnly(2027, 4, 1)),
            new RequisitionWithdrawn(Occurred, requisitionId, 2),
            new RequisitionCancelled(Occurred, requisitionId, 2, Guid.NewGuid()),
            new RequisitionExpired(Occurred, requisitionId, 2),
            new BudgetReservationCreated(Occurred, reservationId, requisitionId, Guid.NewGuid(), new Money(10m, "BRL")),
            new BudgetReservationCommitted(Occurred, reservationId, requisitionId, new Money(10m, "BRL")),
            new BudgetReservationReleased(Occurred, reservationId, requisitionId, new Money(10m, "BRL"), BudgetReleaseReason.Cancelled)
        ];

        var envelopes = events.Select(value =>
            ProcurementIntegrationEventContract.FromDomainEvent(value, new string('c', 200))).ToArray();

        envelopes.Select(value => value.EventType).ToHashSet(StringComparer.Ordinal)
            .SetEquals(ProcurementIntegrationEventContract.SupportedEventTypes).ShouldBeTrue();
        envelopes.ShouldAllBe(value => value.CorrelationId.Length == 128 && value.EventVersion == 1);
    }

    [Fact]
    public void ContractAndDispatchPolicy_RejectInvalidInputs()
    {
        Should.Throw<NotSupportedException>(() =>
            ProcurementIntegrationEventContract.FromDomainEvent(new UnsupportedEvent(Occurred), "trace-28"));
        Should.Throw<ArgumentException>(() =>
            ProcurementIntegrationEventContract.FromDomainEvent(new RequisitionExpired(Occurred, Guid.NewGuid(), 1), " "));
        Should.Throw<ArgumentException>(() =>
            ProcurementIntegrationEventContract.FromDomainEvent(
                new RequisitionExpired(DateTime.SpecifyKind(Occurred, DateTimeKind.Unspecified), Guid.NewGuid(), 1), "trace-28"));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            OutboxDispatchPolicy.NextAttemptAtUtc(Occurred, -1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            OutboxDispatchPolicy.NextAttemptAtUtc(Occurred, 0, TimeSpan.Zero, TimeSpan.FromSeconds(2)));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            OutboxDispatchPolicy.NextAttemptAtUtc(Occurred, 0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }

    private sealed class Effects : IProcurementEventEffectStore
    {
        public int Calls { get; private set; }
        public Guid LastEventId { get; private set; }
        public Task<InboxApplyResult> ApplyOnceAsync(string consumerName, IntegrationEventEnvelope envelope, DateTime processedAtUtc, CancellationToken cancellationToken)
        {
            Calls++;
            LastEventId = envelope.EventId;
            return Task.FromResult(InboxApplyResult.Applied);
        }
    }

    private sealed class Clock : IClock { public DateTime UtcNow => Occurred.AddMinutes(1); }
    private sealed record UnsupportedEvent(DateTime AtUtc) : DomainEvent(AtUtc)
    {
        public override string EventType => "unsupported";
    }
}
