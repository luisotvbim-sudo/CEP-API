using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkforceIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkforcePersonId",
                table: "invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_workforce_identities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_workforce_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_workforce_identities_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workforce_sync_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workforce_sync_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workforce_sync_batches_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workforce_people",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    MondayIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VrMaisIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workforce_people", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workforce_people_external_workforce_identities_MondayIdenti~",
                        column: x => x.MondayIdentityId,
                        principalTable: "external_workforce_identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workforce_people_external_workforce_identities_VrMaisIdenti~",
                        column: x => x.VrMaisIdentityId,
                        principalTable: "external_workforce_identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workforce_people_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workforce_people_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "workforce_time_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workforce_time_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workforce_time_records_external_workforce_identities_Extern~",
                        column: x => x.ExternalIdentityId,
                        principalTable: "external_workforce_identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workforce_time_records_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workforce_sync_source_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceivedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedCount = table.Column<int>(type: "integer", nullable: false),
                    TimeRecordReceivedCount = table.Column<int>(type: "integer", nullable: false),
                    TimeRecordCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    TimeRecordUpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    TimeRecordRemovedCount = table.Column<int>(type: "integer", nullable: false),
                    CompleteSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    CoverageFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    CoverageTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workforce_sync_source_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workforce_sync_source_runs_workforce_sync_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "workforce_sync_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invitations_WorkforcePersonId",
                table: "invitations",
                column: "WorkforcePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_external_workforce_identities_OrganizationId_Source_Externa~",
                table: "external_workforce_identities",
                columns: new[] { "OrganizationId", "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_workforce_identities_OrganizationId_Source_IsActive",
                table: "external_workforce_identities",
                columns: new[] { "OrganizationId", "Source", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_workforce_people_MondayIdentityId",
                table: "workforce_people",
                column: "MondayIdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workforce_people_OrganizationId_Email",
                table: "workforce_people",
                columns: new[] { "OrganizationId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_workforce_people_UserId",
                table: "workforce_people",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workforce_people_VrMaisIdentityId",
                table: "workforce_people",
                column: "VrMaisIdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workforce_sync_batches_OrganizationId",
                table: "workforce_sync_batches",
                column: "OrganizationId",
                unique: true,
                filter: "\"Status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_workforce_sync_batches_OrganizationId_StartedAt",
                table: "workforce_sync_batches",
                columns: new[] { "OrganizationId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workforce_sync_source_runs_BatchId_Source",
                table: "workforce_sync_source_runs",
                columns: new[] { "BatchId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workforce_sync_source_runs_OrganizationId_Source_CompletedAt",
                table: "workforce_sync_source_runs",
                columns: new[] { "OrganizationId", "Source", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workforce_time_records_ExternalIdentityId_WorkDate",
                table: "workforce_time_records",
                columns: new[] { "ExternalIdentityId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_workforce_time_records_OrganizationId_Source_ExternalKey",
                table: "workforce_time_records",
                columns: new[] { "OrganizationId", "Source", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workforce_time_records_OrganizationId_WorkDate_Source",
                table: "workforce_time_records",
                columns: new[] { "OrganizationId", "WorkDate", "Source" });

            migrationBuilder.AddForeignKey(
                name: "FK_invitations_workforce_people_WorkforcePersonId",
                table: "invitations",
                column: "WorkforcePersonId",
                principalTable: "workforce_people",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invitations_workforce_people_WorkforcePersonId",
                table: "invitations");

            migrationBuilder.DropTable(
                name: "workforce_people");

            migrationBuilder.DropTable(
                name: "workforce_sync_source_runs");

            migrationBuilder.DropTable(
                name: "workforce_time_records");

            migrationBuilder.DropTable(
                name: "workforce_sync_batches");

            migrationBuilder.DropTable(
                name: "external_workforce_identities");

            migrationBuilder.DropIndex(
                name: "IX_invitations_WorkforcePersonId",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "WorkforcePersonId",
                table: "invitations");
        }
    }
}
