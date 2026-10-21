#nullable enable
using System.Globalization;
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Services.Procurement;
using Template.Domain.Common;
using Template.Domain.Requisitions;

namespace Template.Application.Features.Requisitions;

/// <summary>
/// Line body: with <see cref="CatalogItemId"/> the kind, category and unit come from the catalog item and the description
/// defaults to its description (CAT-004 reference prices are advisory, so the price is always the user's); without it,
/// kind, category and unit are required. Quantity and price amount are invariant decimal strings.
/// </summary>
public sealed record RequisitionLineRequest(
    Guid? CatalogItemId,
    string? Description,
    RequisitionLineKind? Kind,
    Guid? CategoryId,
    Guid? UnitOfMeasureId,
    string? Quantity,
    MoneyInput? EstimatedUnitPrice);

/// <summary>Creates revision 1 in <c>Draft</c> for the actor (requester) on an active cost center. Permission <c>requisitions.create</c>.</summary>
public sealed record CreateRequisitionDraftCommand(Guid CostCenterId, string? Currency, string? Purpose, DateOnly? NeededBy) : IRequest<Result<RequisitionView>>;

/// <summary>Replaces the draft header (REQ-001 ownership or edit-any proxy).</summary>
public sealed record UpdateRequisitionDraftCommand(Guid Id, long? ExpectedVersion, Guid CostCenterId, string? Currency, string? Purpose, DateOnly? NeededBy)
    : IRequest<Result<RequisitionView>>;

/// <summary>Deletes a never-submitted draft (REQ-010).</summary>
public sealed record DiscardRequisitionDraftCommand(Guid Id, long? ExpectedVersion) : IRequest<Result>;

public sealed record AddRequisitionLineCommand(Guid Id, long? ExpectedVersion, RequisitionLineRequest Line) : IRequest<Result<RequisitionView>>;

public sealed record UpdateRequisitionLineCommand(Guid Id, long? ExpectedVersion, int LineNumber, RequisitionLineRequest Line) : IRequest<Result<RequisitionView>>;

public sealed record RemoveRequisitionLineCommand(Guid Id, long? ExpectedVersion, int LineNumber) : IRequest<Result<RequisitionView>>;

/// <summary>Attachment metadata in <c>Pending</c> (FIL-002; bytes arrive in Phase 30).</summary>
public sealed record AddRequisitionAttachmentCommand(Guid Id, long? ExpectedVersion, string? FileName, string? MediaType, long Length)
    : IRequest<Result<RequisitionView>>;

public sealed record RemoveRequisitionAttachmentCommand(Guid Id, long? ExpectedVersion, Guid AttachmentId) : IRequest<Result<RequisitionView>>;

public sealed class CreateRequisitionDraftCommandValidator : AbstractValidator<CreateRequisitionDraftCommand>
{
    public CreateRequisitionDraftCommandValidator()
    {
        RuleFor(command => command.CostCenterId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Purpose).MaximumLength(PurchaseRequisition.PurposeMaxLength);
    }
}

public sealed class UpdateRequisitionDraftCommandValidator : AbstractValidator<UpdateRequisitionDraftCommand>
{
    public UpdateRequisitionDraftCommandValidator()
    {
        RuleFor(command => command.CostCenterId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Purpose).MaximumLength(PurchaseRequisition.PurposeMaxLength);
    }
}

/// <summary>REQ-002 field validation shared by add and update.</summary>
public sealed class RequisitionLineRequestValidator : AbstractValidator<RequisitionLineRequest>
{
    public RequisitionLineRequestValidator()
    {
        RuleFor(line => line.Description).MaximumLength(RequisitionLine.DescriptionMaxLength);
        RuleFor(line => line.Quantity).NotEmpty().MaximumLength(30);
        RuleFor(line => line.EstimatedUnitPrice).NotNull();
        RuleFor(line => line.EstimatedUnitPrice!.Amount).NotEmpty().MaximumLength(30).When(line => line.EstimatedUnitPrice is not null);
        RuleFor(line => line.EstimatedUnitPrice!.Currency).NotEmpty().Length(3).When(line => line.EstimatedUnitPrice is not null);
        RuleFor(line => line.Kind).NotNull().When(line => line.CatalogItemId is null);
        RuleFor(line => line.CategoryId).NotEmpty().When(line => line.CatalogItemId is null);
        RuleFor(line => line.UnitOfMeasureId).NotEmpty().When(line => line.CatalogItemId is null);
        RuleFor(line => line.Description).NotEmpty().When(line => line.CatalogItemId is null);
    }
}

public sealed class AddRequisitionLineCommandValidator : AbstractValidator<AddRequisitionLineCommand>
{
    public AddRequisitionLineCommandValidator() => RuleFor(command => command.Line).NotNull().SetValidator(new RequisitionLineRequestValidator());
}

public sealed class UpdateRequisitionLineCommandValidator : AbstractValidator<UpdateRequisitionLineCommand>
{
    public UpdateRequisitionLineCommandValidator() => RuleFor(command => command.Line).NotNull().SetValidator(new RequisitionLineRequestValidator());
}

public sealed class AddRequisitionAttachmentCommandValidator : AbstractValidator<AddRequisitionAttachmentCommand>
{
    public AddRequisitionAttachmentCommandValidator()
    {
        RuleFor(command => command.FileName).NotEmpty().MaximumLength(1000);
        RuleFor(command => command.MediaType).NotEmpty().MaximumLength(FilePolicy.MediaTypeMaxLength);
        RuleFor(command => command.Length).GreaterThan(0);
    }
}

public sealed class RequisitionDraftCommandHandlers(
    IPurchaseRequisitionRepository requisitions,
    IRequisitionNumberAllocator numbers,
    IRequisitionReferenceReader references,
    ICostCenterRepository costCenters,
    RequisitionAccess access,
    ProcurementWriter writer) :
    IRequestHandler<CreateRequisitionDraftCommand, Result<RequisitionView>>,
    IRequestHandler<UpdateRequisitionDraftCommand, Result<RequisitionView>>,
    IRequestHandler<DiscardRequisitionDraftCommand, Result>,
    IRequestHandler<AddRequisitionLineCommand, Result<RequisitionView>>,
    IRequestHandler<UpdateRequisitionLineCommand, Result<RequisitionView>>,
    IRequestHandler<RemoveRequisitionLineCommand, Result<RequisitionView>>,
    IRequestHandler<AddRequisitionAttachmentCommand, Result<RequisitionView>>,
    IRequestHandler<RemoveRequisitionAttachmentCommand, Result<RequisitionView>>
{
    public async Task<Result<RequisitionView>> Handle(CreateRequisitionDraftCommand request, CancellationToken cancellationToken)
    {
        if (writer.ActorId is not { } actor)
            return ProcurementWriter.Forbidden();
        var costCenter = await LoadCostCenterAsync(request.CostCenterId, cancellationToken);
        if (costCenter is null)
            return ApplicationError.Validation("costCenterId", "The cost center does not exist.");
        var settings = await writer.SettingsAsync(cancellationToken);
        var today = ProcurementWriter.LocalDate(writer.UtcNow, settings.TimeZone);
        var number = PurchaseRequisition.FormatNumber(today.Year, await numbers.NextAsync(today.Year, cancellationToken));
        var currency = CurrencyCodes.Normalize(request.Currency);

        var (requisition, error) = PurchaseRequisition.CreateDraft(
            Guid.NewGuid(), number, actor, costCenter, currency, currency is not null && settings.SupportedCurrencies.Contains(currency),
            request.Purpose, request.NeededBy, actor, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        requisitions.Add(requisition!);
        writer.Audit(RequisitionAudit.DraftCreated, RequisitionAudit.Resource, requisition!.Id, toState: "Draft", ruleIds: ["REQ-002", "ORG-003"],
            details: new Dictionary<string, string> { ["number"] = requisition.Number, ["costCenterId"] = costCenter.Id.ToString() });
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, cancellationToken);
        return commit is null ? await access.ViewAsync(requisition, cancellationToken) : commit;
    }

    public async Task<Result<RequisitionView>> Handle(UpdateRequisitionDraftCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var costCenter = request.CostCenterId == requisition!.CostCenterId
            ? await LoadCostCenterAsync(request.CostCenterId, cancellationToken) ?? Snapshot(requisition)
            : await LoadCostCenterAsync(request.CostCenterId, cancellationToken);
        if (costCenter is null)
            return ApplicationError.Validation("costCenterId", "The cost center does not exist.");
        var settings = await writer.SettingsAsync(cancellationToken);
        var currency = CurrencyCodes.Normalize(request.Currency);

        var version = requisition.Version;
        var error = requisition.UpdateHeader(
            costCenter, currency, currency is not null && settings.SupportedCurrencies.Contains(currency), request.Purpose, request.NeededBy, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (requisition.Version == version)
            return await access.ViewAsync(requisition, cancellationToken);

        AuditDraftChange(requisition, "costCenterId,currency,purpose,neededBy");
        return await CommitAsync(requisition, cancellationToken);
    }

    public async Task<Result> Handle(DiscardRequisitionDraftCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (requisition!.EnsureDiscardable() is { } error)
            return error.ToApplicationError();

        requisitions.Remove(requisition);
        writer.Audit(RequisitionAudit.DraftDiscarded, RequisitionAudit.Resource, requisition.Id, "Draft", ruleIds: ["REQ-010"],
            details: new Dictionary<string, string> { ["number"] = requisition.Number });
        var commit = await writer.CommitAsync(token => requisitions.GetVersionAsync(requisition.Id, token), null, cancellationToken);
        return commit is null ? Result.Success() : commit;
    }

    public async Task<Result<RequisitionView>> Handle(AddRequisitionLineCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var (input, inputError) = await ResolveLineAsync(request.Line, cancellationToken);
        if (inputError is not null)
            return inputError;

        var (line, error) = requisition!.AddLine(input!, RequisitionPolicy.V1, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();

        AuditDraftChange(requisition, "lines", line!.LineNumber);
        return await CommitAsync(requisition, cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(UpdateRequisitionLineCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var (input, inputError) = await ResolveLineAsync(request.Line, cancellationToken);
        if (inputError is not null)
            return inputError;
        if (requisition!.UpdateLine(request.LineNumber, input!, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        AuditDraftChange(requisition, "lines", request.LineNumber);
        return await CommitAsync(requisition, cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(RemoveRequisitionLineCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (requisition!.RemoveLine(request.LineNumber, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        AuditDraftChange(requisition, "lines", request.LineNumber);
        return await CommitAsync(requisition, cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(AddRequisitionAttachmentCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken, draftOnly: false);
        if (failure is not null)
            return failure;
        var (attachment, error) = requisition!.AddAttachment(Guid.NewGuid(), request.FileName, request.MediaType, request.Length, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();

        writer.Audit(RequisitionAudit.AttachmentAdded, RequisitionAudit.Resource, requisition.Id, ruleIds: ["FIL-002", "FIL-004"],
            details: new Dictionary<string, string> { ["attachmentId"] = attachment!.Id.ToString(), ["revision"] = Text(requisition.Revision) });
        return await CommitAsync(requisition, cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(RemoveRequisitionAttachmentCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadEditableAsync(request.Id, request.ExpectedVersion, cancellationToken, draftOnly: false);
        if (failure is not null)
            return failure;
        if (requisition!.RemoveAttachment(request.AttachmentId, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(RequisitionAudit.AttachmentRemoved, RequisitionAudit.Resource, requisition.Id, ruleIds: ["FIL-004"],
            details: new Dictionary<string, string> { ["attachmentId"] = request.AttachmentId.ToString() });
        return await CommitAsync(requisition, cancellationToken);
    }

    /// <summary>404 when missing or unreadable (REQ-011), 403 when not editable by the actor (REQ-001), then 428/412.</summary>
    private async Task<(PurchaseRequisition? Requisition, ApplicationError? Error)> LoadEditableAsync(
        Guid id, long? expectedVersion, CancellationToken cancellationToken, bool draftOnly = true)
    {
        var requisition = await requisitions.FindAsync(id, cancellationToken);
        if (requisition is null || !await access.CanReadAsync(requisition, cancellationToken))
            return (null, RequisitionAudit.NotFound());
        if (!await access.CanEditAsync(requisition, cancellationToken))
            return (null, ProcurementWriter.Forbidden("Only the requester or a proxy with requisitions.edit-any may change this requisition."));
        if (VersionPrecondition.Check(expectedVersion, requisition.Version) is { } precondition)
            return (null, precondition);
        if (draftOnly && requisition.Status != RequisitionStatus.Draft)
            return (null, RequisitionErrors.Immutable.ToApplicationError());
        return (requisition, null);
    }

    private async Task<(RequisitionLineInput? Input, ApplicationError? Error)> ResolveLineAsync(RequisitionLineRequest line, CancellationToken cancellationToken)
    {
        if (!BudgetMoney.TryParse(line.Quantity, out var quantity))
            return (null, ApplicationError.Validation("quantity", "The quantity must be a decimal string such as \"2.5\"."));
        if (!BudgetMoney.TryParse(line.EstimatedUnitPrice?.Amount, out var price))
            return (null, ApplicationError.Validation("estimatedUnitPrice.amount", "The amount must be a decimal string such as \"12.50\"."));

        Guid categoryId;
        Guid unitId;
        RequisitionLineKind kind;
        string? description = line.Description;
        MasterDataReference? item = null;
        if (line.CatalogItemId is { } catalogItemId)
        {
            var catalogItem = await references.FindCatalogItemAsync(catalogItemId, cancellationToken);
            if (catalogItem is null)
                return (null, ApplicationError.Validation("catalogItemId", "The catalog item does not exist."));
            item = new MasterDataReference(catalogItem.Id, catalogItem.IsActive);
            categoryId = catalogItem.CategoryId;
            unitId = catalogItem.UnitOfMeasureId;
            kind = Enum.Parse<RequisitionLineKind>(catalogItem.Kind);
            description = string.IsNullOrWhiteSpace(description) ? catalogItem.Description : description;
        }
        else
        {
            categoryId = line.CategoryId!.Value;
            unitId = line.UnitOfMeasureId!.Value;
            kind = line.Kind!.Value;
        }

        var category = await references.FindCategoryAsync(categoryId, cancellationToken);
        if (category is null)
            return (null, ApplicationError.Validation("categoryId", "The category does not exist."));
        var unit = await references.FindUnitAsync(unitId, cancellationToken);
        if (unit is null)
            return (null, ApplicationError.Validation("unitOfMeasureId", "The unit of measure does not exist."));

        return (new RequisitionLineInput(
            item, description, kind, new CategorySnapshot(category.Id, category.Code, category.Name, category.IsActive),
            new UnitSnapshot(unit.Id, unit.Code, unit.Name, unit.AllowedDecimals, unit.IsActive), quantity,
            new Money(price, line.EstimatedUnitPrice!.Currency?.Trim().ToUpperInvariant() ?? string.Empty)), null);
    }

    private async Task<CostCenterSnapshot?> LoadCostCenterAsync(Guid id, CancellationToken cancellationToken) =>
        await costCenters.FindAsync(id, cancellationToken) is { } costCenter
            ? new CostCenterSnapshot(costCenter.Id, costCenter.Code, costCenter.Name, costCenter.DepartmentId, costCenter.IsActive)
            : null;

    private static CostCenterSnapshot Snapshot(PurchaseRequisition requisition) =>
        new(requisition.CostCenterId, requisition.CostCenterCode, requisition.CostCenterName, requisition.DepartmentId, false);

    private void AuditDraftChange(PurchaseRequisition requisition, string fields, int? lineNumber = null)
    {
        var details = new Dictionary<string, string> { ["fields"] = fields };
        if (lineNumber is { } number)
            details["lineNumber"] = Text(number);
        writer.Audit(RequisitionAudit.DraftUpdated, RequisitionAudit.Resource, requisition.Id, ruleIds: ["REQ-001", "REQ-002"], details: details);
    }

    private async Task<Result<RequisitionView>> CommitAsync(PurchaseRequisition requisition, CancellationToken cancellationToken)
    {
        var commit = await writer.CommitAsync(token => requisitions.GetVersionAsync(requisition.Id, token), null, cancellationToken);
        return commit is null ? await access.ViewAsync(requisition, cancellationToken) : commit;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
