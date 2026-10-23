#nullable enable
using Microsoft.Extensions.Logging;
using Template.Application.Features.Reporting;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Logging;
using Template.Application.Services.Procurement;
using Template.Domain.Files;
using Template.Domain.Reporting;

namespace Template.Application.Services.Reporting;

public sealed record ReportMaintenanceResult(int ExpiredJobs, int DeletedFiles, int FailedDeletions);

/// <summary>
/// Retention pass run by the Worker (RPT-006, FIL-005): Ready jobs past their 7-day retention become Expired, and the
/// selected files (pending uploads older than 24 h — orphans of interrupted writes — and available files past expiry)
/// have their bytes deleted and their metadata marked Deleted. Each item commits on its own; a storage failure leaves
/// the item selected for the next pass.
/// </summary>
public sealed class ReportMaintenance(
    IReportJobQueue queue,
    IReportJobRepository jobs,
    IStoredFileRepository files,
    IFileStorage storage,
    ProcurementWriter writer,
    ILogger<ReportMaintenance> logger)
{
    public const string ExpiredAction = "report.expired";
    public const string FileExpiredAction = "file.expired";

    public async Task<ReportMaintenanceResult> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = writer.UtcNow;
        var expired = 0;
        foreach (var id in await queue.ListExpiredReadyAsync(now, batchSize, cancellationToken))
        {
            var job = await jobs.FindAsync(id, cancellationToken);
            if (job is null || !job.Expire(now))
                continue;
            writer.Audit(ExpiredAction, ReportJobHandlers.ReportResource, job.Id, ReportJobStatus.Ready.ToString(), job.Status.ToString(), ruleIds: ["RPT-006"]);
            if (await writer.CommitAsync(token => jobs.GetVersionAsync(id, token), null, cancellationToken) is null)
                expired++;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var file in await files.ListCleanupCandidatesAsync(FileRetentionPolicy.V1, now, batchSize, cancellationToken))
        {
            var reason = file.Status == StoredFileStatus.Pending ? "orphan-pending" : "expired";
            try
            {
                await storage.DeleteAsync(file.Id, cancellationToken);
            }
            catch (FileStorageUnavailableException)
            {
                failed++;
                continue;
            }

            var previous = file.Status;
            file.MarkDeleted(reason, writer.UtcNow);
            writer.Audit(FileExpiredAction, ReportJobHandlers.FileResource, file.Id, previous.ToString(), file.Status.ToString(), ruleIds: ["FIL-005"],
                details: new Dictionary<string, string> { ["reason"] = reason, ["ownerType"] = file.OwnerType });
            if (await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, cancellationToken) is null)
                deleted++;
        }

        if (expired + deleted + failed > 0)
            logger.LogInformation(LogEvents.ReportMaintenanceCompleted,
                "Report maintenance expired {ExpiredJobs} jobs, deleted {DeletedFiles} files, {FailedDeletions} deletions deferred", expired, deleted, failed);
        return new ReportMaintenanceResult(expired, deleted, failed);
    }
}
