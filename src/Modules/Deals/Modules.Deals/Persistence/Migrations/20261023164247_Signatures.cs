using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Deals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Signatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signatures",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    party = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    signer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    certificate_subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    certificate_issuer = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    certificate_serial = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    signing_time = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    result = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    report = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signatures", x => x.id);
                    table.CheckConstraint("ck_signature_report_json", "ISJSON([report]) = 1");
                    table.ForeignKey(
                        name: "fk_signatures_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trusted_certificates",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    sha256_thumbprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    certificate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    added_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    deactivated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trusted_certificates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signatures_deal_id_validated_at",
                schema: "deals",
                table: "signatures",
                columns: new[] { "deal_id", "validated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trusted_certificates_sha256_thumbprint",
                schema: "deals",
                table: "trusted_certificates",
                column: "sha256_thumbprint",
                unique: true);

            // Validated signatures are evidence: append-only (task 016, database.md §1).
            migrationBuilder.Sql("""
                CREATE TRIGGER deals.tr_signatures_append_only ON deals.signatures
                INSTEAD OF UPDATE, DELETE AS
                THROW 51000, 'deals.signatures is append-only.', 1;
                """);
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw; DENY UPDATE, DELETE ON deals.signatures TO app_rw;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER deals.tr_signatures_append_only;");

            migrationBuilder.DropTable(
                name: "signatures",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "trusted_certificates",
                schema: "deals");
        }
    }
}
