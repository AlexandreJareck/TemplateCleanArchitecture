#nullable enable
using System.Globalization;
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Organization;
using Template.Application.Services.MasterData;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Common;

namespace Template.Application.Features.Budgeting;

/// <summary>Creates an allocation unique per (period, cost center, currency) (BUD-002). The amount is a wire decimal string.</summary>
public sealed record CreateBudgetAllocationCommand(Guid FiscalPeriodId, Guid CostCenterId, string? Currency, string? Amount)
    : IRequest<Result<BudgetAllocationView>>;

/// <summary>Changes the amount with a reason; a decrease cannot make availability negative (BUD-003).</summary>
public sealed record ChangeBudgetAllocationAmountCommand(Guid Id, long? ExpectedVersion, string? Amount, string? Reason)
    : IRequest<Result<BudgetAllocationView>>;

/// <summary>Closes an allocation without <c>Reserved</c> reservations (BUD-011 per allocation).</summary>
public sealed record CloseBudgetAllocationCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<BudgetAllocationView>>;

public sealed record GetBudgetAllocationQuery(Guid Id) : IRequest<Result<BudgetAllocationView>>;

public sealed record ListBudgetAllocationsQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, Guid? FiscalPeriodId = null, Guid? CostCenterId = null,
    string? Currency = null, string? Status = null) : IRequest<Result<PagedResult<BudgetAllocationView>>>;

public sealed record ListBudgetReservationsQuery(
    Guid AllocationId, int? Page = null, int? PageSize = null, string? Sort = null, string? Status = null)
    : IRequest<Result<PagedResult<BudgetReservationView>>>;

/// <summary>Availability of a cost center and currency on a local date (default: today) for <c>budget.read</c> holders (BUD-012).</summary>
public sealed record GetBudgetAvailabilityQuery(Guid CostCenterId, string? Currency, DateOnly? Date) : IRequest<Result<BudgetAvailabilityView>>;

public sealed class CreateBudgetAllocationCommandValidator : AbstractValidator<CreateBudgetAllocationCommand>
{
    public CreateBudgetAllocationCommandValidator()
    {
        RuleFor(command => command.FiscalPeriodId).NotEmpty();
        RuleFor(command => command.CostCenterId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Amount).NotEmpty().MaximumLength(30);
    }
}

public sealed class ChangeBudgetAllocationAmountCommandValidator : AbstractValidator<ChangeBudgetAllocationAmountCommand>
{
    public ChangeBudgetAllocationAmountCommandValidator() =>
        RuleFor(command => command.Amount).NotEmpty().MaximumLength(30);
}

public sealed class GetBudgetAvailabilityQueryValidator : AbstractValidator<GetBudgetAvailabilityQuery>
{
    public GetBudgetAvailabilityQueryValidator()
    {
        RuleFor(query => query.CostCenterId).NotEmpty();
        RuleFor(query => query.Currency).NotEmpty().Length(3);
    }
}

public sealed class BudgetAllocationCommandHandlers(
    IBudgetAllocationRepository allocations,
    IFiscalPeriodRepository periods,
    ICostCenterRepository costCenters,
    ProcurementWriter writer) :
    IRequestHandler<CreateBudgetAllocationCommand, Result<BudgetAllocationView>>,
    IRequestHandler<ChangeBudgetAllocationAmountCommand, Result<BudgetAllocationView>>,
    IRequestHandler<CloseBudgetAllocationCommand, Result<BudgetAllocationView>>
{
    public async Task<Result<BudgetAllocationView>> Handle(CreateBudgetAllocationCommand request, CancellationToken cancellationToken)
    {
        if (!BudgetMoney.TryParse(request.Amount, out var amount))
            return ApplicationError.Validation("amount", "The amount must be a decimal string such as \"1000.00\".");
        var period = await periods.FindAsync(request.FiscalPeriodId, cancellationToken);
        if (period is null)
            return ApplicationError.Validation("fiscalPeriodId", "The fiscal period does not exist.");
        var costCenter = await costCenters.FindAsync(request.CostCenterId, cancellationToken);
        if (costCenter is null)
            return ApplicationError.Validation("costCenterId", "The cost center does not exist.");
        var settings = await writer.SettingsAsync(cancellationToken);
        var currency = CurrencyCodes.Normalize(request.Currency);

        var (allocation, error) = BudgetAllocation.Create(
            Guid.NewGuid(), period, new MasterDataReference(costCenter.Id, costCenter.IsActive), currency,
            currency is not null && settings.SupportedCurrencies.Contains(currency), amount, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await allocations.ExistsAsync(period.Id, costCenter.Id, allocation!.Currency, cancellationToken))
            return BudgetMoney.AllocationDuplicate;

        allocations.Add(allocation);
        writer.Audit(BudgetMoney.AllocationCreated, BudgetMoney.AllocationResource, allocation.Id, toState: allocation.Status.ToString(), ruleIds: ["BUD-002"],
            details: new Dictionary<string, string>
            {
                ["fiscalPeriodId"] = period.Id.ToString(),
                ["costCenterId"] = costCenter.Id.ToString(),
                ["currency"] = allocation.Currency,
                ["amount"] = Format(allocation.Amount)
            });
        var commit = await writer.CommitAsync(
            _ => Task.FromResult<long?>(null),
            constraint => constraint == IBudgetAllocationRepository.UniqueKey ? BudgetMoney.AllocationDuplicate : null,
            cancellationToken);
        if (commit is null)
            return BudgetAllocationView.From(allocation, includeHistory: true);
        return commit.Category == ErrorCategory.PreconditionFailed && await allocations.ExistsAsync(period.Id, costCenter.Id, allocation.Currency, cancellationToken)
            ? BudgetMoney.AllocationDuplicate
            : commit;
    }

    public async Task<Result<BudgetAllocationView>> Handle(ChangeBudgetAllocationAmountCommand request, CancellationToken cancellationToken)
    {
        if (!BudgetMoney.TryParse(request.Amount, out var amount))
            return ApplicationError.Validation("amount", "The amount must be a decimal string such as \"1000.00\".");

        // The allocation row lock serializes the "not below reserved" check with concurrent reservations (BUD-003, BUD-006).
        await writer.BeginAsync(cancellationToken);
        var allocation = await allocations.FindForUpdateAsync(request.Id, Guid.Empty, cancellationToken);
        if (allocation is null)
            return await writer.FailAsync(ProcurementWriter.NotFound("budget allocation"));
        if (VersionPrecondition.Check(request.ExpectedVersion, allocation.Version) is { } precondition)
            return await writer.FailAsync(precondition);

        var previous = allocation.Amount;
        if (allocation.ChangeAmount(amount, request.Reason, writer.ActorId, writer.UtcNow) is { } error)
            return await writer.FailAsync(error.ToApplicationError());
        if (allocation.Amount == previous)
        {
            await writer.RollbackAsync();
            return BudgetAllocationView.From(allocation, includeHistory: true);
        }

        writer.Audit(BudgetMoney.AllocationAmountChanged, BudgetMoney.AllocationResource, allocation.Id,
            reason: MasterDataRules.NormalizeReason(request.Reason), ruleIds: ["BUD-003"],
            details: new Dictionary<string, string>
            {
                ["previousAmount"] = Format(previous),
                ["amount"] = Format(allocation.Amount),
                ["currency"] = allocation.Currency
            });
        var commit = await writer.CommitAsync(token => allocations.GetVersionAsync(allocation.Id, token), null, cancellationToken);
        return commit is null ? BudgetAllocationView.From(allocation, includeHistory: true) : commit;
    }

    public async Task<Result<BudgetAllocationView>> Handle(CloseBudgetAllocationCommand request, CancellationToken cancellationToken)
    {
        await writer.BeginAsync(cancellationToken);
        var allocation = await allocations.FindForUpdateAsync(request.Id, Guid.Empty, cancellationToken);
        if (allocation is null)
            return await writer.FailAsync(ProcurementWriter.NotFound("budget allocation"));
        if (VersionPrecondition.Check(request.ExpectedVersion, allocation.Version) is { } precondition)
            return await writer.FailAsync(precondition);
        if (allocation.Close(request.Reason, writer.UtcNow) is { } error)
            return await writer.FailAsync(error.ToApplicationError());

        writer.Audit(BudgetMoney.AllocationClosed, BudgetMoney.AllocationResource, allocation.Id, "Active", "Closed",
            MasterDataRules.NormalizeReason(request.Reason), ["BUD-011"]);
        var commit = await writer.CommitAsync(token => allocations.GetVersionAsync(allocation.Id, token), null, cancellationToken);
        return commit is null ? BudgetAllocationView.From(allocation, includeHistory: true) : commit;
    }

    internal static string Format(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);
}

public sealed class BudgetAllocationQueryHandlers(
    IBudgetAllocationRepository allocations,
    IFiscalPeriodRepository periods,
    ProcurementWriter writer) :
    IRequestHandler<GetBudgetAllocationQuery, Result<BudgetAllocationView>>,
    IRequestHandler<ListBudgetAllocationsQuery, Result<PagedResult<BudgetAllocationView>>>,
    IRequestHandler<ListBudgetReservationsQuery, Result<PagedResult<BudgetReservationView>>>,
    IRequestHandler<GetBudgetAvailabilityQuery, Result<BudgetAvailabilityView>>
{
    public const string DefaultSort = "-createdAt";

    public async Task<Result<BudgetAllocationView>> Handle(GetBudgetAllocationQuery request, CancellationToken cancellationToken) =>
        await allocations.FindAsync(request.Id, cancellationToken) is { } allocation
            ? BudgetAllocationView.From(allocation, includeHistory: true)
            : ProcurementWriter.NotFound("budget allocation");

    public async Task<Result<PagedResult<BudgetAllocationView>>> Handle(ListBudgetAllocationsQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IBudgetAllocationRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        BudgetAllocationStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<BudgetAllocationStatus>(request.Status, true, out var parsed) || int.TryParse(request.Status, out _))
                return ApplicationError.Validation("status", "status accepts Active or Closed.");
            status = parsed;
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? null : CurrencyCodes.Normalize(request.Currency);
        if (!string.IsNullOrWhiteSpace(request.Currency) && currency is null)
            return ApplicationError.Validation("currency", "currency must be a 3-letter ISO 4217 code.");

        var filter = new BudgetAllocationFilter(request.FiscalPeriodId, request.CostCenterId, currency, status);
        return MasterDataQueries.Map(await allocations.ListAsync(filter, page.Value, cancellationToken), allocation => BudgetAllocationView.From(allocation));
    }

    public async Task<Result<PagedResult<BudgetReservationView>>> Handle(ListBudgetReservationsQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IBudgetAllocationRepository.ReservationSortFields, "-createdAt");
        if (!page.IsSuccess)
            return page.Error!;
        var statuses = ProcurementQueries.ParseEnumList<BudgetReservationStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;
        if (await allocations.GetVersionAsync(request.AllocationId, cancellationToken) is null)
            return ProcurementWriter.NotFound("budget allocation");

        var result = await allocations.ListReservationsAsync(request.AllocationId, new BudgetReservationFilter(statuses.Value), page.Value, cancellationToken);
        return MasterDataQueries.Map(result, BudgetReservationView.From);
    }

    public async Task<Result<BudgetAvailabilityView>> Handle(GetBudgetAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var currency = CurrencyCodes.Normalize(request.Currency);
        if (currency is null)
            return ApplicationError.Validation("currency", "currency must be a 3-letter ISO 4217 code.");
        var date = request.Date ?? await writer.TodayAsync(cancellationToken);
        return await BudgetAvailability.ReadAsync(periods, allocations, request.CostCenterId, currency, date, cancellationToken);
    }
}

/// <summary>Shared availability read (BUD-012) used by the budget and the requisition endpoints.</summary>
public static class BudgetAvailability
{
    public static async Task<BudgetAvailabilityView> ReadAsync(
        IFiscalPeriodRepository periods, IBudgetAllocationRepository allocations, Guid costCenterId, string currency, DateOnly date,
        CancellationToken cancellationToken)
    {
        var period = await periods.FindOpenContainingAsync(date, cancellationToken);
        if (period is null)
            return BudgetAvailabilityView.None(costCenterId, currency, date, null);
        var allocation = await allocations.FindByKeyAsync(period.Id, costCenterId, currency, cancellationToken);
        return allocation is null
            ? BudgetAvailabilityView.None(costCenterId, currency, date, period)
            : BudgetAvailabilityView.From(allocation, period, date);
    }
}
