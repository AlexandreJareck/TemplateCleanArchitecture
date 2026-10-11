using System.Reflection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Template.Application.Logging;

namespace Template.Unit.Tests.ApplicationTests.Logging;

/// <summary>The structured-log vocabulary is a dashboard contract: ids and names are unique and never change.</summary>
public sealed class LogEventsTests
{
    private static readonly EventId[] All = typeof(LogEvents)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(EventId))
        .Select(field => (EventId)field.GetValue(null)!)
        .ToArray();

    [Fact]
    public void IdsAndNamesAreUnique_AndNamesMatchTheirFields()
    {
        All.Select(eventId => eventId.Id).ShouldBeUnique();
        All.Select(eventId => eventId.Name).ShouldBeUnique();
        typeof(LogEvents).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ShouldAllBe(field => ((EventId)field.GetValue(null)!).Name == field.Name);
    }

    [Theory]
    [InlineData(nameof(LogEvents.RequestCompleted), 1000)]
    [InlineData(nameof(LogEvents.UnhandledException), 1001)]
    [InlineData(nameof(LogEvents.RequestCanceled), 1002)]
    [InlineData(nameof(LogEvents.AuthenticationChallenged), 1003)]
    [InlineData(nameof(LogEvents.AuthorizationForbidden), 1004)]
    [InlineData(nameof(LogEvents.ApplicationStarted), 2000)]
    [InlineData(nameof(LogEvents.ApplicationStopping), 2001)]
    [InlineData(nameof(LogEvents.ApplicationStartupFailed), 2002)]
    [InlineData(nameof(LogEvents.CacheOperationFailed), 3000)]
    [InlineData(nameof(LogEvents.MessagingBrokerUnreachable), 4003)]
    [InlineData(nameof(LogEvents.MessagingConnectionLost), 4004)]
    [InlineData(nameof(LogEvents.MessagePublishFailed), 4008)]
    [InlineData(nameof(LogEvents.MessagePublishTimedOut), 4009)]
    [InlineData(nameof(LogEvents.MessageRetryScheduled), 4011)]
    [InlineData(nameof(LogEvents.MessageRetriesExhausted), 4012)]
    [InlineData(nameof(LogEvents.MessageDeadLettered), 4013)]
    [InlineData(nameof(LogEvents.MessagingConsumersStopped), 4002)]
    [InlineData(nameof(LogEvents.SearchOperationFailed), 5001)]
    [InlineData(nameof(LogEvents.HealthDependencyStatusChanged), 6000)]
    public void CriticalEvents_KeepTheirPublishedIds(string name, int id)
    {
        All.Single(eventId => eventId.Name == name).Id.ShouldBe(id);
    }
}
