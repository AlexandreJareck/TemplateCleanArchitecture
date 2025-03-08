using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Template.Application.Interfaces;
using Template.Domain.Common;

namespace Template.Infrastructure.Persistence.Extensions;
public static class EntityFrameworkExtensions
{
    /// <summary>
    /// Applies auditing information to entities that implement AuditableBaseEntity.
    /// </summary>
    /// <param name="changeTracker">The ChangeTracker instance to track entity changes.</param>
    /// <param name="authenticatedUser">The authenticated user service to get user information.</param>
    public static void ApplyAuditing(this ChangeTracker changeTracker, IAuthenticatedUserService authenticatedUser)
    {
        var userId = string.IsNullOrEmpty(authenticatedUser.UserId)
            ? Guid.Empty
            : Guid.Parse(authenticatedUser.UserId);

        var currentTime = DateTime.UtcNow;

        foreach (var entry in changeTracker.Entries())
        {
            var entityType = entry.Entity.GetType();

            if (typeof(AuditableBaseEntity).IsAssignableFrom(entityType) ||
                (entityType.BaseType?.IsGenericType ?? false) &&
                entityType.BaseType.GetGenericTypeDefinition() == typeof(AuditableBaseEntity<>))
            {
                dynamic auditableEntity = entry.Entity;

                if (entry.State == EntityState.Added)
                {
                    auditableEntity.Created = currentTime;
                    auditableEntity.CreatedBy = userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditableEntity.LastModified = currentTime;
                    auditableEntity.LastModifiedBy = userId;
                }
            }
        }
    }
}
