#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Template.Application.Interfaces;
using Template.Domain.Products.Entities;
using Template.Infrastructure.Persistence.Extensions;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Infrastructure.Persistence.Contexts;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IAuthenticatedUserService authenticatedUser) : DbContext(options)
{
    public DbSet<ProductEntity> Products { get; set; }
    public DbSet<OutboxMessage> MessagingOutbox { get; set; }
    public DbSet<InboxMessage> MessagingInbox { get; set; }
    public DbSet<ProcurementProjectionState> MessagingProcurementProjections { get; set; }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        ChangeTracker.ApplyAuditing(authenticatedUser);

        return base.SaveChangesAsync(cancellationToken);
    }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

