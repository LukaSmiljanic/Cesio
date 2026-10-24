using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Compliance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "compliance");

            migrationBuilder.CreateTable(
                name: "beneficial_owners",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organization_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    nationality = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: true),
                    ownership_percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    control_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    pep_declared = table.Column<bool>(type: "bit", nullable: false),
                    current = table.Column<bool>(type: "bit", nullable: false),
                    declared_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    declared_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beneficial_owners", x => x.id);
                    table.CheckConstraint("ck_beneficial_owner_basis", "[ownership_percent] IS NOT NULL OR [control_type] IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "holds",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    placed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    lift_requested_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lifted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lifted_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holds", x => x.id);
                    table.CheckConstraint("ck_hold_four_eyes", "[lifted_by] IS NULL OR [lift_requested_by] IS NULL OR [lifted_by] <> [lift_requested_by]");
                });

            migrationBuilder.CreateTable(
                name: "kyb_cases",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organization_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    risk_rating = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    risk_reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    sharing_consent = table.Column<bool>(type: "bit", nullable: false),
                    sharing_consent_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    first_approver = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    second_approver = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decision_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    next_review_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kyb_cases", x => x.id);
                    table.CheckConstraint("ck_kyb_case_four_eyes", "[second_approver] IS NULL OR [second_approver] <> [first_approver]");
                });

            migrationBuilder.CreateTable(
                name: "screening_lists",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    entry_count = table.Column<int>(type: "int", nullable: false),
                    imported_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_screening_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "screening_results",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kyb_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    subject_name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    list_source = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    entry_name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    score = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    result = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    resolved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    screened_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_screening_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_screening_results_kyb_cases_kyb_case_id",
                        column: x => x.kyb_case_id,
                        principalSchema: "compliance",
                        principalTable: "kyb_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "screening_list_entries",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    list_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    normalized_name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_screening_list_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_screening_list_entries_screening_lists_list_id",
                        column: x => x.list_id,
                        principalSchema: "compliance",
                        principalTable: "screening_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_beneficial_owners_organization_id_current",
                schema: "compliance",
                table: "beneficial_owners",
                columns: new[] { "organization_id", "current" });

            migrationBuilder.CreateIndex(
                name: "ix_holds_subject_type_subject_id_lifted_at",
                schema: "compliance",
                table: "holds",
                columns: new[] { "subject_type", "subject_id", "lifted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kyb_cases_organization_id",
                schema: "compliance",
                table: "kyb_cases",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_kyb_cases_status",
                schema: "compliance",
                table: "kyb_cases",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_screening_list_entries_list_id",
                schema: "compliance",
                table: "screening_list_entries",
                column: "list_id");

            migrationBuilder.CreateIndex(
                name: "ix_screening_lists_source_imported_at",
                schema: "compliance",
                table: "screening_lists",
                columns: new[] { "source", "imported_at" });

            migrationBuilder.CreateIndex(
                name: "ix_screening_lists_source_sha256",
                schema: "compliance",
                table: "screening_lists",
                columns: new[] { "source", "sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_screening_results_kyb_case_id_result",
                schema: "compliance",
                table: "screening_results",
                columns: new[] { "kyb_case_id", "result" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beneficial_owners",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "holds",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "screening_list_entries",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "screening_results",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "screening_lists",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "kyb_cases",
                schema: "compliance");
        }
    }
}
