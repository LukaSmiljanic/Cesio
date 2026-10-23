using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shared.Infrastructure.Persistence;

namespace Modules.Fees.Persistence;

/// <summary>The <c>fees</c> schema (database.md §2): versioned fee schedules (task 017a); charges come with task 018.</summary>
public sealed class FeesDbContext(DbContextOptions<FeesDbContext> options, UnitOfWork? unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "fees";

    public const string ImmutableTrigger = "tr_fee_schedules_immutable";

    public const string NoDeleteTrigger = "tr_fee_schedules_no_delete";

    internal DbSet<FeeScheduleRow> FeeSchedules => Set<FeeScheduleRow>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
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
