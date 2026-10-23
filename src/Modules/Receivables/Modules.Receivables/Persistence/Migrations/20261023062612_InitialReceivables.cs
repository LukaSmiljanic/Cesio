using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Receivables.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "receivables");

            migrationBuilder.CreateTable(
                name: "debtors",
                schema: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pib = table.Column<string>(type: "char(9)", unicode: false, fixedLength: true, maxLength: 9, nullable: false),
                    mb = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    legal_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debtors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seller_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    debtor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sef_invoice_id = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    invoice_number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    gross_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ubl_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ubl_sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    sef_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    sef_status_checked_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoice_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_invoice_dates", "[due_date] >= [issue_date]");
                    table.CheckConstraint("ck_invoice_gross", "[gross_amount] = [net_amount] + [vat_amount]");
                    table.ForeignKey(
                        name: "fk_invoices_debtors_debtor_id",
                        column: x => x.debtor_id,
                        principalSchema: "receivables",
                        principalTable: "debtors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                schema: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seller_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    debtor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    maturity_date = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    fingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    face_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivables", x => x.id);
                    table.CheckConstraint("ck_receivable_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_receivable_face_value", "[face_value] > 0");
                    table.ForeignKey(
                        name: "fk_receivables_debtors_debtor_id",
                        column: x => x.debtor_id,
                        principalSchema: "receivables",
                        principalTable: "debtors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receivables_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "receivables",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sef_status_history",
                schema: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    raw = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sef_status_history", x => x.id);
                    table.CheckConstraint("ck_sef_status_observation_raw_json", "ISJSON([raw]) = 1");
                    table.ForeignKey(
                        name: "fk_sef_status_history_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "receivables",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_debtors_pib",
                schema: "receivables",
                table: "debtors",
                column: "pib",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_debtor_id",
                schema: "receivables",
                table: "invoices",
                column: "debtor_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_sef_invoice_id",
                schema: "receivables",
                table: "invoices",
                column: "sef_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_seller_org_id_invoice_number_issue_date",
                schema: "receivables",
                table: "invoices",
                columns: new[] { "seller_org_id", "invoice_number", "issue_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receivable_fingerprint",
                schema: "receivables",
                table: "receivables",
                column: "fingerprint");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_debtor_id",
                schema: "receivables",
                table: "receivables",
                column: "debtor_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_seller_org_id_state",
                schema: "receivables",
                table: "receivables",
                columns: new[] { "seller_org_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_receivable_per_invoice",
                schema: "receivables",
                table: "receivables",
                column: "invoice_id",
                unique: true,
                filter: "[state] <> 'Rejected' AND [state] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_sef_status_history_invoice_id_observed_at",
                schema: "receivables",
                table: "sef_status_history",
                columns: new[] { "invoice_id", "observed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receivables",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "sef_status_history",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "debtors",
                schema: "receivables");
        }
    }
}
