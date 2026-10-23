using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Deals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cef_registrations",
                schema: "deals",
                columns: table => new
                {
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    evidence_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recorded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cef_registrations", x => x.deal_id);
                    table.ForeignKey(
                        name: "fk_cef_registrations_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debtor_notifications",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    evidence_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recorded_by_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debtor_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_debtor_notifications_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    opened_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                    table.ForeignKey(
                        name: "fk_disputes_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "funding_confirmations",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    received = table.Column<bool>(type: "bit", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    step_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funding_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_funding_confirmations_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "funding_reports",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payee_bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payee_account_number = table.Column<string>(type: "char(18)", unicode: false, fixedLength: true, maxLength: 18, nullable: false),
                    reported_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    step_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funding_reports", x => x.id);
                    table.CheckConstraint("ck_funding_report_currency", "[currency] IN ('RSD','EUR')");
                    table.ForeignKey(
                        name: "fk_funding_reports_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outcomes",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_collected = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    reported_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outcomes", x => x.id);
                    table.ForeignKey(
                        name: "fk_outcomes_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_debtor_notifications_deal_id",
                schema: "deals",
                table: "debtor_notifications",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_deal_id_status",
                schema: "deals",
                table: "disputes",
                columns: new[] { "deal_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_funding_confirmations_deal_id",
                schema: "deals",
                table: "funding_confirmations",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_funding_reports_deal_id",
                schema: "deals",
                table: "funding_reports",
                column: "deal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outcomes_deal_id",
                schema: "deals",
                table: "outcomes",
                column: "deal_id",
                unique: true);

            // The evidence of the steps after signing is append-only (task 017, database.md §1).
            foreach (var table in AppendOnly)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER deals.tr_{table}_append_only ON deals.{table}
                    INSTEAD OF UPDATE, DELETE AS
                    THROW 51000, 'deals.{table} is append-only.', 1;
                    """);
                migrationBuilder.Sql($"IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw; DENY UPDATE, DELETE ON deals.{table} TO app_rw;");
            }
        }

        private static readonly string[] AppendOnly = ["debtor_notifications", "funding_reports", "funding_confirmations", "outcomes"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnly)
            {
                migrationBuilder.Sql($"DROP TRIGGER deals.tr_{table}_append_only;");
            }

            migrationBuilder.DropTable(
                name: "cef_registrations",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "debtor_notifications",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "disputes",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "funding_confirmations",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "funding_reports",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "outcomes",
                schema: "deals");
        }
    }
}
