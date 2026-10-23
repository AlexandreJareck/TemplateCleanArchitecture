using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Reporting;
using Template.Domain.Files;
using Template.Domain.Reporting;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Files module metadata (FIL-001): no byte or storage-key column exists; the storage adapter addresses the bytes by
/// the row ID. Length and digest are set together when the file becomes Available.
/// </summary>
public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("FilesStoredFiles", table =>
        {
            table.HasCheckConstraint("CK_FilesStoredFiles_Available",
                "\"Status\" <> 'Available' OR (\"Length\" > 0 AND \"Sha256\" IS NOT NULL AND char_length(\"Sha256\") = 64)");
            table.HasCheckConstraint("CK_FilesStoredFiles_Length", "\"Length\" IS NULL OR \"Length\" > 0");
        });
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.Version).IsConcurrencyToken();
        builder.Property(file => file.OwnerType).HasMaxLength(StoredFile.OwnerTypeMaxLength).IsRequired();
        builder.Property(file => file.OriginalName).HasMaxLength(StoredFile.NameMaxLength).IsRequired();
        builder.Property(file => file.MediaType).HasMaxLength(StoredFile.MediaTypeMaxLength).IsRequired();
        builder.Property(file => file.Sha256).HasMaxLength(StoredFile.Sha256Length).IsFixedLength();
        builder.Property(file => file.Classification).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(file => file.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(file => file.DeletionReason).HasMaxLength(StoredFile.DeletionReasonMaxLength);
        builder.HasIndex(file => new { file.OwnerType, file.OwnerId });
        builder.HasIndex(file => new { file.Status, file.CreatedAtUtc });
        builder.HasIndex(file => new { file.Status, file.ExpiresAtUtc });
    }
}

/// <summary>
/// Report jobs (RPT-001..009). The job table is the durable queue: due rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c> and a version-checked lease; a partial unique index keeps one active job per
/// (requester, type, parameter fingerprint) (RPT-001).
/// </summary>
public sealed class ReportJobConfiguration : IEntityTypeConfiguration<ReportJob>
{
    public void Configure(EntityTypeBuilder<ReportJob> builder)
    {
        builder.ToTable("ReportingReportJobs", table =>
        {
            table.HasCheckConstraint("CK_ReportingReportJobs_Attempts", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 10");
            table.HasCheckConstraint("CK_ReportingReportJobs_Progress", "\"ProgressPercent\" >= 0 AND \"ProgressPercent\" <= 100");
            table.HasCheckConstraint("CK_ReportingReportJobs_ReadyOutput", "\"Status\" <> 'Ready' OR \"OutputFileId\" IS NOT NULL");
        });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.Version).IsConcurrencyToken();
        builder.Property(job => job.ReportType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(job => job.ParametersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.ParametersHash).HasMaxLength(ReportJob.ParametersHashLength).IsFixedLength().IsRequired();
        builder.Property(job => job.TimeZoneId).HasMaxLength(ReportJob.TimeZoneMaxLength).IsRequired();
        builder.Property(job => job.TemplateVersion).HasMaxLength(ReportJob.TemplateVersionMaxLength).IsRequired();
        builder.Property(job => job.ClaimOwner).HasMaxLength(ReportJob.ClaimOwnerMaxLength);
        builder.Property(job => job.ProgressStage).HasMaxLength(ReportJob.StageMaxLength);
        builder.Property(job => job.FailureCode).HasMaxLength(ReportJob.FailureCodeMaxLength);
        builder.Property(job => job.OutputFileName).HasMaxLength(ReportJob.OutputNameMaxLength);
        builder.Ignore(job => job.DomainEvents);
        builder.Ignore(job => job.Format);
        builder.Ignore(job => job.IsActive);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(job => job.OutputFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(job => new { job.RequestedBy, job.ReportType, job.ParametersHash })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Pending', 'Running')")
            .HasDatabaseName(IReportJobRepository.ActiveUniqueKey);
        builder.HasIndex(job => new { job.Status, job.NextAttemptAtUtc });
        builder.HasIndex(job => new { job.Status, job.ClaimExpiresAtUtc });
        builder.HasIndex(job => new { job.Status, job.ExpiresAtUtc });
        builder.HasIndex(job => new { job.RequestedBy, job.RequestedAtUtc });
    }
}
