#nullable enable
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Template.Application.Features.Reporting;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Logging;
using Template.Application.Services.Files;
using Template.Application.Services.Procurement;
using Template.Domain.Files;
using Template.Domain.Reporting;

namespace Template.Application.Services.Reporting;

public enum ReportProcessingOutcome
{
    /// <summary>Nothing was due.</summary>
    Idle,
    Ready,
    RetryScheduled,
    Failed,

    /// <summary>Another Worker took the job over (expired lease); this attempt's output was discarded.</summary>
    LeaseLost,

    /// <summary>Graceful shutdown handed the job back without consuming the attempt.</summary>
    Released
}

/// <summary>
/// Worker use case of one report attempt (workflows §6, RPT-003/005, FIL-001/002): claim the oldest due job with a
/// leased row lock, build the content from server-owned sources at the job's <c>AsOfUtc</c>, render it with the
/// versioned template, check its signature and size, record the <see cref="StoredFile"/> as Pending, stream the bytes to
/// private storage, re-read them to verify length and SHA-256, and only then mark the file Available and the job Ready
/// with the completion event in one commit. Failures expose a catalogued safe code; storage or renderer failures never
/// touch procurement state.
/// </summary>
public sealed class ReportJobProcessor(
    IReportJobQueue queue,
    IReportJobRepository jobs,
    IStoredFileRepository files,
    IFileStorage storage,
    IMalwareScanner scanner,
    IEnumerable<IReportBuilder> builders,
    IPdfRenderer pdfRenderer,
    ISpreadsheetWriter spreadsheetWriter,
    ProcurementWriter writer,
    ILogger<ReportJobProcessor> logger)
{
    public const string ReportOwnerType = "ReportJob";
    public const string ReadyAction = "report.ready";
    public const string FailedAction = "report.failed";
    public const string StoredAction = "file.stored";
    public const string MeterName = "Template.Reports";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "reports.job_duration", "s", "Duration of report attempts by type and outcome.");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("reports.job_outcomes", description: "Report attempt outcomes.");

    public async Task<(ReportProcessingOutcome Outcome, Guid? JobId)> ProcessNextAsync(string owner, CancellationToken cancellationToken)
    {
        var policy = ReportPolicy.V1;
        var job = await ClaimAsync(owner, policy, cancellationToken);
        if (job is null)
            return (ReportProcessingOutcome.Idle, null);
        if (job.Status == ReportJobStatus.Failed)
            return (ReportProcessingOutcome.Failed, job.Id);

        var started = Stopwatch.GetTimestamp();
        var outcome = await RunAttemptAsync(job, owner, policy, cancellationToken);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>("report_type", job.ReportType.ToString()), new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        Outcomes.Add(1, new KeyValuePair<string, object?>("report_type", job.ReportType.ToString()), new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        return (outcome, job.Id);
    }

    private async Task<ReportJob?> ClaimAsync(string owner, ReportPolicy policy, CancellationToken cancellationToken)
    {
        await writer.BeginAsync(cancellationToken);
        var job = await queue.LockNextDueAsync(writer.UtcNow, cancellationToken);
        if (job is null)
        {
            await writer.RollbackAsync();
            return null;
        }

        var (claimed, error) = job.Claim(owner, policy, writer.UtcNow);
        if (error is not null)
        {
            await writer.RollbackAsync();
            return null;
        }

        if (!claimed)
            writer.Audit(FailedAction, ReportJobHandlers.ReportResource, job.Id, ReportJobStatus.Running.ToString(), job.Status.ToString(),
                ruleIds: ["RPT-005"], details: new Dictionary<string, string> { ["failureCode"] = job.FailureCode! });
        var staged = writer.Stage(job.DomainEvents);
        if (await writer.CommitAsync(token => jobs.GetVersionAsync(job.Id, token), null, cancellationToken) is not null)
            return null;
        writer.Record(staged);
        job.ClearDomainEvents();
        if (claimed)
            logger.LogInformation(LogEvents.ReportJobClaimed, "Report job {ReportJobId} of type {ReportType} claimed for attempt {Attempt}",
                job.Id, job.ReportType, job.AttemptCount);
        else
            logger.LogWarning(LogEvents.ReportJobFailed, "Report job {ReportJobId} failed with {FailureCode}", job.Id, job.FailureCode);
        return job;
    }

    private async Task<ReportProcessingOutcome> RunAttemptAsync(ReportJob job, string owner, ReportPolicy policy, CancellationToken cancellationToken)
    {
        StoredFile? pendingFile = null;
        var tempPath = Path.Combine(Path.GetTempPath(), $"report-{job.Id:N}-{Guid.NewGuid():N}.tmp");
        try
        {
            var context = new ReportBuildContext(job, ReportParameters.Parse(job.ParametersJson), writer.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(job.TimeZoneId), policy);
            var builder = builders.Single(candidate => candidate.Type == job.ReportType);
            var content = await builder.BuildAsync(context, cancellationToken);
            if (!await ProgressAsync(job, owner, 40, "rendering", policy, cancellationToken))
                return ReportProcessingOutcome.LeaseLost;

            var mediaType = ReportCatalog.MediaType(job.ReportType);
            var maxBytes = FileRetentionPolicy.V1.MaxReportOutputBytes;
            await using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            if (job.Format == ReportFormat.Pdf)
                await pdfRenderer.RenderAsync(content.Pdf ?? throw new InvalidOperationException("A PDF builder returned no document."), output, cancellationToken);
            else
                await spreadsheetWriter.WriteAsync(content.Workbook ?? throw new InvalidOperationException("A workbook builder returned no workbook."), output, cancellationToken);
            if (output.Length > maxBytes)
                throw new ReportSourceException(ReportFailureCodes.TooLarge, "The rendered output exceeds the size policy.");
            output.Position = 0;
            if (output.Length == 0 || !await FileContent.MatchesAsync(output, mediaType, cancellationToken))
                throw new ReportSourceException(ReportFailureCodes.OutputInvalid, "The rendered output does not match its media type.");
            if (!await ProgressAsync(job, owner, 70, "storing", policy, cancellationToken))
                return ReportProcessingOutcome.LeaseLost;

            // The Pending metadata is committed before any byte is written, so an interrupted write is always selectable
            // by the FIL-005 cleanup (never an untracked blob).
            var now = writer.UtcNow;
            var (file, fileError) = StoredFile.CreatePending(Guid.NewGuid(), ReportOwnerType, job.Id, content.FileName, mediaType,
                FileClassification.Confidential, null, now, ReportCatalog.KeepsOutput(job.ReportType) ? null : now + policy.OutputRetention);
            if (fileError is not null)
                throw new ReportSourceException(ReportFailureCodes.OutputInvalid, "The output metadata is invalid.");
            files.Add(file!);
            if (await writer.CommitAsync(token => jobs.GetVersionAsync(job.Id, token), null, cancellationToken) is not null)
                return await LeaseLostAsync(job, null);
            pendingFile = file;

            var stored = await storage.WriteAsync(file!.Id, output, mediaType, maxBytes, cancellationToken);
            var verified = await FileContent.MeasureAsync(storage, file.Id, cancellationToken);
            if (verified is null || verified != stored || stored.Length != output.Length)
                throw new FileStorageUnavailableException("The stored bytes could not be verified.");
            if (await scanner.ScanAsync(file.Id, mediaType, cancellationToken) == MalwareScanVerdict.Infected)
                throw new ReportSourceException(ReportFailureCodes.OutputInvalid, "The output was rejected by the malware scanner.");

            now = writer.UtcNow;
            if (file.MarkAvailable(stored.Length, stored.Sha256, maxBytes, now) is not null)
                throw new ReportSourceException(ReportFailureCodes.OutputInvalid, "The output could not be confirmed.");
            if (job.Complete(owner, file.Id, content.FileName, stored.Length, policy, now) is not null)
                return await LeaseLostAsync(job, pendingFile);
            writer.Audit(StoredAction, ReportJobHandlers.FileResource, file.Id, StoredFileStatus.Pending.ToString(), file.Status.ToString(), ruleIds: ["FIL-001", "FIL-002"],
                details: new Dictionary<string, string> { ["reportJobId"] = job.Id.ToString(), ["length"] = stored.Length.ToString() });
            writer.Audit(ReadyAction, ReportJobHandlers.ReportResource, job.Id, ReportJobStatus.Running.ToString(), job.Status.ToString(), ruleIds: ["RPT-005"],
                details: new Dictionary<string, string> { ["outputFileId"] = file.Id.ToString(), ["attempt"] = job.AttemptCount.ToString() });
            var staged = writer.Stage(job.DomainEvents);
            if (await writer.CommitAsync(token => jobs.GetVersionAsync(job.Id, token), null, cancellationToken) is not null)
                return await LeaseLostAsync(job, pendingFile);
            writer.Record(staged);
            job.ClearDomainEvents();
            logger.LogInformation(LogEvents.ReportJobCompleted, "Report job {ReportJobId} of type {ReportType} is ready ({Length} bytes)",
                job.Id, job.ReportType, stored.Length);
            return ReportProcessingOutcome.Ready;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await ReleaseAsync(job, owner, pendingFile);
        }
        catch (ReportSourceException exception)
        {
            return await FailAsync(job, owner, exception.FailureCode, transient: false, policy, pendingFile);
        }
        catch (FileContentTooLargeException)
        {
            return await FailAsync(job, owner, ReportFailureCodes.TooLarge, transient: false, policy, pendingFile);
        }
        catch (ReportRendererUnavailableException)
        {
            return await FailAsync(job, owner, ReportFailureCodes.RendererUnavailable, transient: true, policy, pendingFile);
        }
        catch (FileStorageUnavailableException)
        {
            return await FailAsync(job, owner, ReportFailureCodes.StorageUnavailable, transient: true, policy, pendingFile);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return await FailAsync(job, owner, ReportFailureCodes.Timeout, transient: true, policy, pendingFile);
        }
        catch (Exception exception)
        {
            logger.LogWarning(LogEvents.ReportJobFailed, "Report job {ReportJobId} attempt failed with {ExceptionType}", job.Id, exception.GetType().Name);
            return await FailAsync(job, owner, ReportFailureCodes.Unexpected, transient: true, policy, pendingFile);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private async Task<bool> ProgressAsync(ReportJob job, string owner, int percent, string stage, ReportPolicy policy, CancellationToken cancellationToken)
    {
        if (job.ReportProgress(owner, percent, stage, policy, writer.UtcNow) is not null)
            return false;
        return await writer.CommitAsync(token => jobs.GetVersionAsync(job.Id, token), null, cancellationToken) is null;
    }

    private async Task<ReportProcessingOutcome> FailAsync(
        ReportJob job, string owner, string failureCode, bool transient, ReportPolicy policy, StoredFile? pendingFile)
    {
        var current = await ReloadAsync(job.Id);
        await DiscardOutputAsync(pendingFile, "attempt-failed");
        if (current is null)
            return ReportProcessingOutcome.LeaseLost;
        var (final, error) = current.FailAttempt(owner, failureCode, transient, policy, writer.UtcNow);
        if (error is not null)
            return await LeaseLostAsync(current, null);
        if (final)
            writer.Audit(FailedAction, ReportJobHandlers.ReportResource, current.Id, ReportJobStatus.Running.ToString(), current.Status.ToString(),
                ruleIds: ["RPT-005"], details: new Dictionary<string, string> { ["failureCode"] = current.FailureCode!, ["attempt"] = current.AttemptCount.ToString() });
        var staged = writer.Stage(current.DomainEvents);
        if (await writer.CommitAsync(token => jobs.GetVersionAsync(current.Id, token), null, CancellationToken.None) is not null)
            return ReportProcessingOutcome.LeaseLost;
        writer.Record(staged);
        current.ClearDomainEvents();
        logger.LogWarning(LogEvents.ReportJobFailed, "Report job {ReportJobId} attempt {Attempt} failed with {FailureCode}; final: {Final}",
            current.Id, current.AttemptCount, failureCode, final);
        return final ? ReportProcessingOutcome.Failed : ReportProcessingOutcome.RetryScheduled;
    }

    private async Task<ReportProcessingOutcome> ReleaseAsync(ReportJob job, string owner, StoredFile? pendingFile)
    {
        var current = await ReloadAsync(job.Id);
        await DiscardOutputAsync(pendingFile, "worker-shutdown");
        if (current is null || current.ReleaseClaim(owner, writer.UtcNow) is not null)
            return ReportProcessingOutcome.LeaseLost;
        if (await writer.CommitAsync(token => jobs.GetVersionAsync(current.Id, token), null, CancellationToken.None) is not null)
            return ReportProcessingOutcome.LeaseLost;
        logger.LogInformation(LogEvents.ReportJobReleased, "Report job {ReportJobId} released on shutdown", current.Id);
        return ReportProcessingOutcome.Released;
    }

    private async Task<ReportProcessingOutcome> LeaseLostAsync(ReportJob job, StoredFile? pendingFile)
    {
        await writer.RollbackAsync();
        await DiscardOutputAsync(pendingFile, "lease-lost");
        logger.LogWarning(LogEvents.ReportJobLeaseLost, "Report job {ReportJobId} was taken over by another worker; output discarded", job.Id);
        return ReportProcessingOutcome.LeaseLost;
    }

    private async Task<ReportJob?> ReloadAsync(Guid jobId)
    {
        await writer.RollbackAsync();
        return await jobs.FindAsync(jobId, CancellationToken.None);
    }

    /// <summary>Best-effort removal of this attempt's bytes; a failure leaves the Pending row for the FIL-005 cleanup.</summary>
    private async Task DiscardOutputAsync(StoredFile? pendingFile, string reason)
    {
        if (pendingFile is null)
            return;
        try
        {
            await storage.DeleteAsync(pendingFile.Id, CancellationToken.None);
            var file = await files.FindAsync(pendingFile.Id, CancellationToken.None);
            if (file is null || file.Status == StoredFileStatus.Deleted)
                return;
            file.MarkDeleted(reason, writer.UtcNow);
            await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, CancellationToken.None);
        }
        catch (Exception exception) when (exception is FileStorageUnavailableException or IOException or TimeoutException)
        {
            logger.LogWarning(LogEvents.FileStorageFailed, "Discarding output {FileId} failed with {ExceptionType}; left for cleanup",
                pendingFile.Id, exception.GetType().Name);
        }
    }
}
