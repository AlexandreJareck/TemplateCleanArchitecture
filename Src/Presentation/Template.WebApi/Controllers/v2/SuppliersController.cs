using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Suppliers;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Suppliers (SUP-001..006). Contacts and addresses are personal/commercial data readable only with
/// <c>suppliers.read</c> (SUP-006). Status transitions are action sub-resources with <c>suppliers.status.change</c>.
/// Never deleted (ORG-005).
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/suppliers")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class SuppliersController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List suppliers (summary representation without contacts or addresses).</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>legalName</c>, <c>status</c>, <c>createdAt</c>, <c>updatedAt</c>; prefix <c>-</c> for descending (default <c>legalName</c>).</param>
    /// <param name="q">Case-insensitive text matched against legal and trade name.</param>
    /// <param name="status">Comma-separated statuses: <c>Draft</c>, <c>Active</c>, <c>Suspended</c>, <c>Inactive</c>.</param>
    /// <param name="country">ISO 3166-1 alpha-2 country.</param>
    /// <param name="taxId">Tax identifier (punctuation ignored; CNPJ when no country is given).</param>
    /// <param name="categoryId">Linked category.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.SuppliersRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<SupplierSummaryView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? q, [FromQuery] string? status,
        [FromQuery] string? country, [FromQuery] string? taxId, [FromQuery] Guid? categoryId, CancellationToken cancellationToken) =>
        SendAsync(new ListSuppliersQuery(page, pageSize, sort, q, status, country, taxId, categoryId), Ok, cancellationToken);

    /// <summary>Get one supplier with contacts, addresses, categories, status history and enrichment provenance.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.SuppliersRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetSupplierQuery(id), Versioned, cancellationToken);

    /// <summary>Create a <c>Draft</c> supplier; the tax ID is normalized and unique per country (SUP-001). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.SuppliersManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SupplierTaxIdInvalid, ProblemCodes.SupplierTaxIdDuplicate, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CreateSupplierRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateSupplierCommand(request.LegalName, request.TradeName, request.Country, request.TaxId),
            view => CreatedVersioned($"/api/v2/suppliers/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Change legal and trade names; country and tax ID only while <c>Draft</c>. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.SuppliersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SupplierTaxIdInvalid, ProblemCodes.SupplierTaxIdDuplicate, ProblemCodes.NotFound,
        ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Update(Guid id, [FromBody] CreateSupplierRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateSupplierCommand(id, IfMatch, request.LegalName, request.TradeName, request.Country, request.TaxId), Versioned, cancellationToken);

    /// <summary>Replace the contact list (entries with <c>id</c> are updated, without are added, missing are removed). Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}/contacts")]
    [HasPermission(Permissions.SuppliersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.SupplierActivationIncomplete, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> ReplaceContacts(Guid id, [FromBody] SupplierContactsRequest request, CancellationToken cancellationToken) =>
        SendAsync(new ReplaceSupplierContactsCommand(id, IfMatch, request.Contacts), Versioned, cancellationToken);

    /// <summary>Replace the address list (same semantics as contacts). Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}/addresses")]
    [HasPermission(Permissions.SuppliersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.SupplierActivationIncomplete, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> ReplaceAddresses(Guid id, [FromBody] SupplierAddressesRequest request, CancellationToken cancellationToken) =>
        SendAsync(new ReplaceSupplierAddressesCommand(id, IfMatch, request.Addresses?.Select(address => address.ToInput()).ToArray()),
            Versioned, cancellationToken);

    /// <summary>Replace the linked categories; newly linked categories must be active (CAT-002). Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}/categories")]
    [HasPermission(Permissions.SuppliersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.CatalogInactive, ProblemCodes.SupplierActivationIncomplete,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> SetCategories(Guid id, [FromBody] SupplierCategoriesRequest request, CancellationToken cancellationToken) =>
        SendAsync(new SetSupplierCategoriesCommand(id, IfMatch, request.CategoryIds), Versioned, cancellationToken);

    /// <summary>Accept (possibly edited) enrichment values and record their provenance (INT-002). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/enrichment-acceptances")]
    [HasPermission(Permissions.SuppliersManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> AcceptEnrichment(Guid id, [FromBody] AcceptEnrichmentRequest request, CancellationToken cancellationToken) =>
        SendAsync(new AcceptSupplierEnrichmentCommand(id, IfMatch, request.Provider, request.RetrievedAt, request.LegalName, request.TradeName,
            request.RegistrationStatus, request.Address?.ToInput()), Versioned, cancellationToken);

    /// <summary><c>Draft → Active</c>; requires complete data (SUP-002). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.SuppliersStatusChange)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.SupplierActivationIncomplete, ProblemCodes.InvalidTransition, ProblemCodes.NotFound, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Activate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        Transition(id, SupplierStatusTransition.Activate, request, cancellationToken);

    /// <summary><c>Active → Suspended</c> with reason (SUP-004, SUP-005). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/suspend")]
    [HasPermission(Permissions.SuppliersStatusChange)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.InvalidTransition, ProblemCodes.NotFound, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> Suspend(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        Transition(id, SupplierStatusTransition.Suspend, request, cancellationToken);

    /// <summary><c>Active/Suspended → Inactive</c> with reason (SUP-004, SUP-005). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/inactivate")]
    [HasPermission(Permissions.SuppliersStatusChange)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.InvalidTransition, ProblemCodes.NotFound, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> Inactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        Transition(id, SupplierStatusTransition.Inactivate, request, cancellationToken);

    /// <summary><c>Suspended/Inactive → Active</c> with reason; data must be complete (SUP-002, SUP-005). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(Permissions.SuppliersStatusChange)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.SupplierActivationIncomplete, ProblemCodes.InvalidTransition, ProblemCodes.NotFound,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(SupplierView), StatusCodes.Status200OK)]
    public Task<IActionResult> Reactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        Transition(id, SupplierStatusTransition.Reactivate, request, cancellationToken);

    private Task<IActionResult> Transition(Guid id, SupplierStatusTransition transition, ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new ChangeSupplierStatusCommand(id, IfMatch, transition, request?.Reason), Versioned, cancellationToken);

    private IActionResult Versioned(SupplierView view) => Versioned(view, view.Version);
}
