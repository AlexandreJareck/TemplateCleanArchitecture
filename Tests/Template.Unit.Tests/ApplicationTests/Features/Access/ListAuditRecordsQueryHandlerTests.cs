using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Features.Auditing.Queries;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Auditing;

namespace Template.Unit.Tests.ApplicationTests.Features.Access;

/// <summary>
/// F-06: the audit query adds the actor's display name (never an email) from one batched directory lookup; system
/// records and actors missing from the directory keep a <c>null</c> name, and an invalid request never reaches the
/// directory. All data is synthetic.
/// </summary>
public sealed class ListAuditRecordsQueryHandlerTests
{
    private static readonly DateTime At = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private readonly Guid known = Guid.NewGuid();
    private readonly Guid unknown = Guid.NewGuid();

    [Fact]
    public async Task Handle_NamesKnownActors_InOneLookup_AndLeavesSystemAndUnknownActorsUnnamed()
    {
        var reader = new FakeReader([Record(known), Record(known), Record(null), Record(unknown)]);
        var names = new FakeNames(new Dictionary<Guid, string> { [known] = "Synthetic Auditee" });

        var result = await new ListAuditRecordsQueryHandler(reader, names).Handle(new ListAuditRecordsQuery(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(item => item.ActorName).ShouldBe(["Synthetic Auditee", "Synthetic Auditee", null, null]);
        result.Value.TotalItems.ShouldBe(4);
        names.Lookups.Single().ShouldBe([known, unknown], ignoreOrder: true);
    }

    [Fact]
    public async Task Handle_AnEmptyPage_OrAnInvalidRequest_NeverReachesTheDirectory()
    {
        var names = new FakeNames(new Dictionary<Guid, string>());

        (await new ListAuditRecordsQueryHandler(new FakeReader([]), names).Handle(new ListAuditRecordsQuery(), default)).IsSuccess.ShouldBeTrue();
        (await new ListAuditRecordsQueryHandler(new FakeReader([Record(known)]), names)
            .Handle(new ListAuditRecordsQuery(From: At, To: At.AddHours(-1)), default)).IsSuccess.ShouldBeFalse();

        names.Lookups.ShouldBeEmpty();
    }

    private static AuditRecordView Record(Guid? actorId) => new(
        Guid.NewGuid(), At, actorId, "requisition.submitted", "purchase-requisition", Guid.NewGuid().ToString(), "Draft", "Submitted", ["REQ-004"],
        null, "corr-synthetic", new Dictionary<string, string>());

    private sealed class FakeReader(IReadOnlyList<AuditRecordView> items) : IAuditRecordReader
    {
        public Task<PagedResult<AuditRecordView>> ListAsync(AuditRecordFilter filter, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<AuditRecordView>(items, page.Page, page.PageSize, items.Count));
    }

    private sealed class FakeNames(IReadOnlyDictionary<Guid, string> names) : IUserDisplayNames
    {
        public List<IReadOnlyCollection<Guid>> Lookups { get; } = [];

        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
        {
            Lookups.Add(userIds);
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Where(names.ContainsKey).ToDictionary(id => id, id => names[id]));
        }
    }
}
