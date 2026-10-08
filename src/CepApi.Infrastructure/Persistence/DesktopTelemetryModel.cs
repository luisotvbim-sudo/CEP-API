using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Persistence;

internal static class DesktopTelemetryModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<DesktopTelemetryEvent>(entity =>
        {
            entity.ToTable("desktop_telemetry_events", "time_control");
            entity.HasKey(x => new { x.OrganizationId, x.UserId, x.EventId });
            entity.HasIndex(x => x.ReceivedAt);
            entity.HasIndex(x => new { x.OrganizationId, x.ReceivedAt });
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.Property(x => x.Phase).HasMaxLength(24);
            entity.Property(x => x.Outcome).HasMaxLength(16);
            entity.Property(x => x.Action).HasMaxLength(16);
            entity.Property(x => x.ErrorCode).HasMaxLength(32);
            entity.Property(x => x.AppVersion).HasMaxLength(32);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
