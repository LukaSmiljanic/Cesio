using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Fees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeeChargeApprovedAtImmutable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 4 money review: Effective reversals approved before the column existed get their approval time
            // (statements place a reversal by approved_at), and approved_at is set once.
            migrationBuilder.Sql("UPDATE fees.fee_charges SET approved_at = created_at WHERE kind = 'Reversal' AND status = 'Effective' AND approved_at IS NULL;");
            migrationBuilder.Sql($"DROP TRIGGER fees.{FeesDbContext.ChargeImmutableTrigger};");
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.{FeesDbContext.ChargeImmutableTrigger} ON fees.fee_charges
                AFTER UPDATE AS
                IF UPDATE(id) OR UPDATE(deal_id) OR UPDATE(payer_org_id) OR UPDATE(payer_role) OR UPDATE(kind) OR UPDATE(amount)
                   OR UPDATE(vat_amount) OR UPDATE(currency) OR UPDATE(vat_pending) OR UPDATE(fee_schedule_version) OR UPDATE(due_at)
                   OR UPDATE(reverses_id) OR UPDATE(reason) OR UPDATE(deal_reference) OR UPDATE(invoice_number) OR UPDATE(face_value)
                   OR UPDATE(funded_at) OR UPDATE(created_by) OR UPDATE(created_at)
                   OR EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.id = d.id WHERE d.approved_at IS NOT NULL AND (i.approved_at IS NULL OR i.approved_at <> d.approved_at))
                    THROW 51000, 'A fee charge is immutable except for its status; approved_at is set once.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER fees.{FeesDbContext.ChargeImmutableTrigger};");
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.{FeesDbContext.ChargeImmutableTrigger} ON fees.fee_charges
                AFTER UPDATE AS
                IF UPDATE(id) OR UPDATE(deal_id) OR UPDATE(payer_org_id) OR UPDATE(payer_role) OR UPDATE(kind) OR UPDATE(amount)
                   OR UPDATE(vat_amount) OR UPDATE(currency) OR UPDATE(vat_pending) OR UPDATE(fee_schedule_version) OR UPDATE(due_at)
                   OR UPDATE(reverses_id) OR UPDATE(reason) OR UPDATE(deal_reference) OR UPDATE(invoice_number) OR UPDATE(face_value)
                   OR UPDATE(funded_at) OR UPDATE(created_by) OR UPDATE(created_at)
                    THROW 51000, 'A fee charge is immutable except for its status.', 1;
                """);
        }
    }
}
