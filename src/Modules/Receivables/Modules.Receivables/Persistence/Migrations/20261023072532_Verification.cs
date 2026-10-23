using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Receivables.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Verification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "receivables",
                table: "receivables",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "review_required",
                schema: "receivables",
                table: "receivables",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "declaration_versions",
                schema: "receivables",
                columns: table => new
                {
                    version = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    item_codes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaration_versions", x => x.version);
                    table.CheckConstraint("ck_declaration_version_item_codes_json", "ISJSON([item_codes]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "receivable_documents",
                schema: "receivables",
                columns: table => new
                {
                    receivable_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attached_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivable_documents", x => new { x.receivable_id, x.document_id });
                    table.ForeignKey(
                        name: "fk_receivable_documents_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "receivables",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_checks",
                schema: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    result = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    details = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ran_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_checks_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "receivables",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seller_declarations",
                schema: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    answers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    all_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    accepted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ip_address = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_declarations", x => x.id);
                    table.CheckConstraint("ck_seller_declaration_answers_json", "ISJSON([answers]) = 1");
                    table.ForeignKey(
                        name: "fk_seller_declarations_declaration_versions_version",
                        column: x => x.version,
                        principalSchema: "receivables",
                        principalTable: "declaration_versions",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seller_declarations_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "receivables",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "receivables",
                table: "declaration_versions",
                columns: new[] { "version", "created_at", "item_codes", "sha256", "text" },
                values: new object[] { "DRAFT-0", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "[\"NoPriorAssignment\",\"NoAssignmentProhibition\",\"NoSetOffOrDispute\",\"GoodsOrServicesDelivered\"]", "10734902504f2d52e33eae5d7251123ef6310e7f6b2cf302b18c7620bdee88be", "NACRT (DRAFT-0), čeka konačan tekst. Potvrđujem: (1) potraživanje nije ranije ustupljeno niti založeno; (2) ugovor sa dužnikom ne zabranjuje ustupanje; (3) ne postoji prebijanje ni spor sa dužnikom; (4) roba je isporučena odnosno usluga izvršena." });

            migrationBuilder.CreateIndex(
                name: "ix_seller_declarations_receivable_id_accepted_at",
                schema: "receivables",
                table: "seller_declarations",
                columns: new[] { "receivable_id", "accepted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_seller_declarations_version",
                schema: "receivables",
                table: "seller_declarations",
                column: "version");

            migrationBuilder.CreateIndex(
                name: "ix_verification_checks_receivable_id_run",
                schema: "receivables",
                table: "verification_checks",
                columns: new[] { "receivable_id", "run" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receivable_documents",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "seller_declarations",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "verification_checks",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "declaration_versions",
                schema: "receivables");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "receivables",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "review_required",
                schema: "receivables",
                table: "receivables");
        }
    }
}
