using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Fees.Persistence;

/// <summary>The <c>fees</c> schema (database.md §2): versioned fee schedules (task 017a), fee charges and payments (task 018).</summary>
public sealed class FeesDbContext(DbContextOptions<FeesDbContext> options, UnitOfWork? unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "fees";

    public const string ImmutableTrigger = "tr_fee_schedules_immutable";

    public const string NoDeleteTrigger = "tr_fee_schedules_no_delete";

    public const string ChargeImmutableTrigger = "tr_fee_charges_immutable";

    public const string PaymentsAppendOnlyTrigger = "tr_fee_payments_append_only";

    internal DbSet<FeeScheduleRow> FeeSchedules => Set<FeeScheduleRow>();

    internal DbSet<FeeCharge> FeeCharges => Set<FeeCharge>();

    internal DbSet<FeePayment> FeePayments => Set<FeePayment>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeeCharge>(charge =>
        {
            charge.ToTable("fee_charges", table =>
            {
                table.HasTrigger(ChargeImmutableTrigger);
                table.HasTrigger("tr_fee_charges_no_delete");
                table.HasCheckConstraint("ck_fee_charge_kind", "[kind] IN ('Charge','Reversal')");
                table.HasCheckConstraint("ck_fee_charge_amounts", "[amount] >= 0 AND [vat_amount] >= 0");
                table.HasCheckConstraint("ck_fee_charge_reversal", "([kind] = 'Reversal' AND [reverses_id] IS NOT NULL) OR ([kind] = 'Charge' AND [reverses_id] IS NULL)");
            });
            charge.HasKey(c => c.Id);
            charge.Property(c => c.Id).ValueGeneratedNever();
            charge.Property(c => c.Kind).HasMaxLength(10).IsUnicode(false);
            charge.Property(c => c.PayerRole).HasMaxLength(10).IsUnicode(false);
            charge.Property(c => c.Status).HasMaxLength(20).IsUnicode(false);
            charge.HasMoney(c => c.Amount, "amount", "currency");
            charge.Property(c => c.VatAmount).HasColumnName("vat_amount");
            charge.Property(c => c.FaceValueAmount).HasColumnName("face_value");
            charge.Property(c => c.DealReference).HasMaxLength(20).IsUnicode(false);
            charge.Property(c => c.InvoiceNumber).HasMaxLength(200);
            charge.Property(c => c.ExternalInvoiceRef).HasMaxLength(200);
            charge.Property(c => c.Reason).HasMaxLength(400);
            charge.HasOne<FeeCharge>().WithMany().HasForeignKey(c => c.ReversesId).OnDelete(DeleteBehavior.Restrict);
            // INV-F3: one charge per deal and payer, even when DealFunded is delivered twice (database.md §2).
            charge.HasIndex(c => new { c.DealId, c.PayerOrgId }).IsUnique()
                .HasFilter("[kind] = 'Charge'")
                .HasDatabaseName("ux_fee_charge_once");
            // One reversal per charge.
            // One live reversal per charge; a withdrawn one frees the charge for a new reversal (task 021).
            charge.HasIndex(c => c.ReversesId).IsUnique().HasFilter("[reverses_id] IS NOT NULL AND [status] <> 'Withdrawn'");
            charge.HasIndex(c => new { c.PayerOrgId, c.FundedAt });
        });

        modelBuilder.Entity<FeePayment>(payment =>
        {
            payment.ToTable("fee_payments", table => table.HasTrigger(PaymentsAppendOnlyTrigger));
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).ValueGeneratedNever();
            payment.Property(p => p.Reference).HasMaxLength(200);
            payment.HasOne<FeeCharge>().WithMany().HasForeignKey(p => p.ChargeId).OnDelete(DeleteBehavior.Restrict);
            payment.HasIndex(p => p.ChargeId).IsUnique();
        });

        modelBuilder.Entity<FeeScheduleRow>(schedule =>
        {
            schedule.ToTable("fee_schedules", table =>
            {
                table.HasTrigger(ImmutableTrigger);
                table.HasTrigger(NoDeleteTrigger);
                table.HasCheckConstraint("ck_fee_schedule_four_eyes", "[approved_by] IS NULL OR [approved_by] <> [created_by]");
            });
            schedule.HasKey(s => s.Version);
            schedule.Property(s => s.Version).ValueGeneratedNever();
            schedule.HasJsonCheck("definition");
            schedule.HasIndex(s => s.EffectiveFrom);
            schedule.HasData(FeeScheduleRow.Seed);
        });
    }
}

/// <summary>
/// A platform fee charge (INV-F3) or its reversal: amounts and the deal snapshot never change; a charge moves
/// Due → Invoiced → Paid, a reversal PendingApproval → Effective (four-eyes). Corrections are reversals.
/// </summary>
internal sealed class FeeCharge
{
    public const string ChargeKind = "Charge";
    public const string ReversalKind = "Reversal";

    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public Guid PayerOrgId { get; init; }

    /// <summary><c>Factor</c> or <c>Seller</c>.</summary>
    public required string PayerRole { get; init; }

    public required string Kind { get; init; }

    /// <summary>The net fee, never negative; a reversal states the amount it takes back.</summary>
    public required Money Amount { get; init; }

    /// <summary>In the amount's currency (0 while VAT is pending, LQ-43).</summary>
    public decimal VatAmount { get; init; }

    public bool VatPending { get; init; }

    public int FeeScheduleVersion { get; init; }

    public required string Status { get; set; }

    public DateOnly? DueAt { get; init; }

    public string? ExternalInvoiceRef { get; set; }

    public Guid? ReversesId { get; init; }

    public string? Reason { get; init; }

    public required string DealReference { get; init; }

    public required string InvoiceNumber { get; init; }

    public decimal FaceValueAmount { get; init; }

    public DateTimeOffset FundedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? ApprovedBy { get; set; }

    /// <summary>When a reversal took effect; it counts in the statement of that month.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>A recorded payment of a charge; append-only.</summary>
internal sealed class FeePayment
{
    public Guid Id { get; init; }

    public Guid ChargeId { get; init; }

    public DateOnly PaidOn { get; init; }

    public required string Reference { get; init; }

    public Guid RecordedBy { get; init; }

    public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// One fee schedule version. Everything is fixed at insert; only the approval (approved_by, approved_at) is set
/// once, by a second person (four-eyes). Triggers enforce both.
/// </summary>
internal sealed class FeeScheduleRow
{
    /// <summary>The system creates and approves the seed; no person can be the second approver of it.</summary>
    public static readonly Guid SystemUser = Guid.Empty;

    /// <summary>Version 1 (task 017a): the factor pays 15% of the discount, no minimum, no maximum, VAT pending LQ-43.</summary>
    public static readonly FeeScheduleRow Seed = new()
    {
        Version = 1,
        Definition = """{"payer":"Factor","basis":"Discount","share":0.15,"sellerShareOfFee":null,"minimumFee":null,"maximumFee":null,"vatTreatment":"TBD","currency":"RSD"}""",
        EffectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        CreatedBy = SystemUser,
        CreatedAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero),
        ApprovedBy = new Guid("00000000-0000-0000-0000-000000000001"),
        ApprovedAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero),
    };

    public int Version { get; init; }

    public required string Definition { get; init; }

    public DateTimeOffset EffectiveFrom { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
}

internal sealed class FeesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FeesDbContext>
{
    public FeesDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<FeesDbContext>(FeesDbContext.SchemaName);
}
