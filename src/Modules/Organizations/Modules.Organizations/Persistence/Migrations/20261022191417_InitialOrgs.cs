using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Organizations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrgs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orgs");

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    legal_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    pib = table.Column<string>(type: "char(9)", unicode: false, fixedLength: true, maxLength: 9, nullable: false),
                    mb = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: true),
                    legal_form = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    registered_address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    official_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    onboarding_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    risk_rating = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                    table.CheckConstraint("ck_organization_org_type", "[org_type] IN ('Seller','Factor')");
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    token_hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    invited_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    accepted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitations_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "legal_representatives",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    linked_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_representatives", x => x.id);
                    table.ForeignKey(
                        name: "fk_legal_representatives_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "terms_acceptances",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    terms_version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ip_address = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terms_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "fk_terms_acceptances_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    is_signatory = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    legal_representative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    power_of_attorney_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_legal_representatives_legal_representative_id",
                        column: x => x.legal_representative_id,
                        principalSchema: "orgs",
                        principalTable: "legal_representatives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memberships_organizations_org_id",
                        column: x => x.org_id,
                        principalSchema: "orgs",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_org_id",
                schema: "orgs",
                table: "invitations",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_token_hash",
                schema: "orgs",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_representatives_org_id",
                schema: "orgs",
                table: "legal_representatives",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_legal_representative_id",
                schema: "orgs",
                table: "memberships",
                column: "legal_representative_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_org_id_user_id",
                schema: "orgs",
                table: "memberships",
                columns: new[] { "org_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                schema: "orgs",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_org_mb_claimed",
                schema: "orgs",
                table: "organizations",
                column: "mb",
                unique: true,
                filter: "[claimed_at] IS NOT NULL AND [mb] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_org_pib_claimed",
                schema: "orgs",
                table: "organizations",
                column: "pib",
                unique: true,
                filter: "[claimed_at] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_terms_acceptances_org_id",
                schema: "orgs",
                table: "terms_acceptances",
                column: "org_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitations",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "terms_acceptances",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "legal_representatives",
                schema: "orgs");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "orgs");
        }
    }
}
