#nullable enable
using Microsoft.Extensions.Logging;
using Template.Application.Interfaces.Integrations;
using Template.Application.Logging;
using Template.Application.Security;
using Template.Application.Services.Integrations;
using Template.Application.Services.Procurement;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Sourcing;

/// <summary>
/// Sourcing authorization after the endpoint permission check (docs/product/permissions.md): RFQs are readable with
/// <c>sourcing.rfqs.manage</c> or <c>sourcing.quotes.read</c>; quote commercial details and comparisons only with
/// <c>sourcing.quotes.read</c> (SRC-021); every waiver needs its dedicated permission (SOD-5). It never bypasses a rule.
/// </summary>
public sealed class SourcingAccess(ProcurementWriter writer)
{
    public static string PermissionFor(WaiverType type) => type switch
    {
        WaiverType.LowValueDirectPurchase => Permissions.SourcingWaiversLowValue,
        WaiverType.DirectPurchase => Permissions.SourcingWaiversDirectPurchase,
        WaiverType.InsufficientQuotations => Permissions.SourcingWaiversInsufficientQuotations,
        WaiverType.NonLeadingSelection => Permissions.SourcingWaiversNonLeading,
        WaiverType.ManualExchangeRate => Permissions.SourcingRatesManual,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public async Task<bool> MayReadAsync(CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return permissions.Contains(Permissions.SourcingRfqsManage) || permissions.Contains(Permissions.SourcingQuotesRead);
    }

    public Task<bool> CommercialVisibleAsync(CancellationToken cancellationToken) =>
        writer.HasPermissionAsync(Permissions.SourcingQuotesRead, cancellationToken);

    /// <summary>A waiver request for the actor, or <c>null</c> when the actor lacks the dedicated permission.</summary>
    public async Task<WaiverRequest?> WaiverAsync(WaiverType type, string? reason, CancellationToken cancellationToken)
    {
        var permission = PermissionFor(type);
        return writer.ActorId is { } actor && await writer.HasPermissionAsync(permission, cancellationToken)
            ? new WaiverRequest(type, permission, reason, actor)
            : null;
    }

    /// <summary>Actions the server would accept now from this actor (state and permission only; content guards still apply).</summary>
    public async Task<IReadOnlyList<string>> AllowedActionsAsync(RequestForQuotation rfq, CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        var manage = permissions.Contains(Permissions.SourcingRfqsManage);
        var enter = permissions.Contains(Permissions.SourcingQuotesEnter);
        var beforeDeadline = rfq.DeadlineUtc is { } deadline && writer.UtcNow < deadline;
        var actions = new List<string>();
        switch (rfq.Status)
        {
            case RfqStatus.Draft when manage:
                actions.AddRange([SourcingAudit.ActionInvite, SourcingAudit.ActionRemoveInvitation, SourcingAudit.ActionPublish, SourcingAudit.ActionCancel]);
                break;
            case RfqStatus.Published:
                if (manage && beforeDeadline)
                    actions.AddRange([SourcingAudit.ActionInvite, SourcingAudit.ActionExtendDeadline, SourcingAudit.ActionRemind]);
                if (enter)
                    actions.AddRange([SourcingAudit.ActionEnterQuote, SourcingAudit.ActionReviseQuote, SourcingAudit.ActionWithdrawQuote]);
                if (manage && !beforeDeadline)
                    actions.Add(SourcingAudit.ActionClose);
                if (manage && beforeDeadline && permissions.Contains(Permissions.SourcingRfqsCloseEarly))
                    actions.Add(SourcingAudit.ActionCloseEarly);
                if (manage)
                    actions.Add(SourcingAudit.ActionCancel);
                break;
            case RfqStatus.Closed:
                if (rfq.CurrentComparison?.Status == ComparisonStatus.Incomplete && permissions.Contains(Permissions.SourcingRatesManual))
                    actions.Add(SourcingAudit.ActionRecordRate);
                if (rfq.CurrentComparison?.Status == ComparisonStatus.Complete && permissions.Contains(Permissions.SourcingAwardsSubmit))
                    actions.Add(SourcingAudit.ActionAward);
                if (manage)
                    actions.Add(SourcingAudit.ActionCancel);
                break;
        }

        return actions;
    }

    public async Task<RfqView> ViewAsync(RequestForQuotation rfq, CancellationToken cancellationToken) =>
        RfqView.From(rfq, await CommercialVisibleAsync(cancellationToken), await AllowedActionsAsync(rfq, cancellationToken));
}

/// <summary>
/// Resolves the PTAX selling rate snapshot for a sourcing date (SP-5, CALC-004) through the Phase 25 port. Any
/// provider outcome other than <c>Found</c> yields <c>null</c>: the comparison becomes <c>Incomplete</c> and a manual
/// rate needs the <c>ManualExchangeRate</c> waiver (SRC-013). A rate is never guessed.
/// </summary>
public sealed class SourcingExchangeRates(PtaxRateResolver resolver, ILogger<SourcingExchangeRates> logger)
{
    public async Task<ExchangeRateInput?> ResolveAsync(string currency, DateOnly localDate, CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveSellingRateAsync(currency, localDate, cancellationToken);
        if (result.Outcome != EnrichmentOutcome.Found || result.Value is not { } rate)
        {
            logger.LogInformation(LogEvents.SourcingRateUnavailable, "Exchange rate for {Currency} unavailable with {Outcome}", currency, result.Outcome);
            return null;
        }

        logger.LogInformation(LogEvents.SourcingRateResolved, "Exchange rate for {Currency} resolved from {Provider}", currency, rate.Source);
        return new ExchangeRateInput(rate.Currency, rate.Rate, ExchangeRateSource.BcbPtax, rate.QuoteDate, rate.RetrievedAtUtc, rate.Source);
    }

    public async Task<IReadOnlyList<ExchangeRateInput>> ResolveManyAsync(IEnumerable<string> currencies, DateOnly localDate, CancellationToken cancellationToken)
    {
        var rates = new List<ExchangeRateInput>();
        foreach (var currency in currencies)
        {
            if (await ResolveAsync(currency, localDate, cancellationToken) is { } rate)
                rates.Add(rate);
        }

        return rates;
    }
}
