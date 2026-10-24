using System.Data.Common;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Approvals;
using Template.Application.Security;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Identity.Contexts;
using Template.Infrastructure.Identity.Models;
using Template.WorkflowFunctions.Activities;
using Template.WorkflowFunctions.Hosting;

namespace Template.WorkflowFunctions.Tests;

/// <summary>Activity retry classification, internal-key authorization and host settings validation.</summary>
public sealed class HostBoundaryTests
{
    private const string Key = "synthetic-internal-key-0123456789abcdef";

    [Fact]
    public void Activities_ClassifyDatabaseAndTimeoutFailuresAsTransient_AndRuleFailuresAsPermanent()
    {
        ApprovalActivityFunctions.IsTransient(new TimeoutException()).ShouldBeTrue();
        ApprovalActivityFunctions.IsTransient(new SyntheticDbException()).ShouldBeTrue();
        ApprovalActivityFunctions.IsTransient(new InvalidOperationException("wrapped", new TimeoutException())).ShouldBeTrue();
        ApprovalActivityFunctions.IsTransient(new InvalidOperationException("The approval cannot start: validation.")).ShouldBeFalse();
        ApprovalActivityFunctions.IsTransient(new NotSupportedException()).ShouldBeFalse();
    }

    [Fact]
    public async Task ApproverDirectory_OfTheHost_ReadsOnlyActiveApprovers_AndNeedsNoSignInConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TestDatabaseName"] = Guid.NewGuid().ToString("N"),
            ["ConnectionStrings:IdentityConnection"] = "Host=127.0.0.1;Database=unused"
        }).Build();
        var relational = new ServiceCollection().AddIdentityDirectoryInfrastructure(configuration, useInMemoryDatabase: false);
        relational.ShouldContain(descriptor => descriptor.ServiceType == typeof(IApproverDirectory));

        await using var provider = new ServiceCollection().AddIdentityDirectoryInfrastructure(configuration, useInMemoryDatabase: true).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        var approverRole = new ApplicationRole("FinanceApprover") { Id = Guid.NewGuid(), NormalizedName = "FINANCEAPPROVER" };
        var otherRole = new ApplicationRole("Requester") { Id = Guid.NewGuid(), NormalizedName = "REQUESTER" };
        var approver = new ApplicationUser { Id = Guid.NewGuid(), UserName = "ana", NormalizedUserName = "ANA", Name = "Ana Aprovadora" };
        var locked = new ApplicationUser { Id = Guid.NewGuid(), UserName = "lia", NormalizedUserName = "LIA", Name = "", LockoutEnd = DateTimeOffset.UtcNow.AddDays(1) };
        var requester = new ApplicationUser { Id = Guid.NewGuid(), UserName = "rui", NormalizedUserName = "RUI", Name = "Rui" };
        context.AddRange(approverRole, otherRole, approver, locked, requester);
        context.RoleClaims.Add(new IdentityRoleClaim<Guid> { RoleId = approverRole.Id, ClaimType = Permissions.ClaimType, ClaimValue = Permissions.ApprovalsDecide });
        context.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = approver.Id, RoleId = approverRole.Id },
            new IdentityUserRole<Guid> { UserId = locked.Id, RoleId = approverRole.Id },
            new IdentityUserRole<Guid> { UserId = requester.Id, RoleId = otherRole.Id });
        await context.SaveChangesAsync();
        var directory = scope.ServiceProvider.GetRequiredService<IApproverDirectory>();

        (await directory.GetCandidateAsync(approver.Id, default)).ShouldBe(new ApprovalCandidate(approver.Id, true, true));
        (await directory.GetCandidateAsync(locked.Id, default)).ShouldBe(new ApprovalCandidate(locked.Id, false, true));
        (await directory.GetCandidateAsync(requester.Id, default)).ShouldBe(new ApprovalCandidate(requester.Id, true, false));
        (await directory.GetCandidateAsync(Guid.NewGuid(), default)).ShouldBeNull();
        (await directory.GetRolesAsync(approver.Id, default)).ShouldBe(["FinanceApprover"]);

        var all = await directory.SearchApproversAsync(null, PageRequest.Create(1, 20, null, ["userName"], "userName").Value, default);
        all.Items.Select(user => user.UserName).ShouldBe(["ana"]);
        (await directory.SearchApproversAsync("  aprovadora ", PageRequest.Create(1, 20, null, ["userName"], "userName").Value, default)).TotalItems.ShouldBe(1);
        (await directory.SearchApproversAsync("nobody", PageRequest.Create(1, 20, null, ["userName"], "userName").Value, default)).TotalItems.ShouldBe(0);

        var names = await directory.GetDisplayNamesAsync([approver.Id, locked.Id], default);
        (names[approver.Id], names[locked.Id]).ShouldBe(("Ana Aprovadora", "lia"));
        (await directory.GetDisplayNamesAsync([], default)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("wrong-key", false)]
    [InlineData(Key + "x", false)]
    [InlineData(Key, true)]
    public void InternalApi_AcceptsOnlyTheConfiguredKey(string? presented, bool expected)
    {
        var authorization = new InternalApiAuthorization(Options.Create(new WorkflowHostSettings { Key = Key }));

        authorization.IsAuthorized(Request(presented)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("too-short", false)]
    [InlineData(Key, true)]
    public void HostSettings_RequireALongInternalKey(string key, bool valid)
    {
        var result = new WorkflowHostSettingsValidator().Validate(null, new WorkflowHostSettings { Key = key });

        result.Succeeded.ShouldBe(valid);
        if (!valid)
            result.FailureMessage.ShouldNotContain(key.Length > 0 ? key : "\u0000");
    }

    [Fact]
    public void RequestContext_UsesTheOrchestrationCorrelation_AndBoundsItsLength()
    {
        var context = new WorkflowRequestContext();
        context.Use(new string('c', 300));
        context.CorrelationId.Length.ShouldBe(128);

        var fallback = new WorkflowRequestContext();
        fallback.Use(null);
        fallback.CorrelationId.Length.ShouldBe(32);
    }

    [Fact]
    public void InstanceIds_AreDerivedFromTheAwardOnly()
    {
        var award = Guid.NewGuid();
        var instanceId = ApprovalWorkflowNames.InstanceIdFor(award);

        ApprovalWorkflowNames.AwardIdFrom(instanceId).ShouldBe(award);
        ApprovalWorkflowNames.AwardIdFrom("approval-not-a-guid-000000000000000000000").ShouldBeNull();
        ApprovalWorkflowNames.AwardIdFrom("../admin").ShouldBeNull();
        ApprovalWorkflowNames.AwardIdFrom(null).ShouldBeNull();
    }

    private static HttpRequestData Request(string? key)
    {
        var headers = new HttpHeadersCollection();
        if (key is not null)
            headers.Add(ApprovalWorkflowHttp.KeyHeader, key);
        var request = new Mock<HttpRequestData>(Mock.Of<FunctionContext>());
        request.SetupGet(item => item.Headers).Returns(headers);
        request.SetupGet(item => item.Identities).Returns(Array.Empty<ClaimsIdentity>());
        return request.Object;
    }

    private sealed class SyntheticDbException : DbException;
}
