using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Persistence;

internal static class WorkforceModel
{
    public static void Configure(ModelBuilder builder)
    {
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
