using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Results;
using Template.Application.Features.Suppliers;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Optional, advisory supplier enrichment (ADR-0007, INT-001). Proposals are never stored by these endpoints; the
/// user accepts them through <c>POST /api/v2/suppliers/{id}/enrichment-acceptances</c>. A disabled, slow or failing
/// provider answers <c>503 integration.provider-unavailable</c> and never blocks manual entry.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/enrichment")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class EnrichmentController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>Look up company registration data for a CNPJ (digits or letters, punctuation ignored).</summary>
    [HttpGet("company-registrations/{taxId}")]
    [HasPermission(Permissions.SuppliersManage)]
    [ProblemCodes(ProblemCodes.SupplierTaxIdInvalid, ProblemCodes.NotFound, ProblemCodes.ProviderRejected, ProblemCodes.ProviderUnavailable,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CompanyRegistrationProposal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IActionResult> CompanyRegistration(string taxId, CancellationToken cancellationToken) =>
        SendAsync(new LookupCompanyRegistrationQuery(taxId), Ok, cancellationToken);

    /// <summary>Look up a Brazilian postal code (CEP, 8 digits).</summary>
    [HttpGet("postal-addresses/{postalCode}")]
    [HasPermission(Permissions.SuppliersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.ProviderRejected, ProblemCodes.ProviderUnavailable,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PostalAddressProposal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IActionResult> PostalAddress(string postalCode, CancellationToken cancellationToken) =>
        SendAsync(new LookupPostalCodeQuery(postalCode), Ok, cancellationToken);
}
