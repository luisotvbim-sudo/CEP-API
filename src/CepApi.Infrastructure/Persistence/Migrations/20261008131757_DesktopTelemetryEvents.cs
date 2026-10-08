using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DesktopTelemetryEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "desktop_telemetry_events",
                schema: "time_control",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Phase = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_desktop_telemetry_events", x => new { x.OrganizationId, x.UserId, x.EventId });
                    table.ForeignKey(
                        name: "FK_desktop_telemetry_events_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_desktop_telemetry_events_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_desktop_telemetry_events_OrganizationId_ReceivedAt",
                schema: "time_control",
                table: "desktop_telemetry_events",
                columns: new[] { "OrganizationId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_desktop_telemetry_events_ReceivedAt",
                schema: "time_control",
                table: "desktop_telemetry_events",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_desktop_telemetry_events_UserId",
                schema: "time_control",
                table: "desktop_telemetry_events",
                column: "UserId");

            migrationBuilder.Sql("""
                DO $grant$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cep_api_runtime') THEN
                        GRANT USAGE ON SCHEMA time_control TO cep_api_runtime;
                        GRANT SELECT, INSERT, DELETE ON time_control.desktop_telemetry_events TO cep_api_runtime;
                    END IF;
                END
                $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desktop_telemetry_events",
                schema: "time_control");
        }
    }
}
