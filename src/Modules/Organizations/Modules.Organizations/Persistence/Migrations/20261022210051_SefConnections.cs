using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Organizations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SefConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sef_connections",
                schema: "orgs",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    secret_name = table.Column<string>(type: "varchar(127)", unicode: false, maxLength: 127, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    supplier_pib = table.Column<string>(type: "char(9)", unicode: false, fixedLength: true, maxLength: 9, nullable: true),
                    validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sef_connections", x => x.org_id);
                    table.ForeignKey(
                        name: "fk_sef_connections_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sef_connections",
                schema: "orgs");
        }
    }
}
