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
        TimeNotificationModel.Configure(builder);
        PowerActionModel.Configure(builder);

        IdentityModel.Configure(builder);
        OrganizationAccessModel.Configure(builder);
        WorkforceModel.Configure(builder);
    }
}
