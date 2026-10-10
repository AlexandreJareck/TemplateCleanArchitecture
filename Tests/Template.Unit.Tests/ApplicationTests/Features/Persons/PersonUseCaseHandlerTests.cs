using Moq;
using Shouldly;
using Template.Application.Features.Persons.Commands;
using Template.Application.Features.Persons.Queries;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Unit.Tests.ApplicationTests.Features.Persons;

public class PersonUseCaseHandlerTests
{
    private readonly Mock<IPersonSearchIndex> searchIndex = new(MockBehavior.Strict);
    private readonly CancellationTokenSource cancellation = new();

    [Fact]
    public async Task EnsureIndex_CallsThePortWithTheIndexNameAndToken()
    {
        searchIndex.Setup(port => port.EnsureIndexAsync("people-v1", cancellation.Token)).ReturnsAsync(PersonIndexResult.Success());

        var result = await new EnsurePersonIndexCommandHandler(searchIndex.Object)
            .Handle(new EnsurePersonIndexCommand("people-v1"), cancellation.Token);

        result.Succeeded.ShouldBeTrue();
        searchIndex.Verify(port => port.EnsureIndexAsync("people-v1", cancellation.Token), Times.Once);
        searchIndex.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IndexPerson_MapsTheCommandToTheIndexedPerson_AndUpserts()
    {
        IndexedPerson? indexed = null;
        searchIndex.Setup(port => port.UpsertAsync(It.IsAny<IndexedPerson>(), cancellation.Token))
            .Callback<IndexedPerson, CancellationToken>((person, _) => indexed = person)
            .ReturnsAsync(PersonIndexResult.Success());

        var result = await new IndexPersonCommandHandler(searchIndex.Object)
            .Handle(new IndexPersonCommand(5, "Eva", null), cancellation.Token);

        result.Succeeded.ShouldBeTrue();
        indexed.ShouldBe(new IndexedPerson(5, "Eva", null));
        searchIndex.Verify(port => port.UpsertAsync(It.IsAny<IndexedPerson>(), cancellation.Token), Times.Once);
        searchIndex.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IndexPerson_UpdateUsesTheSameUpsert_SoAnExistingPersonIsReplaced()
    {
        searchIndex.SetupSequence(port => port.UpsertAsync(It.IsAny<IndexedPerson>(), cancellation.Token))
            .ReturnsAsync(PersonIndexResult.Success())
            .ReturnsAsync(PersonIndexResult.Success());
        var handler = new IndexPersonCommandHandler(searchIndex.Object);

        await handler.Handle(new IndexPersonCommand(5, "Eva", null), cancellation.Token);
        await handler.Handle(new IndexPersonCommand(5, "Eva", "Lima"), cancellation.Token);

        searchIndex.Verify(port => port.UpsertAsync(new IndexedPerson(5, "Eva", null), cancellation.Token), Times.Once);
        searchIndex.Verify(port => port.UpsertAsync(new IndexedPerson(5, "Eva", "Lima"), cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetPerson_ReturnsThePerson()
    {
        var person = new IndexedPerson(5, "Eva", "Lima");
        searchIndex.Setup(port => port.GetAsync("5", cancellation.Token)).ReturnsAsync(PersonIndexResult<IndexedPerson>.Success(person));

        var result = await new GetPersonQueryHandler(searchIndex.Object).Handle(new GetPersonQuery("5"), cancellation.Token);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.Value.ShouldBe(person);
    }

    [Theory]
    [InlineData(PersonIndexStatus.NotFound)]
    [InlineData(PersonIndexStatus.Unavailable)]
    [InlineData(PersonIndexStatus.Rejected)]
    [InlineData(PersonIndexStatus.IncompatibleDocument)]
    public async Task GetPerson_KeepsMissingAndUnavailableApart(PersonIndexStatus status)
    {
        searchIndex.Setup(port => port.GetAsync("5", cancellation.Token)).ReturnsAsync(PersonIndexResult<IndexedPerson>.Failure(status));

        var result = await new GetPersonQueryHandler(searchIndex.Object).Handle(new GetPersonQuery("5"), cancellation.Token);

        result.Status.ShouldBe(status);
        result.Succeeded.ShouldBeFalse();
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task ListPersons_ReturnsTheIndexPage()
    {
        IReadOnlyList<IndexedPerson> persons = [new IndexedPerson(1, "A", null), new IndexedPerson(2, null, "B")];
        searchIndex.Setup(port => port.ListAsync(cancellation.Token)).ReturnsAsync(PersonIndexResult<IReadOnlyList<IndexedPerson>>.Success(persons));

        var result = await new ListPersonsQueryHandler(searchIndex.Object).Handle(new ListPersonsQuery(), cancellation.Token);

        result.Value.ShouldBe(persons);
        searchIndex.Verify(port => port.ListAsync(cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ListPersons_Failure_HasNoValue()
    {
        searchIndex.Setup(port => port.ListAsync(cancellation.Token))
            .ReturnsAsync(PersonIndexResult<IReadOnlyList<IndexedPerson>>.Failure(PersonIndexStatus.Unavailable));

        var result = await new ListPersonsQueryHandler(searchIndex.Object).Handle(new ListPersonsQuery(), cancellation.Token);

        result.Status.ShouldBe(PersonIndexStatus.Unavailable);
        result.Value.ShouldBeNull();
    }

    [Theory]
    [InlineData(PersonIndexStatus.Succeeded)]
    [InlineData(PersonIndexStatus.NotFound)]
    [InlineData(PersonIndexStatus.Unavailable)]
    public async Task RemovePerson_CallsThePortWithTheKey_AndReturnsItsOutcome(PersonIndexStatus status)
    {
        var outcome = status == PersonIndexStatus.Succeeded ? PersonIndexResult.Success() : PersonIndexResult.Failure(status);
        searchIndex.Setup(port => port.RemoveAsync("5", cancellation.Token)).ReturnsAsync(outcome);

        var result = await new RemovePersonCommandHandler(searchIndex.Object).Handle(new RemovePersonCommand("5"), cancellation.Token);

        result.Status.ShouldBe(status);
        searchIndex.Verify(port => port.RemoveAsync("5", cancellation.Token), Times.Once);
        searchIndex.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancellation_FromThePort_PropagatesFromEveryUseCase()
    {
        searchIndex.Setup(port => port.EnsureIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        searchIndex.Setup(port => port.UpsertAsync(It.IsAny<IndexedPerson>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        searchIndex.Setup(port => port.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        searchIndex.Setup(port => port.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        searchIndex.Setup(port => port.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => new EnsurePersonIndexCommandHandler(searchIndex.Object).Handle(new("x"), cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new IndexPersonCommandHandler(searchIndex.Object).Handle(new(1, "A", null), cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new GetPersonQueryHandler(searchIndex.Object).Handle(new("1"), cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new ListPersonsQueryHandler(searchIndex.Object).Handle(new(), cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new RemovePersonCommandHandler(searchIndex.Object).Handle(new("1"), cancellation.Token));
    }
}
