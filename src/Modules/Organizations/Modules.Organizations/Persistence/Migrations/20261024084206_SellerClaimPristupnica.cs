using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Organizations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SellerClaimPristupnica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "official_email_recorded_at",
                schema: "orgs",
                table: "organizations",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "official_email_recorded_by",
                schema: "orgs",
                table: "organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "official_email_source_document_id",
                schema: "orgs",
                table: "organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "confirmation_tokens",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purpose = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    token_hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_confirmation_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_confirmation_tokens_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mfa_reset_requests",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_kind = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    first_officer = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    executed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_reset_requests", x => x.id);
                    table.CheckConstraint("ck_mfa_reset_four_eyes", "[executed_by] IS NULL OR [first_officer] IS NULL OR [executed_by] <> [first_officer]");
                    table.ForeignKey(
                        name: "fk_mfa_reset_requests_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pristupnice",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generated_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generated_sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    signed_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    method = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    signer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    matched_representative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    power_of_attorney_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pristupnice", x => x.id);
                    table.ForeignKey(
                        name: "fk_pristupnice_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_confirmation_tokens_org_id",
                schema: "orgs",
                table: "confirmation_tokens",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_confirmation_tokens_token_hash",
                schema: "orgs",
                table: "confirmation_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_reset_requests_org_id",
                schema: "orgs",
                table: "mfa_reset_requests",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_reset_requests_user_id_status",
                schema: "orgs",
                table: "mfa_reset_requests",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pristupnice_org_id_created_at",
                schema: "orgs",
                table: "pristupnice",
                columns: new[] { "org_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confirmation_tokens",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "mfa_reset_requests",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "pristupnice",
                schema: "orgs");

            migrationBuilder.DropColumn(
                name: "official_email_recorded_at",
                schema: "orgs",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "official_email_recorded_by",
                schema: "orgs",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "official_email_source_document_id",
                schema: "orgs",
                table: "organizations");
        }
    }
}
