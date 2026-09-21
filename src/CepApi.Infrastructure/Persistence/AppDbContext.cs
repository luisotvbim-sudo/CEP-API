using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<ProductAccess> ProductAccesses => Set<ProductAccess>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();
    public DbSet<AllowedEmailDomain> AllowedEmailDomains => Set<AllowedEmailDomain>();
    public DbSet<WorkforceTeam> WorkforceTeams => Set<WorkforceTeam>();
    public DbSet<TeamAssignment> TeamAssignments => Set<TeamAssignment>();
    public DbSet<ExternalWorkforceIdentity> ExternalWorkforceIdentities => Set<ExternalWorkforceIdentity>();
    public DbSet<WorkforcePerson> WorkforcePeople => Set<WorkforcePerson>();
    public DbSet<WorkforceSyncBatch> WorkforceSyncBatches => Set<WorkforceSyncBatch>();
    public DbSet<WorkforceSyncSourceRun> WorkforceSyncSourceRuns => Set<WorkforceSyncSourceRun>();
    public DbSet<WorkforceTimeRecord> WorkforceTimeRecords => Set<WorkforceTimeRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AllowedEmailDomain>(entity =>
        {
            entity.ToTable("allowed_email_domains", table => table.HasCheckConstraint(
                "ck_allowed_email_domains_normalized",
                "domain = lower(domain) AND domain ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$'"));
            entity.HasKey(x => x.Domain);
            entity.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        });

        builder.Entity<EmailOutboxMessage>(entity =>
        {
            entity.ToTable("email_outbox");
            entity.HasIndex(x => x.NextAttemptAt);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(100);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<ProductAccess>(entity =>
        {
            entity.ToTable("product_accesses");
            entity.HasKey(x => new { x.UserId, x.Product });
            entity.Property(x => x.Product).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<ApplicationUser>().WithMany(x => x.ProductAccesses).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Invitation>(entity =>
        {
            entity.ToTable("invitations");
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.CodeHash).HasMaxLength(64);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.Email, x.RevokedAt, x.AcceptedAt });
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.WorkforcePerson).WithMany().HasForeignKey(x => x.WorkforcePersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PasswordReset>(entity =>
        {
            entity.ToTable("password_resets");
            entity.Property(x => x.CodeHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.UserId, x.UsedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("refresh_sessions");
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.ClientType).HasMaxLength(50);
            entity.Property(x => x.ClientVersion).HasMaxLength(50);
            entity.Property(x => x.InstallationId).HasMaxLength(200);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.RevocationReason).HasMaxLength(100);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.FamilyId });
            entity.HasOne<ApplicationUser>().WithMany(x => x.RefreshSessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb");
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
            entity.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<WorkforceTeam>(entity =>
        {
            entity.ToTable("workforce_teams");
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.NormalizedName).HasMaxLength(120);
            entity.HasIndex(x => new { x.OrganizationId, x.NormalizedName }).IsUnique();
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TeamAssignment>(entity =>
        {
            entity.ToTable("team_assignments", table => table.HasCheckConstraint(
                "ck_team_assignments_effective_period", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\""));
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.TeamId, x.UserId, x.Role, x.EffectiveFrom });
            entity.HasIndex(x => new { x.UserId, x.EffectiveFrom, x.EffectiveTo });
            entity.HasOne(x => x.Team).WithMany(x => x.Assignments).HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExternalWorkforceIdentity>(entity =>
        {
            entity.ToTable("external_workforce_identities");
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ExternalId).HasMaxLength(200);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.HasIndex(x => new { x.OrganizationId, x.Source, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Source, x.IsActive });
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkforcePerson>(entity =>
        {
            entity.ToTable("workforce_people");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.HasIndex(x => x.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
            entity.HasIndex(x => x.MondayIdentityId).IsUnique();
            entity.HasIndex(x => x.VrMaisIdentityId).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Email });
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.MondayIdentity).WithMany().HasForeignKey(x => x.MondayIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.VrMaisIdentity).WithMany().HasForeignKey(x => x.VrMaisIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkforceSyncBatch>(entity =>
        {
            entity.ToTable("workforce_sync_batches");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.OrganizationId, x.StartedAt });
            entity.HasIndex(x => x.OrganizationId).IsUnique().HasFilter("\"Status\" = 'Running'");
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkforceSyncSourceRun>(entity =>
        {
            entity.ToTable("workforce_sync_source_runs");
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.Property(x => x.ErrorMessage).HasMaxLength(500);
            entity.HasIndex(x => new { x.BatchId, x.Source }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Source, x.CompletedAt });
            entity.HasOne(x => x.Batch).WithMany(x => x.Sources).HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkforceTimeRecord>(entity =>
        {
            entity.ToTable("workforce_time_records");
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ExternalKey).HasMaxLength(500);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.Url).HasMaxLength(2000);
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OrganizationId, x.Source, x.ExternalKey }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.WorkDate, x.Source });
            entity.HasIndex(x => new { x.ExternalIdentityId, x.WorkDate });
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ExternalIdentity).WithMany().HasForeignKey(x => x.ExternalIdentityId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
