using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GlobalTimeAnalysisNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "time_control");

            migrationBuilder.CreateTable(
                name: "app_settings",
                schema: "time_control",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ToleranceMinutes = table.Column<int>(type: "integer", nullable: false),
                    AutomaticEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Id);
                    table.CheckConstraint("ck_settings_singleton", "\"Id\" = 1");
                    table.CheckConstraint("ck_tolerance", "\"ToleranceMinutes\" BETWEEN 0 AND 1440");
                });

            migrationBuilder.CreateTable(
                name: "notification_dispatches",
                schema: "time_control",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ReportOnly = table.Column<bool>(type: "boolean", nullable: false),
                    ToleranceMinutes = table.Column<int>(type: "integer", nullable: false),
                    SettingsVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_dispatches_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_schedules",
                schema: "time_control",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "analysis_reports",
                schema: "time_control",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkforcePersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnalysisJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_analysis_reports_notification_dispatches_DispatchId",
                        column: x => x.DispatchId,
                        principalSchema: "time_control",
                        principalTable: "notification_dispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_analysis_reports_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_analysis_reports_workforce_people_WorkforcePersonId",
                        column: x => x.WorkforcePersonId,
                        principalTable: "workforce_people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "time_control",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_analysis_reports_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "time_control",
                        principalTable: "analysis_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "time_control",
                table: "app_settings",
                columns: new[] { "Id", "AutomaticEnabled", "ToleranceMinutes", "UpdatedAt", "UpdatedByUserId", "Version" },
                values: new object[] { 1, false, 30, new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new Guid("53321a5f-3dd2-4eb8-86ec-c9e154f3f011") });

            migrationBuilder.InsertData(
                schema: "time_control",
                table: "notification_schedules",
                columns: new[] { "Id", "IsDeleted", "IsEnabled", "Kind", "LocalTime", "Message", "Version" },
                values: new object[,]
                {
                    { new Guid("03321a5f-3dd2-4eb8-86ec-c9e154f3f011"), false, true, "PreviousDay", new TimeOnly(10, 0, 0), "Confira as pendências de ontem.", new Guid("03321a5f-3dd2-4eb8-86ec-c9e154f3f011") },
                    { new Guid("03321a5f-3dd2-4eb8-86ec-c9e154f3f012"), false, true, "Lunch", new TimeOnly(11, 50, 0), "Estamos perto do almoço. Lembre-se de pausar seus registros.", new Guid("03321a5f-3dd2-4eb8-86ec-c9e154f3f012") },
                    { new Guid("03321a5f-3dd2-4eb8-86ec-c9e154f3f013"), false, true, "EndOfDay", new TimeOnly(17, 0, 0), "Estamos perto do fim do expediente. Confira e encerre seus registros.", new Guid("03321a5f-3dd2-4eb8-86ec-c9e154f3f013") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_reports_DispatchId_UserId",
                schema: "time_control",
                table: "analysis_reports",
                columns: new[] { "DispatchId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_analysis_reports_OrganizationId_CreatedAt",
                schema: "time_control",
                table: "analysis_reports",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_reports_UserId",
                schema: "time_control",
                table: "analysis_reports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_reports_WorkforcePersonId",
                schema: "time_control",
                table: "analysis_reports",
                column: "WorkforcePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_dispatches_OrganizationId_DeduplicationKey",
                schema: "time_control",
                table: "notification_dispatches",
                columns: new[] { "OrganizationId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_dispatches_Status_CreatedAt",
                schema: "time_control",
                table: "notification_dispatches",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_schedules_LocalTime",
                schema: "time_control",
                table: "notification_schedules",
                column: "LocalTime",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_ReportId",
                schema: "time_control",
                table: "notifications",
                column: "ReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_CreatedAt",
                schema: "time_control",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAt" });

            // Existing installations grant runtime access only to public. The migration
            // owner also owns this new schema; keep runtime limited to data operations.
            migrationBuilder.Sql("""
                DO $grant$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cep_api_runtime') THEN
                        GRANT USAGE ON SCHEMA time_control TO cep_api_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA time_control TO cep_api_runtime;
                        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA time_control TO cep_api_runtime;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA time_control
                            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cep_api_runtime;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA time_control
                            GRANT USAGE, SELECT ON SEQUENCES TO cep_api_runtime;
                    END IF;
                END $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings",
                schema: "time_control");

            migrationBuilder.DropTable(
                name: "notification_schedules",
                schema: "time_control");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "time_control");

            migrationBuilder.DropTable(
                name: "analysis_reports",
                schema: "time_control");

            migrationBuilder.DropTable(
                name: "notification_dispatches",
                schema: "time_control");
        }
    }
}
