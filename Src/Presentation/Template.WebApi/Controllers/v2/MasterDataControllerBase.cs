using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Results;
using Template.WebApi.Infrastructure.Http;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Shared HTTP plumbing of the Phase 25 master-data controllers: Problem Details mapping (B23-8), the strong
/// <c>ETag</c> on every representation of a versioned aggregate and <c>If-Match</c> reading (api-guidelines §4).
/// </summary>
public abstract class MasterDataControllerBase(ISender sender, ApiResultMapper resultMapper) : BaseApiController
{
    /// <summary><c>null</c> when absent (→ 428), <c>0</c> when malformed (→ 412).</summary>
    protected long? IfMatch => VersionHeaders.ReadIfMatch(Request);

    protected async Task<IActionResult> SendAsync<T>(IRequest<Result<T>> request, Func<T, IActionResult> onSuccess, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request, cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () => onSuccess(result.Value));
    }

    /// <summary>200 with the representation and its <c>ETag</c>.</summary>
    protected IActionResult Versioned(object representation, string version)
    {
        VersionHeaders.SetETag(Response, long.Parse(version, CultureInfo.InvariantCulture));
        return Ok(representation);
    }

    /// <summary>201 with the representation, its <c>ETag</c> and the <c>Location</c> of the new resource.</summary>
    protected IActionResult CreatedVersioned(string location, object representation, string version)
    {
        VersionHeaders.SetETag(Response, long.Parse(version, CultureInfo.InvariantCulture));
        return Created(location, representation);
    }
}
