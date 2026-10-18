#nullable enable
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Organization;
using Template.Application.Services.MasterData;
using Template.Application.Wrappers;
using Template.Domain.Organization;

namespace Template.Application.Features.Organization;

public sealed record GetOrganizationSettingsQuery : IRequest<Result<OrganizationSettingsView>>;

/// <summary>Replaces the organization settings (ORG-004). Permission <c>organization.settings.manage</c>.</summary>
public sealed record UpdateOrganizationSettingsCommand(
    long? ExpectedVersion,
    string? DisplayName,
    string? BaseCurrency,
    string? TimeZone,
    IReadOnlyList<string>? SupportedCurrencies) : IRequest<Result<OrganizationSettingsView>>;

public sealed class OrganizationSettingsHandlers(
    IOrganizationSettingsRepository settings,
    IOrganizationSettingsLock settingsLock,
    MasterDataWriter writer) :
    IRequestHandler<GetOrganizationSettingsQuery, Result<OrganizationSettingsView>>,
    IRequestHandler<UpdateOrganizationSettingsCommand, Result<OrganizationSettingsView>>
{
    private static readonly ApplicationError Missing = new(
        ProblemCodes.NotFound, ErrorCategory.NotFound, "The organization settings have not been initialized.");

    public async Task<Result<OrganizationSettingsView>> Handle(GetOrganizationSettingsQuery request, CancellationToken cancellationToken) =>
        await settings.GetAsync(cancellationToken) is { } current ? OrganizationSettingsView.From(current) : Missing;

    public async Task<Result<OrganizationSettingsView>> Handle(UpdateOrganizationSettingsCommand request, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        if (current is null)
            return Missing;
        if (VersionPrecondition.Check(request.ExpectedVersion, current.Version) is { } precondition)
            return precondition;

        var before = (current.BaseCurrency, current.TimeZone);
        var version = current.Version;
        var locked = await settingsLock.HasBudgetAllocationsAsync(cancellationToken);
        var error = current.Change(
            request.DisplayName, request.BaseCurrency, request.TimeZone, request.SupportedCurrencies,
            IsKnownTimeZone(request.TimeZone), locked, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (current.Version == version)
            return OrganizationSettingsView.From(current);

        writer.Audit(OrganizationAudit.SettingsChanged, OrganizationAudit.SettingsResource, current.Id,
            fromState: $"{before.BaseCurrency} {before.TimeZone}", toState: $"{current.BaseCurrency} {current.TimeZone}",
            ruleIds: ["ORG-004"], details: new Dictionary<string, string> { ["fields"] = "displayName,baseCurrency,timeZone,supportedCurrencies" });
        var commit = await writer.CommitAsync(async token => (await settings.GetAsync(token))?.Version, null, cancellationToken);
        return commit is null ? OrganizationSettingsView.From(current) : commit;
    }

    /// <summary>IANA identifiers are resolved by the platform's time-zone database (ICU on every supported OS).</summary>
    private static bool IsKnownTimeZone(string? timeZone)
    {
        var value = timeZone?.Trim();
        return !string.IsNullOrEmpty(value)
            && TimeZoneInfo.TryFindSystemTimeZoneById(value, out _)
            && TimeZoneInfo.TryConvertWindowsIdToIanaId(value, out _) is false; // IANA only, not Windows IDs
    }
}
