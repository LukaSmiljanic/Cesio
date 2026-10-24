using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Modules.Organizations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fingerprint",
                schema: "orgs",
                table: "confirmation_tokens",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_number = table.Column<string>(type: "char(18)", unicode: false, fixedLength: true, maxLength: 18, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    evidence_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    supplement_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    duplicate_flag = table.Column<bool>(type: "bit", nullable: false),
                    verified_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    usable_from = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                    table.CheckConstraint("ck_bank_account_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_bank_account_usable_after_verification", "[usable_from] IS NULL OR [verified_at] IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_bank_accounts_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_codes",
                schema: "orgs",
                columns: table => new
                {
                    code = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_codes", x => x.code);
                });

            migrationBuilder.InsertData(
                schema: "orgs",
                table: "bank_codes",
                columns: new[] { "code", "name" },
                values: new object[,]
                {
                    { "160", "Banca Intesa" },
                    { "170", "UniCredit Bank Srbija" },
                    { "205", "NLB Komercijalna banka" },
                    { "265", "Raiffeisen banka" },
                    { "325", "OTP banka Srbija" },
                    { "340", "Erste Bank" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_confirmation_tokens_purpose_subject_id",
                schema: "orgs",
                table: "confirmation_tokens",
                columns: new[] { "purpose", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_account_number",
                schema: "orgs",
                table: "bank_accounts",
                column: "account_number");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_org_id_account_number_currency",
                schema: "orgs",
                table: "bank_accounts",
                columns: new[] { "org_id", "account_number", "currency" },
                unique: true,
                filter: "[status] IN ('ConfirmationPending','Pending','Verified')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "bank_codes",
                schema: "orgs");

            migrationBuilder.DropIndex(
                name: "ix_confirmation_tokens_purpose_subject_id",
                schema: "orgs",
                table: "confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "fingerprint",
                schema: "orgs",
                table: "confirmation_tokens");
        }
    }
}
