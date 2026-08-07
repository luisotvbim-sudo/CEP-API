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
    public DbSet<PluginUsageEvent> PluginUsageEvents => Set<PluginUsageEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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

        builder.Entity<PluginUsageEvent>(entity =>
        {
            entity.ToTable("plugin_usage_events");
            entity.Property(x => x.Product).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Command).HasMaxLength(100);
            entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.Property(x => x.PluginVersion).HasMaxLength(50);
            entity.Property(x => x.HostVersion).HasMaxLength(50);
            entity.Property(x => x.InstallationId).HasMaxLength(200);
            entity.HasIndex(x => new { x.OrganizationId, x.ClientEventId }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.OccurredAt });
            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.OccurredAt });
            entity.HasIndex(x => new { x.OrganizationId, x.Product, x.Command, x.OccurredAt });
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
