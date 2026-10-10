using FluentValidation;
using MediatR;
using Shouldly;
using Template.Application.Behaviors;

namespace Template.Unit.Tests.ApplicationTests.Behaviors;

public class ValidationBehaviorTests
{
    public sealed class SampleRequest : IRequest<string>
    {
        public string? Name { get; init; }
    }

    private sealed class NameRequiredValidator : AbstractValidator<SampleRequest>
    {
        public NameRequiredValidator() => RuleFor(request => request.Name).NotEmpty();
    }

    private sealed class TokenCapturingValidator : AbstractValidator<SampleRequest>
    {
        public CancellationToken? CapturedToken { get; private set; }

        public TokenCapturingValidator()
        {
            RuleFor(request => request.Name).MustAsync((_, cancellationToken) =>
            {
                CapturedToken = cancellationToken;
                return Task.FromResult(true);
            });
        }
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNextAndReturnsItsResponse()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);
        var nextCalled = false;

        var response = await behavior.Handle(new SampleRequest { Name = "valid" }, _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, CancellationToken.None);

        nextCalled.ShouldBeTrue();
        response.ShouldBe("handled");
    }

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([]);

        var response = await behavior.Handle(new SampleRequest(), _ => Task.FromResult("handled"), CancellationToken.None);

        response.ShouldBe("handled");
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationExceptionAndDoesNotCallNext()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);
        var nextCalled = false;

        var exception = await Should.ThrowAsync<ValidationException>(() => behavior.Handle(new SampleRequest { Name = "" }, _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, CancellationToken.None));

        nextCalled.ShouldBeFalse();
        exception.Errors.ShouldContain(failure => failure.PropertyName == nameof(SampleRequest.Name));
    }

    [Fact]
    public async Task Handle_AggregatesFailuresFromAllValidators()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator(), new NameRequiredValidator()]);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            behavior.Handle(new SampleRequest(), _ => Task.FromResult("handled"), CancellationToken.None));

        exception.Errors.Count().ShouldBe(2);
    }

    [Fact]
    public async Task Handle_PassesCancellationTokenToValidatorsAndNext()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var validator = new TokenCapturingValidator();
        var behavior = new ValidationBehavior<SampleRequest, string>([validator]);
        CancellationToken? tokenReceivedByNext = null;

        await behavior.Handle(new SampleRequest { Name = "valid" }, token =>
        {
            tokenReceivedByNext = token;
            return Task.FromResult("handled");
        }, cancellationTokenSource.Token);

        validator.CapturedToken.ShouldBe(cancellationTokenSource.Token);
        tokenReceivedByNext.ShouldBe(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task Handle_CancelledToken_ThrowsOperationCanceledAndDoesNotCallNext()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);
        var nextCalled = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new SampleRequest { Name = "valid" }, _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, cancellationTokenSource.Token));

        nextCalled.ShouldBeFalse();
    }
}
