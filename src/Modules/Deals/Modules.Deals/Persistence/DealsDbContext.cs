using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.Deals.Contracts;
using Modules.Marketplace.Contracts;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Deals.Persistence;

/// <summary>The <c>deals</c> schema (database.md §2): deals and their append-only state history (task 014).</summary>
public sealed class DealsDbContext(DbContextOptions<DealsDbContext> options, UnitOfWork? unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "deals";

    public const string ReferenceSequence = "deal_reference_seq";

    public const string HistoryAppendOnlyTrigger = "tr_deal_state_history_append_only";

    public const string DealImmutableTrigger = "tr_deals_immutable";


    internal DbSet<Deal> Deals => Set<Deal>();

    internal DbSet<DealStateChange> StateHistory => Set<DealStateChange>();

    internal DbSet<CddDecision> CddDecisions => Set<CddDecision>();

    internal DbSet<ContractDocument> ContractDocuments => Set<ContractDocument>();

    internal DbSet<TermsConfirmation> TermsConfirmations => Set<TermsConfirmation>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(ReferenceSequence).StartsAt(1);

        modelBuilder.Entity<Deal>(deal =>
        {
            deal.ToTable("deals", table =>
            {
                table.HasTrigger(DealImmutableTrigger);
                table.HasCheckConstraint("ck_deal_prices", "[purchase_price] > 0 AND [purchase_price] < [face_value]");
                table.HasCheckConstraint("ck_deal_fees", "[platform_fee] >= 0 AND [seller_fee] >= 0 AND [seller_fee] <= [platform_fee]");
            });
            deal.HasKey(d => d.Id);
            deal.Property(d => d.Id).ValueGeneratedNever();
            deal.Property(d => d.Reference).HasMaxLength(20).IsUnicode(false);
            deal.HasIndex(d => d.Reference).IsUnique();
            deal.HasMoney(d => d.FaceValue, "face_value", "currency");
            deal.Property(d => d.PurchasePriceAmount).HasColumnName("purchase_price");
            deal.Property(d => d.PlatformFeeAmount).HasColumnName("platform_fee");
            deal.Property(d => d.SellerFeeAmount).HasColumnName("seller_fee");
            deal.Property(d => d.FundingDeadlineBusinessDays).HasColumnName("funding_deadline_bdays");
            deal.Property(d => d.RecourseType).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            deal.Property(d => d.State).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            deal.Property(d => d.ConditionsText).HasMaxLength(1000);
            deal.Property(d => d.AgreedContractSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            deal.HasIndex(d => d.OfferId).IsUnique();
            // INV-R1: the core anti-double-financing guarantee (database.md §2).
            deal.HasIndex(d => d.ReceivableId).IsUnique()
                .HasFilter($"[state] <> '{nameof(DealState.Cancelled)}' AND [state] <> '{nameof(DealState.Terminated)}'")
                .HasDatabaseName("ux_deal_live_per_receivable");
            deal.HasIndex(d => new { d.SellerOrgId, d.State });
            deal.HasIndex(d => new { d.FactorOrgId, d.State });
        });

        modelBuilder.Entity<DealStateChange>(change =>
        {
            change.ToTable("deal_state_history", table => table.HasTrigger(HistoryAppendOnlyTrigger));
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).ValueGeneratedNever();
            change.Property(c => c.FromState).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            change.Property(c => c.ToState).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            change.HasOne<Deal>().WithMany().HasForeignKey(c => c.DealId).OnDelete(DeleteBehavior.Restrict);
            change.HasIndex(c => new { c.DealId, c.At });
        });

        modelBuilder.Entity<CddDecision>(decision =>
        {
            decision.ToTable("cdd_decisions", table => table.HasTrigger("tr_cdd_decisions_append_only"));
            decision.HasKey(d => d.Id);
            decision.Property(d => d.Id).ValueGeneratedNever();
            decision.Property(d => d.Reason).HasMaxLength(400);
            decision.HasOne<Deal>().WithMany().HasForeignKey(d => d.DealId).OnDelete(DeleteBehavior.Restrict);
            decision.HasIndex(d => d.DealId).IsUnique();
        });

        modelBuilder.Entity<ContractDocument>(contract =>
        {
            contract.ToTable("contract_documents", table => table.HasTrigger("tr_contract_documents_append_only"));
            contract.HasKey(c => c.Id);
            contract.Property(c => c.Id).ValueGeneratedNever();
            contract.Property(c => c.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            contract.HasOne<Deal>().WithMany().HasForeignKey(c => c.DealId).OnDelete(DeleteBehavior.Restrict);
            contract.HasIndex(c => new { c.DealId, c.UploadedAt });
        });

        modelBuilder.Entity<TermsConfirmation>(confirmation =>
        {
            confirmation.ToTable("terms_confirmations", table => table.HasTrigger("tr_terms_confirmations_append_only"));
            confirmation.HasKey(c => c.Id);
            confirmation.Property(c => c.Id).ValueGeneratedNever();
            confirmation.Property(c => c.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            confirmation.HasOne<Deal>().WithMany().HasForeignKey(c => c.DealId).OnDelete(DeleteBehavior.Restrict);
            confirmation.HasOne<ContractDocument>().WithMany().HasForeignKey(c => c.ContractDocumentId).OnDelete(DeleteBehavior.Restrict);
            confirmation.HasIndex(c => new { c.ContractDocumentId, c.PartyOrgId }).IsUnique();
        });
    }
}

/// <summary>
/// A deal (domain-model.md): the parties and the terms of the accepted offer, with its fee snapshot, are copied at
/// creation and never change (trigger); the state moves through product-spec.md §7.4.
/// </summary>
internal sealed class Deal
{
    public Guid Id { get; init; }

    public required string Reference { get; init; }

    public Guid ReceivableId { get; init; }

    public Guid ListingId { get; init; }

    public Guid OfferId { get; init; }

    public Guid SellerOrgId { get; init; }

    public Guid FactorOrgId { get; init; }

    public required Money FaceValue { get; init; }

    /// <summary>In the face value's currency.</summary>
    public decimal PurchasePriceAmount { get; init; }

    public RecourseType RecourseType { get; init; }

    public int FundingDeadlineBusinessDays { get; init; }

    public string? ConditionsText { get; init; }

    public int FeeScheduleVersion { get; init; }

    public decimal PlatformFeeAmount { get; init; }

    public decimal SellerFeeAmount { get; init; }

    public bool VatPending { get; init; }

    public DealState State { get; set; }

    public DateTimeOffset SignatureDeadline { get; init; }

    public DateOnly? FundingDeadline { get; set; }

    public Guid AcceptedBy { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }

    /// <summary>The acceptor's MFA step-up time (task 014 security considerations).</summary>
    public DateTimeOffset AcceptedStepUpAt { get; init; }

    /// <summary>SHA-256 of the contract both parties confirmed (task 015); set once, then the signing step checks against it.</summary>
    public string? AgreedContractSha256 { get; set; }

    public int Version { get; set; }
}

/// <summary>One transition of a deal; append-only (database.md §1).</summary>
internal sealed class DealStateChange
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public DealState? FromState { get; init; }

    public DealState ToState { get; init; }

    public Guid? ActorUserId { get; init; }

    public DateTimeOffset At { get; init; }

    public string? Reason { get; init; }
}

/// <summary>The factor's CDD decision on a deal (ADR-015); one per deal, append-only.</summary>
internal sealed class CddDecision
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public bool Approved { get; init; }

    public string? Reason { get; init; }

    public Guid DecidedBy { get; init; }

    public DateTimeOffset DecidedAt { get; init; }

    public DateTimeOffset StepUpAt { get; init; }
}

/// <summary>A version of the factor's agreement for the deal (ADR-018); the newest one is current, older ones stay as evidence.</summary>
internal sealed class ContractDocument
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public Guid DocumentId { get; init; }

    public required string Sha256 { get; init; }

    public Guid UploadedBy { get; init; }

    public DateTimeOffset UploadedAt { get; init; }
}

/// <summary>One party's confirmation that a contract version matches the deal terms (task 015).</summary>
internal sealed class TermsConfirmation
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public Guid ContractDocumentId { get; init; }

    public required string Sha256 { get; init; }

    public Guid PartyOrgId { get; init; }

    public Guid ConfirmedBy { get; init; }

    public DateTimeOffset ConfirmedAt { get; init; }

    public DateTimeOffset StepUpAt { get; init; }
}

internal sealed class DealsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DealsDbContext>
{
    public DealsDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<DealsDbContext>(DealsDbContext.SchemaName);
}
