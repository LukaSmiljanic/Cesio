using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.Compliance.Contracts;
using Shared.Infrastructure.Persistence;

namespace Modules.Compliance.Persistence;

/// <summary>
/// The <c>compliance</c> schema (database.md §2): KYB cases with beneficial owners and screening results, the imported
/// sanctions lists, and holds (task 007). Personal data here is for compliance staff only (security.md).
/// </summary>
public sealed class ComplianceDbContext(DbContextOptions<ComplianceDbContext> options, UnitOfWork? unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "compliance";

    internal DbSet<KybCase> KybCases => Set<KybCase>();

    internal DbSet<BeneficialOwner> BeneficialOwners => Set<BeneficialOwner>();

    internal DbSet<ScreeningList> ScreeningLists => Set<ScreeningList>();

    internal DbSet<ScreeningListEntry> ScreeningListEntries => Set<ScreeningListEntry>();

    internal DbSet<ScreeningResult> ScreeningResults => Set<ScreeningResult>();

    internal DbSet<Hold> Holds => Set<Hold>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KybCase>(kyb =>
        {
            kyb.ToTable("kyb_cases", table => table.HasCheckConstraint("ck_kyb_case_four_eyes", "[second_approver] IS NULL OR [second_approver] <> [first_approver]"));
            kyb.HasKey(k => k.Id);
            kyb.Property(k => k.Id).ValueGeneratedNever();
            kyb.Property(k => k.Status).HasMaxLength(30).IsUnicode(false);
            kyb.Property(k => k.RiskRating).HasMaxLength(10).IsUnicode(false);
            kyb.Property(k => k.RiskReason).HasMaxLength(400);
            kyb.Property(k => k.DecisionReason).HasMaxLength(1000);
            kyb.HasIndex(k => k.OrganizationId);
            kyb.HasIndex(k => k.Status);
        });

        modelBuilder.Entity<BeneficialOwner>(owner =>
        {
            owner.ToTable("beneficial_owners", table => table.HasCheckConstraint("ck_beneficial_owner_basis", "[ownership_percent] IS NOT NULL OR [control_type] IS NOT NULL"));
            owner.HasKey(o => o.Id);
            owner.Property(o => o.Id).ValueGeneratedNever();
            owner.Property(o => o.Name).HasMaxLength(200);
            owner.Property(o => o.Nationality).HasMaxLength(2).IsFixedLength().IsUnicode(false);
            owner.Property(o => o.OwnershipPercent).HasPrecision(5, 2);
            owner.Property(o => o.ControlType).HasMaxLength(100);
            owner.HasIndex(o => new { o.OrganizationId, o.Current });
        });

        modelBuilder.Entity<ScreeningList>(list =>
        {
            list.ToTable("screening_lists");
            list.HasKey(l => l.Id);
            list.Property(l => l.Id).ValueGeneratedNever();
            list.Property(l => l.Source).HasMaxLength(20).IsUnicode(false);
            list.Property(l => l.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            list.HasIndex(l => new { l.Source, l.Sha256 }).IsUnique();
            list.HasIndex(l => new { l.Source, l.ImportedAt });
        });

        modelBuilder.Entity<ScreeningListEntry>(entry =>
        {
            entry.ToTable("screening_list_entries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Name).HasMaxLength(400);
            entry.Property(e => e.NormalizedName).HasMaxLength(400);
            entry.HasOne<ScreeningList>().WithMany().HasForeignKey(e => e.ListId).OnDelete(DeleteBehavior.Restrict);
            entry.HasIndex(e => e.ListId);
        });

        modelBuilder.Entity<ScreeningResult>(result =>
        {
            result.ToTable("screening_results");
            result.HasKey(r => r.Id);
            result.Property(r => r.Id).ValueGeneratedNever();
            result.Property(r => r.SubjectName).HasMaxLength(400);
            result.Property(r => r.SubjectKind).HasMaxLength(20).IsUnicode(false);
            result.Property(r => r.ListSource).HasMaxLength(20).IsUnicode(false);
            result.Property(r => r.EntryName).HasMaxLength(400);
            result.Property(r => r.Score).HasPrecision(5, 4);
            result.Property(r => r.Result).HasMaxLength(20).IsUnicode(false);
            result.Property(r => r.ResolutionNote).HasMaxLength(1000);
            result.HasOne<KybCase>().WithMany().HasForeignKey(r => r.KybCaseId).OnDelete(DeleteBehavior.Restrict);
            result.HasIndex(r => new { r.KybCaseId, r.Result });
        });

        modelBuilder.Entity<Hold>(hold =>
        {
            hold.ToTable("holds", table => table.HasCheckConstraint("ck_hold_four_eyes", "[lifted_by] IS NULL OR [lift_requested_by] IS NULL OR [lifted_by] <> [lift_requested_by]"));
            hold.HasKey(h => h.Id);
            hold.Property(h => h.Id).ValueGeneratedNever();
            hold.Property(h => h.SubjectType).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            hold.Property(h => h.Kind).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            hold.Property(h => h.Reason).HasMaxLength(1000);
            hold.HasIndex(h => new { h.SubjectType, h.SubjectId, h.LiftedAt });
        });
    }
}

/// <summary>One KYB review of an organization (compliance.md §1): Open → InReview → Approved | Rejected | NeedsInfo.</summary>
internal sealed class KybCase
{
    public Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public required string Status { get; set; }

    /// <summary><c>Low</c>, <c>Medium</c> or <c>High</c>; High needs a second officer (four-eyes).</summary>
    public string? RiskRating { get; set; }

    public string? RiskReason { get; set; }

    public bool SharingConsent { get; set; }

    public DateTimeOffset? SharingConsentAt { get; set; }

    public Guid? FirstApprover { get; set; }

    public Guid? SecondApprover { get; set; }

    public string? DecisionReason { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset? NextReviewAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public int Version { get; set; }
}

/// <summary>A declared beneficial owner (≥ 25% ownership or control); a new declaration replaces the current one.</summary>
internal sealed class BeneficialOwner
{
    public Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Nationality { get; init; }

    public decimal? OwnershipPercent { get; init; }

    public string? ControlType { get; init; }

    public bool PepDeclared { get; init; }

    public bool Current { get; set; }

    public Guid DeclaredBy { get; init; }

    public DateTimeOffset DeclaredAt { get; init; }

    public int Version { get; set; }
}

/// <summary>One imported version of a sanctions list (UN, APML, EU, OFAC); the newest per source is used.</summary>
internal sealed class ScreeningList
{
    public Guid Id { get; init; }

    public required string Source { get; init; }

    public required string Sha256 { get; init; }

    public int EntryCount { get; init; }

    public Guid ImportedBy { get; init; }

    public DateTimeOffset ImportedAt { get; init; }
}

internal sealed class ScreeningListEntry
{
    public Guid Id { get; init; }

    public Guid ListId { get; init; }

    public required string Name { get; init; }

    public required string NormalizedName { get; init; }
}

/// <summary>A screened name of a KYB case; a potential match is resolved by an officer with a note.</summary>
internal sealed class ScreeningResult
{
    public Guid Id { get; init; }

    public Guid KybCaseId { get; init; }

    /// <summary><c>Organization</c>, <c>Representative</c> or <c>BeneficialOwner</c>.</summary>
    public required string SubjectKind { get; init; }

    public required string SubjectName { get; init; }

    public string? ListSource { get; init; }

    public string? EntryName { get; init; }

    public decimal Score { get; init; }

    /// <summary><c>Clear</c>, <c>PotentialMatch</c> or <c>ConfirmedMatch</c>.</summary>
    public required string Result { get; set; }

    public Guid? ResolvedBy { get; set; }

    public string? ResolutionNote { get; set; }

    public DateTimeOffset ScreenedAt { get; init; }

    public int Version { get; set; }
}

/// <summary>A hold on an organization, receivable or deal (ADR-014): it blocks forward transitions until lifted.</summary>
internal sealed class Hold
{
    public Guid Id { get; init; }

    public HoldSubject SubjectType { get; init; }

    public Guid SubjectId { get; init; }

    public HoldKind Kind { get; init; }

    public required string Reason { get; init; }

    public Guid PlacedBy { get; init; }

    public DateTimeOffset PlacedAt { get; init; }

    /// <summary>The first officer of a FraudInvestigation lift (four-eyes).</summary>
    public Guid? LiftRequestedBy { get; set; }

    public Guid? LiftedBy { get; set; }

    public DateTimeOffset? LiftedAt { get; set; }

    public int Version { get; set; }
}

internal sealed class ComplianceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ComplianceDbContext>
{
    public ComplianceDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<ComplianceDbContext>(ComplianceDbContext.SchemaName);
}
