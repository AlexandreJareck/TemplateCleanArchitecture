namespace Template.Application.Wrappers;
public enum ErrorCode : short
{
    ModelStateNotValid = 0,
    FieldDataInvalid = 1,
    NotFound = 2,
    AccessDenied = 3,
    ErrorInIdentity = 4,
    Exception = 5,
    // Phase 24: appended for the api-guidelines categories; existing values are never renumbered.
    BusinessRuleViolation = 6,
    PreconditionFailed = 7,
    PreconditionRequired = 8,
    RateLimited = 9,
    Gone = 10,
    Conflict = 11,
}
