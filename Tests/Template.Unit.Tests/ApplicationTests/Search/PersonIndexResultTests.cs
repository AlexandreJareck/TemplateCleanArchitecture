using Shouldly;
using Template.Application.Search.Persons;

namespace Template.Unit.Tests.ApplicationTests.Search;

public class PersonIndexResultTests
{
    [Fact]
    public void Success_HasAValue_AndFailure_NeverDoes()
    {
        var person = new IndexedPerson(1, "A", null);

        PersonIndexResult<IndexedPerson>.Success(person).Value.ShouldBe(person);
        PersonIndexResult<IndexedPerson>.Failure(PersonIndexStatus.NotFound).Value.ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => PersonIndexResult<IndexedPerson>.Success(null!));
    }

    [Fact]
    public void Failure_CannotCarryTheSuccessStatus()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PersonIndexResult.Failure(PersonIndexStatus.Succeeded));
        Should.Throw<ArgumentOutOfRangeException>(() => PersonIndexResult<IndexedPerson>.Failure(PersonIndexStatus.Succeeded));
        Should.Throw<ArgumentOutOfRangeException>(() => PersonBulkIndexResult.Failure(PersonIndexStatus.Succeeded, []));
    }

    [Fact]
    public void Bulk_PartialResult_IsNotACompleteSuccess()
    {
        var partial = PersonBulkIndexResult.Completed(indexedIds: [1], failedIds: [2]);

        partial.Status.ShouldBe(PersonIndexStatus.Succeeded);
        partial.IsComplete.ShouldBeFalse();
        partial.IsPartial.ShouldBeTrue();
    }

    [Fact]
    public void Bulk_AllIndexed_IsComplete()
    {
        var complete = PersonBulkIndexResult.Completed(indexedIds: [1, 2], failedIds: []);

        complete.IsComplete.ShouldBeTrue();
        complete.IsPartial.ShouldBeFalse();
    }

    [Fact]
    public void Bulk_RequestFailure_ListsEveryRequestedPersonAsFailed()
    {
        var failure = PersonBulkIndexResult.Failure(PersonIndexStatus.Unavailable, [1, 2]);

        failure.IsComplete.ShouldBeFalse();
        failure.IsPartial.ShouldBeFalse();
        failure.IndexedIds.ShouldBeEmpty();
        failure.FailedIds.ShouldBe([1, 2]);
    }
}
