using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PowerActionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "power_action_overrides",
                schema: "time_control",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PinVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecipientSecurityVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_power_action_overrides", x => x.UserId);
                    table.CheckConstraint("ck_power_override_duration", "\"ExpiresAt\" = \"GrantedAt\" + INTERVAL '5 minutes'");
                    table.ForeignKey(
                        name: "FK_power_action_overrides_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_power_action_overrides_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "power_pin_configuration",
                schema: "time_control",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PinHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_power_pin_configuration", x => x.Id);
                    table.CheckConstraint("ck_power_pin_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_power_action_overrides_OrganizationId_ExpiresAt",
                schema: "time_control",
                table: "power_action_overrides",
                columns: new[] { "OrganizationId", "ExpiresAt" });

            migrationBuilder.Sql("""
                DO $grant$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cep_api_runtime') THEN
                        GRANT USAGE ON SCHEMA time_control TO cep_api_runtime;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON time_control.power_action_overrides TO cep_api_runtime;
                        REVOKE ALL ON time_control.power_pin_configuration FROM cep_api_runtime;
                        GRANT SELECT, UPDATE ON time_control.power_pin_configuration TO cep_api_runtime;
                    END IF;
                END
                $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "power_action_overrides",
                schema: "time_control");

            migrationBuilder.DropTable(
                name: "power_pin_configuration",
                schema: "time_control");
        }
    }
}
