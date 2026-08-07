using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plugin_usage_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Product = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Command = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PluginVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HostVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_usage_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plugin_usage_events_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plugin_usage_events_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_usage_events_OrganizationId_ClientEventId",
                table: "plugin_usage_events",
                columns: new[] { "OrganizationId", "ClientEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugin_usage_events_OrganizationId_OccurredAt",
                table: "plugin_usage_events",
                columns: new[] { "OrganizationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_usage_events_OrganizationId_Product_Command_Occurred~",
                table: "plugin_usage_events",
                columns: new[] { "OrganizationId", "Product", "Command", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_usage_events_OrganizationId_UserId_OccurredAt",
                table: "plugin_usage_events",
                columns: new[] { "OrganizationId", "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_usage_events_UserId",
                table: "plugin_usage_events",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plugin_usage_events");
        }
    }
}
