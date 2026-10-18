using System.Net;
using Microsoft.Extensions.Logging;
using Template.Application.Logging;

namespace Template.Infrastructure.Integrations.Http;

/// <summary>Transport classification of one provider call.</summary>
internal enum ProviderCallStatus
{
    Success,
    NotFound,
    Rejected,
    RateLimited,
    ServerError,
    Timeout,
    NetworkError,
    CircuitOpen
}

internal sealed record ProviderResponse(ProviderCallStatus Status, string? Body, int? StatusCode, int Attempts)
{
    /// <summary>Safe reason string exposed through the Application result (no provider text, no request data).</summary>
    public string Reason => Status switch
    {
        ProviderCallStatus.RateLimited => "rate-limited",
        ProviderCallStatus.ServerError => "server-error",
        ProviderCallStatus.Timeout => "timeout",
        ProviderCallStatus.NetworkError => "unreachable",
        ProviderCallStatus.CircuitOpen => "circuit-open",
        ProviderCallStatus.NotFound => "not-found",
        ProviderCallStatus.Rejected => "rejected",
        _ => "ok"
    };
}

/// <summary>
/// Bounded, idempotent GET execution for one provider (INT-001): a per-attempt timeout, at most
/// <see cref="ProviderSettings.MaxRetries"/> retries only for transient failures (timeout, network error, 408, 5xx),
/// no retry on 429 (the provider asked to slow down), a consecutive-failure circuit breaker, and caller cancellation
/// propagated as <see cref="OperationCanceledException"/>. Logs carry provider, operation, classification, status code
/// and attempts only: never the URL, which contains the CNPJ or CEP, nor any response content.
/// </summary>
internal sealed class ProviderHttpExecutor(
    string provider,
    ProviderSettings settings,
    ProviderCircuitBreaker breaker,
    TimeProvider timeProvider,
    ILogger logger)
{
    public string Provider { get; } = provider;

    public async Task<ProviderResponse> GetAsync(HttpClient client, string relativeUrl, string operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!breaker.TryAcquire())
            return Fail(new ProviderResponse(ProviderCallStatus.CircuitOpen, null, null, 0), operation, countsAsFailure: false);

        ProviderResponse last = new(ProviderCallStatus.NetworkError, null, null, 0);
        for (var attempt = 1; attempt <= settings.MaxRetries + 1; attempt++)
        {
            if (attempt > 1)
            {
                try
                {
                    await Task.Delay(settings.RetryDelay * (attempt - 1), timeProvider, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    breaker.RecordNeutral();
                    throw;
                }
            }

            last = await SendOnceAsync(client, relativeUrl, attempt, cancellationToken);
            if (!IsTransient(last.Status))
                break;
        }

        if (last.Status is ProviderCallStatus.Success or ProviderCallStatus.NotFound or ProviderCallStatus.Rejected)
        {
            breaker.RecordSuccess();
            if (last.Status == ProviderCallStatus.Rejected)
                logger.LogWarning(LogEvents.ProviderCallFailed, "Provider {Provider} {Operation} answered {Classification} ({StatusCode}) after {Attempts} attempt(s)",
                    Provider, operation, last.Status, last.StatusCode, last.Attempts);
            return last;
        }

        return Fail(last, operation, countsAsFailure: true);
    }

    private async Task<ProviderResponse> SendOnceAsync(HttpClient client, string relativeUrl, int attempt, CancellationToken cancellationToken)
    {
        using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptTimeout.CancelAfter(settings.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, attemptTimeout.Token);
            var status = (int)response.StatusCode;
            var classification = response.StatusCode switch
            {
                >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices => ProviderCallStatus.Success,
                HttpStatusCode.NotFound => ProviderCallStatus.NotFound,
                HttpStatusCode.TooManyRequests => ProviderCallStatus.RateLimited,
                HttpStatusCode.RequestTimeout => ProviderCallStatus.ServerError,
                >= HttpStatusCode.InternalServerError => ProviderCallStatus.ServerError,
                _ => ProviderCallStatus.Rejected
            };
            var body = classification == ProviderCallStatus.Success
                ? await response.Content.ReadAsStringAsync(attemptTimeout.Token)
                : null;
            return new ProviderResponse(classification, body, status, attempt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            breaker.RecordNeutral();
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProviderResponse(ProviderCallStatus.Timeout, null, null, attempt);
        }
        catch (HttpRequestException)
        {
            return new ProviderResponse(ProviderCallStatus.NetworkError, null, null, attempt);
        }
    }

    private ProviderResponse Fail(ProviderResponse response, string operation, bool countsAsFailure)
    {
        if (countsAsFailure && breaker.RecordFailure())
            logger.LogWarning(LogEvents.ProviderCircuitOpened, "Circuit for provider {Provider} opened after {Classification}", Provider, response.Status);
        logger.LogWarning(LogEvents.ProviderCallFailed, "Provider {Provider} {Operation} failed: {Classification} ({StatusCode}) after {Attempts} attempt(s)",
            Provider, operation, response.Status, response.StatusCode, response.Attempts);
        return response;
    }

    private static bool IsTransient(ProviderCallStatus status) =>
        status is ProviderCallStatus.ServerError or ProviderCallStatus.Timeout or ProviderCallStatus.NetworkError;
}
