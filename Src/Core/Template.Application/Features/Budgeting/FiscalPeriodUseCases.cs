#nullable enable
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Services.MasterData;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Common;

namespace Template.Application.Features.Budgeting;

/// <summary>Creates a <c>Planned</c> fiscal period with a unique code and a non-overlapping range (BUD-001).</summary>
public sealed record CreateFiscalPeriodCommand(string? Code, string? Name, DateOnly? StartDate, DateOnly? EndDate) : IRequest<Result<FiscalPeriodView>>;

/// <summary>Changes the name (until closed) and the dates (only while <c>Planned</c>).</summary>
public sealed record UpdateFiscalPeriodCommand(Guid Id, long? ExpectedVersion, string? Name, DateOnly? StartDate, DateOnly? EndDate) : IRequest<Result<FiscalPeriodView>>;

public enum FiscalPeriodTransition
{
    Open,
    Close
}

/// <summary><c>Planned → Open</c> and <c>Open → Closed</c> (BUD-001, BUD-011).</summary>
public sealed record ChangeFiscalPeriodStatusCommand(Guid Id, long? ExpectedVersion, FiscalPeriodTransition Transition) : IRequest<Result<FiscalPeriodView>>;

public sealed record GetFiscalPeriodQuery(Guid Id) : IRequest<Result<FiscalPeriodView>>;

/// <summary><see cref="Status"/> is a comma-separated list such as <c>Planned,Open</c>.</summary>
public sealed record ListFiscalPeriodsQuery(int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, string? Status = null)
    : IRequest<Result<PagedResult<FiscalPeriodView>>>;

public sealed class CreateFiscalPeriodCommandValidator : AbstractValidator<CreateFiscalPeriodCommand>
{
    public CreateFiscalPeriodCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
        RuleFor(command => command.StartDate).NotNull();
        RuleFor(command => command.EndDate).NotNull();
    }
}

public sealed class UpdateFiscalPeriodCommandValidator : AbstractValidator<UpdateFiscalPeriodCommand>
{
    public UpdateFiscalPeriodCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
        RuleFor(command => command.StartDate).NotNull();
        RuleFor(command => command.EndDate).NotNull();
    }
}

public sealed class FiscalPeriodCommandHandlers(
    IFiscalPeriodRepository periods,
    IBudgetAllocationRepository allocations,
    ProcurementWriter writer) :
    IRequestHandler<CreateFiscalPeriodCommand, Result<FiscalPeriodView>>,
    IRequestHandler<UpdateFiscalPeriodCommand, Result<FiscalPeriodView>>,
    IRequestHandler<ChangeFiscalPeriodStatusCommand, Result<FiscalPeriodView>>
{
    public async Task<Result<FiscalPeriodView>> Handle(CreateFiscalPeriodCommand request, CancellationToken cancellationToken)
    {
        var (period, error) = FiscalPeriod.Create(
            Guid.NewGuid(), request.Code, request.Name, request.StartDate!.Value, request.EndDate!.Value, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await periods.CodeExistsAsync(period!.Code, cancellationToken))
            return BudgetMoney.PeriodCodeDuplicate;
        if (await periods.OverlapsAsync(period.StartDate, period.EndDate, null, cancellationToken))
            return BudgetMoney.PeriodOverlap;

        periods.Add(period);
        writer.Audit(BudgetMoney.PeriodCreated, BudgetMoney.PeriodResource, period.Id, toState: period.Status.ToString(), ruleIds: ["BUD-001"],
            details: new Dictionary<string, string> { ["code"] = period.Code });
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null), UniqueViolation, cancellationToken);
        if (commit is null)
            return FiscalPeriodView.From(period);

        // A concurrent creation that lost a deadlock on the exclusion constraint is re-evaluated against the winner.
        if (commit.Category == ErrorCategory.PreconditionFailed && await periods.CodeExistsAsync(period.Code, cancellationToken))
            return BudgetMoney.PeriodCodeDuplicate;
        if (commit.Category == ErrorCategory.PreconditionFailed && await periods.OverlapsAsync(period.StartDate, period.EndDate, null, cancellationToken))
            return BudgetMoney.PeriodOverlap;
        return commit;
    }

    public async Task<Result<FiscalPeriodView>> Handle(UpdateFiscalPeriodCommand request, CancellationToken cancellationToken)
    {
        var period = await periods.FindAsync(request.Id, cancellationToken);
        if (period is null)
            return ProcurementWriter.NotFound("fiscal period");
        if (VersionPrecondition.Check(request.ExpectedVersion, period.Version) is { } precondition)
            return precondition;

        var version = period.Version;
        if (period.Update(request.Name, request.StartDate!.Value, request.EndDate!.Value, writer.UtcNow) is { } error)
            return error.ToApplicationError();
        if (period.Version == version)
            return FiscalPeriodView.From(period);
        if (await periods.OverlapsAsync(period.StartDate, period.EndDate, period.Id, cancellationToken))
        {
            await writer.RollbackAsync();
            return BudgetMoney.PeriodOverlap;
        }

        writer.Audit(BudgetMoney.PeriodUpdated, BudgetMoney.PeriodResource, period.Id, ruleIds: ["BUD-001"],
            details: new Dictionary<string, string> { ["fields"] = "name,startDate,endDate" });
        var commit = await writer.CommitAsync(token => periods.GetVersionAsync(period.Id, token), UniqueViolation, cancellationToken);
        return commit is null ? FiscalPeriodView.From(period) : commit;
    }

    public async Task<Result<FiscalPeriodView>> Handle(ChangeFiscalPeriodStatusCommand request, CancellationToken cancellationToken)
    {
        if (request.Transition == FiscalPeriodTransition.Open)
        {
            var period = await periods.FindAsync(request.Id, cancellationToken);
            if (period is null)
                return ProcurementWriter.NotFound("fiscal period");
            if (VersionPrecondition.Check(request.ExpectedVersion, period.Version) is { } precondition)
                return precondition;
            if (period.Open(writer.UtcNow) is { } error)
                return error.ToApplicationError();

            writer.Audit(BudgetMoney.PeriodOpened, BudgetMoney.PeriodResource, period.Id, "Planned", "Open", ruleIds: ["BUD-001"]);
            var commit = await writer.CommitAsync(token => periods.GetVersionAsync(period.Id, token), null, cancellationToken);
            return commit is null ? FiscalPeriodView.From(period) : commit;
        }

        // BUD-011: the period row lock makes a concurrent submission (which reads the period FOR SHARE) either finish
        // before the reservation check or see the period Closed afterwards.
        await writer.BeginAsync(cancellationToken);
        var locked = await periods.FindForUpdateAsync(request.Id, cancellationToken);
        if (locked is null)
            return await writer.FailAsync(ProcurementWriter.NotFound("fiscal period"));
        if (VersionPrecondition.Check(request.ExpectedVersion, locked.Version) is { } lockedPrecondition)
            return await writer.FailAsync(lockedPrecondition);
        var hasReserved = await allocations.AnyReservedInPeriodAsync(locked.Id, cancellationToken);
        if (locked.Close(hasReserved, writer.UtcNow) is { } closeError)
            return await writer.FailAsync(closeError.ToApplicationError());

        writer.Audit(BudgetMoney.PeriodClosed, BudgetMoney.PeriodResource, locked.Id, "Open", "Closed", ruleIds: ["BUD-011"]);
        var closeCommit = await writer.CommitAsync(token => periods.GetVersionAsync(locked.Id, token), null, cancellationToken);
        return closeCommit is null ? FiscalPeriodView.From(locked) : closeCommit;
    }

    private static ApplicationError? UniqueViolation(string constraint) => constraint switch
    {
        IFiscalPeriodRepository.CodeUniqueKey => BudgetMoney.PeriodCodeDuplicate,
        IFiscalPeriodRepository.NoOverlapKey => BudgetMoney.PeriodOverlap,
        _ => null
    };
}

public sealed class FiscalPeriodQueryHandlers(IFiscalPeriodRepository periods) :
    IRequestHandler<GetFiscalPeriodQuery, Result<FiscalPeriodView>>,
    IRequestHandler<ListFiscalPeriodsQuery, Result<PagedResult<FiscalPeriodView>>>
{
    public const string DefaultSort = "-startDate";

    public async Task<Result<FiscalPeriodView>> Handle(GetFiscalPeriodQuery request, CancellationToken cancellationToken) =>
        await periods.FindAsync(request.Id, cancellationToken) is { } period ? FiscalPeriodView.From(period) : ProcurementWriter.NotFound("fiscal period");

    public async Task<Result<PagedResult<FiscalPeriodView>>> Handle(ListFiscalPeriodsQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IFiscalPeriodRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;
        var statuses = ProcurementQueries.ParseEnumList<FiscalPeriodStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;

        var filter = new FiscalPeriodFilter(MasterDataQueries.Trim(request.Q), statuses.Value);
        return MasterDataQueries.Map(await periods.ListAsync(filter, page.Value, cancellationToken), FiscalPeriodView.From);
    }
}
