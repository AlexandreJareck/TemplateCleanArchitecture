using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Approvals;
using Template.Application.Security;
using Template.Domain.Approvals;
using Template.Domain.Common;
using Template.Domain.Requisitions;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Approval module tables (APR-*, Phase 31). Policy versions are immutable rows (APR-017) and version 1 is seeded by the
/// migration so every host (WebApi, Worker, WorkflowFunctions) finds it without a seeding step. Steps and history belong
/// to the instance aggregate (one version token, SP-1); history is append-only. One instance per award (APR-001).
/// </summary>
public sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public static readonly ApprovalPolicy Version1 = ApprovalPolicy.Version1(SeededRoles.FinanceApprover, SeededRoles.ProcurementManager);

    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.ToTable("ApprovalPolicies", table =>
        {
            table.HasCheckConstraint("CK_ApprovalPolicies_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_ApprovalPolicies_MaxEscalations", "\"MaxEscalations\" >= 0");
        });
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(policy => policy.EscalationRole).HasMaxLength(ApprovalPolicyLevel.RoleMaxLength).IsRequired();
        builder.Property(policy => policy.Reason).HasMaxLength(ApprovalPolicy.ReasonMaxLength);
        builder.Ignore(policy => policy.IsInUse);
        builder.Ignore(policy => policy.Reference);
        builder.HasIndex(policy => policy.Version).IsUnique().HasDatabaseName(IApprovalPolicyRepository.VersionUniqueKey);
        builder.HasIndex(policy => policy.EffectiveFromUtc);
        builder.HasMany(policy => policy.Levels).WithOne().HasForeignKey(level => level.PolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(policy => policy.Levels).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasData(new
        {
            Version1.Id,
            Version1.Version,
            Version1.EffectiveFromUtc,
            Version1.BaseCurrency,
            Version1.StepDuration,
            Version1.ReminderAfter,
            Version1.EscalationRole,
            Version1.MaxEscalations,
            Version1.Reason,
            CreatedBy = (Guid?)null,
            Version1.CreatedAtUtc,
            FirstUsedAtUtc = (DateTime?)null
        });
    }
}

public sealed class ApprovalPolicyLevelConfiguration : IEntityTypeConfiguration<ApprovalPolicyLevel>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicyLevel> builder)
    {
        builder.ToTable("ApprovalPolicyLevels", table =>
        {
            table.HasCheckConstraint("CK_ApprovalPolicyLevels_Level", "\"Level\" >= 1");
            table.HasCheckConstraint("CK_ApprovalPolicyLevels_Minimum", "\"MinimumAmountExclusive\" >= 0");
        });
        builder.HasKey(level => level.Id);
        builder.Property(level => level.Id).ValueGeneratedNever();
        builder.Property(level => level.MinimumAmountExclusive).HasPrecision(18, 2);
        builder.Property(level => level.Resolver).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(level => level.Role).HasMaxLength(ApprovalPolicyLevel.RoleMaxLength);
        builder.HasIndex(level => new { level.PolicyId, level.Level }).IsUnique();
        builder.HasData(ApprovalPolicyConfiguration.Version1.Levels.Select(level => new
        {
            level.Id,
            level.PolicyId,
            level.Level,
            level.MinimumAmountExclusive,
            level.Resolver,
            level.Role
        }).ToArray());
    }
}

public sealed class ApprovalInstanceConfiguration : IEntityTypeConfiguration<ApprovalInstance>
{
    public void Configure(EntityTypeBuilder<ApprovalInstance> builder)
    {
        builder.ToTable("ApprovalInstances", table =>
        {
            table.HasCheckConstraint("CK_ApprovalInstances_Amounts", "\"AmountEvaluated\" > 0 AND \"RequisitionAmount\" > 0");
            table.HasCheckConstraint("CK_ApprovalInstances_CurrentLevel", "\"CurrentLevel\" >= 1");
        });
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.Id).ValueGeneratedNever();
        builder.Property(instance => instance.Version).IsConcurrencyToken();
        builder.Property(instance => instance.RequisitionNumber).HasMaxLength(PurchaseRequisition.NumberMaxLength).IsRequired();
        builder.Property(instance => instance.AmountEvaluated).HasPrecision(18, 2);
        builder.Property(instance => instance.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(instance => instance.RequisitionAmount).HasPrecision(18, 2);
        builder.Property(instance => instance.RequisitionCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(instance => instance.EscalationRole).HasMaxLength(ApprovalPolicyLevel.RoleMaxLength).IsRequired();
        builder.Property(instance => instance.WorkflowInstanceId).HasMaxLength(64).IsRequired();
        builder.Property(instance => instance.WorkflowVersion).HasMaxLength(32).IsRequired();
        builder.Property(instance => instance.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(instance => instance.PendingOutcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(instance => instance.EndCause).HasMaxLength(ApprovalInstance.CauseMaxLength);
        builder.Property(instance => instance.EndReason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Ignore(instance => instance.PolicyReference);
        builder.Ignore(instance => instance.FinalLevel);
        builder.Ignore(instance => instance.CurrentStep);
        builder.Ignore(instance => instance.IsEnded);
        builder.Ignore(instance => instance.NextAction);
        builder.Ignore(instance => instance.NextTimer);
        builder.Ignore(instance => instance.DomainEvents);
        builder.HasIndex(instance => instance.AwardId).IsUnique().HasDatabaseName(IApprovalInstanceRepository.AwardUniqueKey);
        builder.HasIndex(instance => instance.WorkflowInstanceId).IsUnique();
        builder.HasIndex(instance => new { instance.Status, instance.UpdatedAtUtc });
        builder.HasIndex(instance => new { instance.SupplierId, instance.Status });
        builder.HasIndex(instance => instance.RequisitionId);
        builder.HasIndex(instance => instance.RequesterId);
        builder.HasOne<PurchaseRequisition>().WithMany().HasForeignKey(instance => instance.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApprovalPolicy>().WithMany().HasForeignKey(instance => instance.PolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(instance => instance.Steps).WithOne().HasForeignKey(step => step.InstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(instance => instance.History).WithOne().HasForeignKey(entry => entry.InstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(instance => instance.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(instance => instance.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("ApprovalSteps", table =>
        {
            table.HasCheckConstraint("CK_ApprovalSteps_Level", "\"Level\" >= 1");
            table.HasCheckConstraint("CK_ApprovalSteps_Assignee", "NOT (\"AssigneeUserId\" IS NOT NULL AND \"AssigneeRole\" IS NOT NULL)");
        });
        builder.HasKey(step => step.Id);
        builder.Property(step => step.Id).ValueGeneratedNever();
        builder.Property(step => step.Resolver).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(step => step.ResolverRole).HasMaxLength(ApprovalPolicyLevel.RoleMaxLength);
        builder.Property(step => step.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(step => step.AssigneeRole).HasMaxLength(ApprovalPolicyLevel.RoleMaxLength);
        builder.Property(step => step.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(step => step.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.HasIndex(step => new { step.InstanceId, step.Level }).IsUnique();
        builder.HasIndex(step => new { step.AssigneeUserId, step.Status });
        builder.HasIndex(step => new { step.AssigneeRole, step.Status });
    }
}

public sealed class ApprovalHistoryEntryConfiguration : IEntityTypeConfiguration<ApprovalHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ApprovalHistoryEntry> builder)
    {
        builder.ToTable("ApprovalHistory");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasMaxLength(ApprovalHistoryEntry.ActionMaxLength).IsRequired();
        builder.Property(entry => entry.AssigneeRole).HasMaxLength(ApprovalPolicyLevel.RoleMaxLength);
        builder.Property(entry => entry.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(ApprovalHistoryEntry.CorrelationIdMaxLength).IsRequired();
        builder.HasIndex(entry => new { entry.InstanceId, entry.Sequence }).IsUnique();
        builder.HasIndex(entry => entry.ActorId);
    }
}
