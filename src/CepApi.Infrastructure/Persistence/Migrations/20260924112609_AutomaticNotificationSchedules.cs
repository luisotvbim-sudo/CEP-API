using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticNotificationSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automatic_notification_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalTime = table.Column<TimeOnly>(type: "time(0) without time zone", precision: 0, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_notification_schedules", x => x.Id);
                    table.CheckConstraint("ck_automatic_notification_schedules_minute_precision", "date_part('second', \"LocalTime\") = 0");
                    table.ForeignKey(
                        name: "FK_automatic_notification_schedules_organizations_Organization~",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO automatic_notification_schedules
                    ("Id", "OrganizationId", "LocalTime", "TimeZoneId", "IsEnabled", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "Id", TIME '11:50:00', 'America/Sao_Paulo', TRUE, NOW(), NOW()
                FROM organizations;
                """);

            migrationBuilder.CreateTable(
                name: "automatic_notification_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedNotificationCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_notification_executions", x => x.Id);
                    table.CheckConstraint("ck_automatic_notification_executions_attempts", "\"Attempts\" > 0");
                    table.CheckConstraint("ck_automatic_notification_executions_notification_count", "\"CreatedNotificationCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_automatic_notification_executions_automatic_notification_sc~",
                        column: x => x.ScheduleId,
                        principalTable: "automatic_notification_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_automatic_notification_executions_organizations_Organizatio~",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_control_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_control_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_time_control_notifications_automatic_notification_execution~",
                        column: x => x.ExecutionId,
                        principalTable: "automatic_notification_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_time_control_notifications_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_time_control_notifications_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automatic_notification_executions_OrganizationId",
                table: "automatic_notification_executions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_automatic_notification_executions_ScheduleId_LocalDate",
                table: "automatic_notification_executions",
                columns: new[] { "ScheduleId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automatic_notification_executions_Status_LeaseExpiresAt",
                table: "automatic_notification_executions",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_automatic_notification_schedules_OrganizationId_IsEnabled",
                table: "automatic_notification_schedules",
                columns: new[] { "OrganizationId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_automatic_notification_schedules_OrganizationId_LocalTime_T~",
                table: "automatic_notification_schedules",
                columns: new[] { "OrganizationId", "LocalTime", "TimeZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_control_notifications_ExecutionId",
                table: "time_control_notifications",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_time_control_notifications_OrganizationId_RecipientUserId_D~",
                table: "time_control_notifications",
                columns: new[] { "OrganizationId", "RecipientUserId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_control_notifications_RecipientUserId_ReadAt_CreatedAt",
                table: "time_control_notifications",
                columns: new[] { "RecipientUserId", "ReadAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "time_control_notifications");

            migrationBuilder.DropTable(
                name: "automatic_notification_executions");

            migrationBuilder.DropTable(
                name: "automatic_notification_schedules");
        }
    }
}
