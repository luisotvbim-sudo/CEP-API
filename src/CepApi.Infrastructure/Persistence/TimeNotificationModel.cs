using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Persistence;

internal static class TimeNotificationModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<TimeControlSettings>(e =>
        {
            e.ToTable("app_settings", "time_control", t =>
            {
                t.HasCheckConstraint("ck_settings_singleton", "\"Id\" = 1");
                t.HasCheckConstraint("ck_tolerance", "\"ToleranceMinutes\" BETWEEN 0 AND 1440");
            });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasData(new TimeControlSettings { Id = 1, ToleranceMinutes = 30, Version = Guid.Parse("53321a5f-3dd2-4eb8-86ec-c9e154f3f011"), UpdatedAt = DateTimeOffset.Parse("2026-09-29T00:00:00Z") });
        });
        b.Entity<TimeNotificationSchedule>(e =>
        {
            e.ToTable("notification_schedules", "time_control");
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.LocalTime).IsUnique().HasFilter("NOT \"IsDeleted\"");
            e.HasData(
                Schedule("03321a5f-3dd2-4eb8-86ec-c9e154f3f011", 10, 0, NotificationScheduleKind.PreviousDay, "Confira as pendências de ontem."),
                Schedule("03321a5f-3dd2-4eb8-86ec-c9e154f3f012", 11, 50, NotificationScheduleKind.Lunch, "Estamos perto do almoço. Lembre-se de pausar seus registros."),
                Schedule("03321a5f-3dd2-4eb8-86ec-c9e154f3f013", 17, 0, NotificationScheduleKind.EndOfDay, "Estamos perto do fim do expediente. Confira e encerre seus registros."));
        });
        b.Entity<TimeNotificationDispatch>(e =>
        {
            e.ToTable("notification_dispatches", "time_control");
            e.Property(x => x.DeduplicationKey).HasMaxLength(160);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.Period).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.OrganizationId, x.DeduplicationKey }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TimeAnalysisReport>(e =>
        {
            e.ToTable("analysis_reports", "time_control");
            e.Property(x => x.AnalysisJson).HasColumnType("jsonb");
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Period).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.DispatchId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
            e.HasOne<TimeNotificationDispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkforcePerson>().WithMany().HasForeignKey(x => x.WorkforcePersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TimeNotification>(e =>
        {
            e.ToTable("notifications", "time_control");
            e.Property(x => x.Message).HasMaxLength(2500);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => x.ReportId).IsUnique();
            e.HasOne(x => x.Report).WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static TimeNotificationSchedule Schedule(string id, int hour, int minute, NotificationScheduleKind kind, string message)
        => new() { Id = Guid.Parse(id), LocalTime = new TimeOnly(hour, minute), Kind = kind, Message = message, Version = Guid.Parse(id) };
}
