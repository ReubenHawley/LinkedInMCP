using Microsoft.EntityFrameworkCore;

namespace LinkedInMcp.Core.Data;

public sealed class LinkedInMcpDbContext(DbContextOptions<LinkedInMcpDbContext> options) : DbContext(options)
{
    public DbSet<LinkedInConnection> LinkedInConnections => Set<LinkedInConnection>();

    public DbSet<LinkedInOAuthState> LinkedInOAuthStates => Set<LinkedInOAuthState>();

    public DbSet<LinkedInOrganizationAccess> LinkedInOrganizationAccess => Set<LinkedInOrganizationAccess>();

    public DbSet<RateLimitLedgerEntry> RateLimitLedger => Set<RateLimitLedgerEntry>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<DeletionRequest> DeletionRequests => Set<DeletionRequest>();

    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LinkedInConnection>()
            .HasIndex(connection => connection.SubjectKey)
            .IsUnique();

        modelBuilder.Entity<LinkedInOAuthState>()
            .HasIndex(state => state.State)
            .IsUnique();

        modelBuilder.Entity<LinkedInOrganizationAccess>()
            .HasIndex(access => new { access.ConnectionId, access.OrganizationUrn })
            .IsUnique();

        modelBuilder.Entity<RateLimitLedgerEntry>()
            .HasIndex(entry => new { entry.DayUtc, entry.EndpointKey, entry.BucketType, entry.BucketId })
            .IsUnique();

        modelBuilder.Entity<WebhookDelivery>()
            .HasIndex(delivery => delivery.NotificationId)
            .IsUnique();
    }
}
