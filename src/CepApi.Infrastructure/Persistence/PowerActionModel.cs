using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Persistence;

internal static class PowerActionModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<PowerActionOverride>(entity =>
        {
            entity.ToTable("power_action_overrides", "time_control", table =>
                table.HasCheckConstraint("ck_power_override_duration", "\"ExpiresAt\" = \"GrantedAt\" + INTERVAL '5 minutes'"));
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.RecipientSecurityVersion).HasMaxLength(64);
            entity.HasIndex(x => new { x.OrganizationId, x.ExpiresAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PowerPinConfiguration>(entity =>
        {
            entity.ToTable("power_pin_configuration", "time_control", table =>
                table.HasCheckConstraint("ck_power_pin_singleton", "\"Id\" = 1"));
            entity.Property(x => x.PinHash).HasMaxLength(512);
        });
    }
}
