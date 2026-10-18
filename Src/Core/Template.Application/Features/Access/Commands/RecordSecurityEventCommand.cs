#nullable enable
using MediatR;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Security;
using Template.Application.Services.Access;

namespace Template.Application.Features.Access.Commands;

/// <summary>
/// Audits a security event detected at the HTTP boundary without a business change of its own: a rate-limit rejection
/// (SEC-007), a permission denial (SEC-008) or a rejected request origin (SEC-005). The boundary samples these events; details are allow-listed (AUD-003).
/// </summary>
public sealed record RecordSecurityEventCommand(string Action, string Endpoint, IReadOnlyDictionary<string, string>? Details = null)
    : IRequest<Result>;

public sealed class RecordSecurityEventCommandHandler(
    IAuthenticatedUserService currentUser,
    SecurityAuditRecorder audit,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordSecurityEventCommand, Result>
{
    private static readonly HashSet<string> BoundaryActions =
        new(StringComparer.Ordinal) { SecurityAuditActions.RateLimited, SecurityAuditActions.Forbidden, SecurityAuditActions.CsrfRejected };

    public async Task<Result> Handle(RecordSecurityEventCommand request, CancellationToken cancellationToken)
    {
        if (!BoundaryActions.Contains(request.Action))
            throw new ArgumentException("Only boundary security events are recorded by this command.", nameof(request));

        Guid? actorId = Guid.TryParse(currentUser.UserId, out var userId) ? userId : null;
        audit.EndpointEvent(request.Action, actorId, request.Endpoint, request.Details);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
