#nullable enable
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Template.Application.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator for <typeparamref name="TRequest"/>
/// before the handler. Failures throw <see cref="ValidationException"/>, which the existing
/// HTTP error middleware already maps to 400 with <c>ModelStateNotValid</c> errors.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            // A fresh context per validator: a shared ValidationContext accumulates failures,
            // which would duplicate errors from earlier validators in later results.
            var context = new ValidationContext<TRequest>(request);
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors.Where(failure => failure is not null));
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}
