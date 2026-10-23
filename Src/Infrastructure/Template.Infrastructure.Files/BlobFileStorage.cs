using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Files;
using Template.Application.Logging;

namespace Template.Infrastructure.Files;

/// <summary>Readiness probe of the configured storage (health checks only; no business operation).</summary>
public interface IFileStorageProbe
{
    /// <summary><c>true</c> when the private container is reachable within the timeout.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IFileStorage"/> on Azure Blob-compatible storage (Azurite locally, Azure Blob Storage in Azure, D23-7).
/// One private container (created without public access on first use); blob names are derived from the file ID only
/// (<c>v1/{id:N}</c>) and never leave this class. Writes are create-only (<c>If-None-Match: *</c>), streamed, hashed and
/// size-limited; every network failure, timeout or rejection surfaces as <see cref="FileStorageUnavailableException"/>
/// and is logged with the exception type only (no URL, account name or key).
/// </summary>
public sealed class BlobFileStorage : IFileStorage, IFileStorageProbe
{
    private readonly BlobContainerClient container;
    private readonly FileStorageSettings settings;
    private readonly ILogger<BlobFileStorage> logger;
    private readonly SemaphoreSlim containerGate = new(1, 1);
    private volatile bool containerReady;

    public BlobFileStorage(string connectionString, IOptions<FileStorageSettings> options, ILogger<BlobFileStorage> logger)
    {
        settings = options.Value;
        this.logger = logger;
        var clientOptions = new BlobClientOptions();
        clientOptions.Retry.MaxRetries = settings.MaxRetries;
        clientOptions.Retry.NetworkTimeout = settings.OperationTimeout;
        clientOptions.Diagnostics.IsLoggingEnabled = false;
        clientOptions.Diagnostics.IsTelemetryEnabled = false;
        container = new BlobServiceClient(connectionString, clientOptions).GetBlobContainerClient(settings.ContainerName);
    }

    public static string BlobName(Guid fileId) => $"v1/{fileId:N}";

    public async Task<StoredContent> WriteAsync(Guid fileId, Stream content, string mediaType, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var timeout = Timeout(cancellationToken);
        await using var measured = new MeasuringReadStream(content, maxBytes);
        try
        {
            await EnsureContainerAsync(timeout.Token);
            await container.GetBlobClient(BlobName(fileId)).UploadAsync(measured, new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = new BlobHttpHeaders { ContentType = mediaType },
                TransferOptions = new Azure.Storage.StorageTransferOptions { MaximumConcurrency = 1 }
            }, timeout.Token);
            return new StoredContent(measured.BytesRead, measured.Sha256());
        }
        catch (Exception exception) when (TooLarge(exception) is { } tooLarge)
        {
            throw tooLarge;
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            throw new InvalidOperationException("A stored file is never overwritten.", exception);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(cancellationToken);
        try
        {
            var response = await container.GetBlobClient(BlobName(fileId)).DownloadStreamingAsync(cancellationToken: timeout.Token);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    public async Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(cancellationToken);
        try
        {
            var response = await container.GetBlobClient(BlobName(fileId)).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: timeout.Token);
            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    public async Task<bool> ExistsAsync(Guid fileId, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(cancellationToken);
        try
        {
            return (await container.GetBlobClient(BlobName(fileId)).ExistsAsync(timeout.Token)).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        using var timeout = Timeout(cancellationToken);
        try
        {
            // Always one round trip: the cached "container created" flag says nothing about current reachability.
            if (containerReady)
                await container.GetPropertiesAsync(cancellationToken: timeout.Token);
            else
                await EnsureContainerAsync(timeout.Token);
            return true;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            return false;
        }
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (containerReady)
            return;
        await containerGate.WaitAsync(cancellationToken);
        try
        {
            if (!containerReady)
            {
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                containerReady = true;
            }
        }
        finally
        {
            containerGate.Release();
        }
    }

    private CancellationTokenSource Timeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(settings.OperationTimeout);
        return source;
    }

    private static FileContentTooLargeException? TooLarge(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is FileContentTooLargeException tooLarge)
                return tooLarge;
            if (current is AggregateException aggregate)
                return aggregate.Flatten().InnerExceptions.Select(TooLarge).FirstOrDefault(found => found is not null);
        }

        return null;
    }

    private static bool IsUnavailable(Exception exception, CancellationToken callerToken) =>
        exception is RequestFailedException or HttpRequestException or IOException or TimeoutException or AggregateException
        || (exception is OperationCanceledException && !callerToken.IsCancellationRequested);

    private FileStorageUnavailableException Unavailable(Exception exception)
    {
        logger.LogWarning(LogEvents.FileStorageFailed, "Blob storage operation failed with {ExceptionType}", exception.GetType().Name);
        return new FileStorageUnavailableException("The file storage is unavailable.", exception);
    }
}
