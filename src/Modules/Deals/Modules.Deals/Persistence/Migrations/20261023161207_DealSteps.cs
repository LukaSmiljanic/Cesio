using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Deals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agreed_contract_sha256",
                schema: "deals",
                table: "deals",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cdd_decisions",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approved = table.Column<bool>(type: "bit", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    step_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cdd_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_cdd_decisions_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_documents",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_documents_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "terms_confirmations",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contract_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    party_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    step_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terms_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_terms_confirmations_contract_documents_contract_document_id",
                        column: x => x.contract_document_id,
                        principalSchema: "deals",
                        principalTable: "contract_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_terms_confirmations_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cdd_decisions_deal_id",
                schema: "deals",
                table: "cdd_decisions",
                column: "deal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_documents_deal_id_uploaded_at",
                schema: "deals",
                table: "contract_documents",
                columns: new[] { "deal_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_terms_confirmations_contract_document_id_party_org_id",
                schema: "deals",
                table: "terms_confirmations",
                columns: new[] { "contract_document_id", "party_org_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terms_confirmations_deal_id",
                schema: "deals",
                table: "terms_confirmations",
                column: "deal_id");

            // The step records are append-only, and the agreed contract hash is set once (task 015, database.md §1).
            foreach (var table in new[] { "cdd_decisions", "contract_documents", "terms_confirmations" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER deals.tr_{table}_append_only ON deals.{table}
                    INSTEAD OF UPDATE, DELETE AS
                    THROW 51000, 'deals.{table} is append-only.', 1;
                    """);
                migrationBuilder.Sql($"IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw; DENY UPDATE, DELETE ON deals.{table} TO app_rw;");
            }

            migrationBuilder.Sql($"""
                ALTER TRIGGER deals.{DealsDbContext.DealImmutableTrigger} ON deals.deals
                AFTER UPDATE AS
                IF UPDATE(id) OR UPDATE(reference) OR UPDATE(receivable_id) OR UPDATE(listing_id) OR UPDATE(offer_id)
                   OR UPDATE(seller_org_id) OR UPDATE(factor_org_id) OR UPDATE(face_value) OR UPDATE(purchase_price) OR UPDATE(currency)
                   OR UPDATE(recourse_type) OR UPDATE(funding_deadline_bdays) OR UPDATE(conditions_text) OR UPDATE(fee_schedule_version)
                   OR UPDATE(platform_fee) OR UPDATE(seller_fee) OR UPDATE(vat_pending) OR UPDATE(accepted_by) OR UPDATE(accepted_at)
                   OR UPDATE(accepted_step_up_at) OR UPDATE(signature_deadline)
                   OR EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
                              WHERE d.agreed_contract_sha256 IS NOT NULL AND (i.agreed_contract_sha256 IS NULL OR i.agreed_contract_sha256 <> d.agreed_contract_sha256))
                    THROW 51000, 'The parties and terms of a deal are immutable, and the agreed contract hash is set once.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "cdd_decisions", "contract_documents", "terms_confirmations" })
            {
                migrationBuilder.Sql($"DROP TRIGGER deals.tr_{table}_append_only;");
            }

            migrationBuilder.DropTable(
                name: "cdd_decisions",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "terms_confirmations",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "contract_documents",
                schema: "deals");

            migrationBuilder.DropColumn(
                name: "agreed_contract_sha256",
                schema: "deals",
                table: "deals");
        }
    }
}
