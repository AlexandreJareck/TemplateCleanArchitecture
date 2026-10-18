#nullable enable
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Logging;
using Template.Application.Security;
using Template.Application.Services.Access;
using Template.Application.Settings;
using Template.Domain.Access;

namespace Template.Application.Features.Access.Commands;

/// <summary>Login (SEC-001): verifies credentials and starts a new session family. Does not rotate the security stamp.</summary>
public sealed record CreateSessionCommand(string UserName, string Password) : IRequest<Result<SessionIssued>>
{
    public const int UserNameMaxLength = 256;
    public const int PasswordMaxLength = 128;

    // Keeps the password out of logs and exception messages that format the record.
    public override string ToString() => nameof(CreateSessionCommand);
}

public sealed class CreateSessionCommandHandler(
    IAccountDirectory accounts,
    ISessionTokenService tokens,
    ISessionFamilyRepository sessions,
    IAccessTokenIssuer accessTokens,
    IUnitOfWork unitOfWork,
    SecurityAuditRecorder audit,
    IClock clock,
    IOptions<SessionSettings> settings,
    ILogger<CreateSessionCommandHandler> logger) : IRequestHandler<CreateSessionCommand, Result<SessionIssued>>
{
    public async Task<Result<SessionIssued>> Handle(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        var account = await accounts.VerifyCredentialsAsync(request.UserName, request.Password, cancellationToken);
        if (account is null)
        {
            logger.LogInformation(LogEvents.SessionLoginRejected, "Session login rejected: invalid credentials");
            return AccessErrors.InvalidCredentials;
        }

        var policy = settings.Value.ToPolicy();
        var now = clock.UtcNow;
        var refreshToken = tokens.NewRefreshToken();
        var family = SessionFamily.Start(Guid.NewGuid(), account.UserId, account.SecurityStampFingerprint, refreshToken.Hash, now, policy);

        // SEC-010: the new family counts toward the limit, so the oldest active ones make room for it.
        var active = await sessions.ListActiveAsync(account.UserId, now, cancellationToken);
        foreach (var oldest in active.Take(Math.Max(0, active.Count - policy.MaxActiveFamilies + 1)))
        {
            if (oldest.Revoke(SessionRevocationReason.FamilyLimitExceeded, now))
                audit.SessionEvent(SecurityAuditActions.SessionRevoked, account.UserId, oldest.Id,
                    SessionReasons.Describe(SessionRevocationReason.FamilyLimitExceeded), "Revoked");
        }

        sessions.Add(family);
        audit.SessionEvent(SecurityAuditActions.SessionCreated, account.UserId, family.Id, toState: "Active");
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = await accessTokens.IssueAsync(account.UserId, family.Id, cancellationToken);
        logger.LogInformation(LogEvents.SessionCreated, "Session family {SessionFamilyId} created", family.Id);

        return new SessionIssued(
            family.Id,
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken.Token,
            tokens.CreateCsrfToken(family.Id),
            family.CookieExpiresAtUtc);
    }
}

public sealed class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(command => command.UserName).NotEmpty().MaximumLength(CreateSessionCommand.UserNameMaxLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(CreateSessionCommand.PasswordMaxLength);
    }
}
