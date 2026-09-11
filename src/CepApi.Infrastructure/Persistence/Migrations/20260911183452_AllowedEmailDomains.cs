using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CepApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowedEmailDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "allowed_email_domains",
                columns: table => new
                {
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allowed_email_domains", x => x.domain);
                    table.CheckConstraint("ck_allowed_email_domains_normalized", "domain = lower(domain) AND domain ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$'");
                });
            migrationBuilder.InsertData(
                table: "allowed_email_domains",
                columns: new[] { "domain", "is_enabled" },
                values: new object[] { "conceitoprojetos.com", true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allowed_email_domains");
        }
    }
}
