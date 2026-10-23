using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Fees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeeCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fee_charges",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payer_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payer_role = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    kind = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    vat_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_pending = table.Column<bool>(type: "bit", nullable: false),
                    fee_schedule_version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    due_at = table.Column<DateOnly>(type: "date", nullable: true),
                    external_invoice_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    reverses_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    deal_reference = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    invoice_number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    face_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    funded_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_charges", x => x.id);
                    table.CheckConstraint("ck_fee_charge_amounts", "[amount] >= 0 AND [vat_amount] >= 0");
                    table.CheckConstraint("ck_fee_charge_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_fee_charge_kind", "[kind] IN ('Charge','Reversal')");
                    table.CheckConstraint("ck_fee_charge_reversal", "([kind] = 'Reversal' AND [reverses_id] IS NOT NULL) OR ([kind] = 'Charge' AND [reverses_id] IS NULL)");
                    table.ForeignKey(
                        name: "fk_fee_charges_fee_charges_reverses_id",
                        column: x => x.reverses_id,
                        principalSchema: "fees",
                        principalTable: "fee_charges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_payments",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    charge_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    recorded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fee_payments_fee_charges_charge_id",
                        column: x => x.charge_id,
                        principalSchema: "fees",
                        principalTable: "fee_charges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fee_charges_payer_org_id_funded_at",
                schema: "fees",
                table: "fee_charges",
                columns: new[] { "payer_org_id", "funded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_charges_reverses_id",
                schema: "fees",
                table: "fee_charges",
                column: "reverses_id",
                unique: true,
                filter: "[reverses_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_fee_charge_once",
                schema: "fees",
                table: "fee_charges",
                columns: new[] { "deal_id", "payer_org_id" },
                unique: true,
                filter: "[kind] = 'Charge'");

            migrationBuilder.CreateIndex(
                name: "ix_fee_payments_charge_id",
                schema: "fees",
                table: "fee_payments",
                column: "charge_id",
                unique: true);

            // Monetary rows are never edited or deleted (INV-F3, database.md §1): only the status, the external invoice
            // reference and the approval change; payments are append-only.
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.{FeesDbContext.ChargeImmutableTrigger} ON fees.fee_charges
                AFTER UPDATE AS
                IF UPDATE(id) OR UPDATE(deal_id) OR UPDATE(payer_org_id) OR UPDATE(payer_role) OR UPDATE(kind) OR UPDATE(amount)
                   OR UPDATE(vat_amount) OR UPDATE(currency) OR UPDATE(vat_pending) OR UPDATE(fee_schedule_version) OR UPDATE(due_at)
                   OR UPDATE(reverses_id) OR UPDATE(reason) OR UPDATE(deal_reference) OR UPDATE(invoice_number) OR UPDATE(face_value)
                   OR UPDATE(funded_at) OR UPDATE(created_by) OR UPDATE(created_at)
                    THROW 51000, 'A fee charge is immutable except for its status.', 1;
                """);
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.tr_fee_charges_no_delete ON fees.fee_charges
                INSTEAD OF DELETE AS
                THROW 51000, 'fees.fee_charges rows cannot be deleted; use a reversal.', 1;
                """);
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.{FeesDbContext.PaymentsAppendOnlyTrigger} ON fees.fee_payments
                INSTEAD OF UPDATE, DELETE AS
                THROW 51000, 'fees.fee_payments is append-only.', 1;
                """);
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw;
                DENY DELETE ON fees.fee_charges TO app_rw;
                DENY UPDATE, DELETE ON fees.fee_payments TO app_rw;
                DENY UPDATE (deal_id, payer_org_id, payer_role, kind, amount, vat_amount, currency, vat_pending, fee_schedule_version, due_at,
                    reverses_id, reason, deal_reference, invoice_number, face_value, funded_at, created_by, created_at) ON fees.fee_charges TO app_rw;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER fees.{FeesDbContext.PaymentsAppendOnlyTrigger};");
            migrationBuilder.Sql("DROP TRIGGER fees.tr_fee_charges_no_delete;");
            migrationBuilder.Sql($"DROP TRIGGER fees.{FeesDbContext.ChargeImmutableTrigger};");

            migrationBuilder.DropTable(
                name: "fee_payments",
                schema: "fees");

            migrationBuilder.DropTable(
                name: "fee_charges",
                schema: "fees");
        }
    }
}
