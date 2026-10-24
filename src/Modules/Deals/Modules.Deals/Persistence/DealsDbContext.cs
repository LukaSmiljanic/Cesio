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

    internal DbSet<Signature> Signatures => Set<Signature>();

    internal DbSet<TrustedCertificate> TrustedCertificates => Set<TrustedCertificate>();

    internal DbSet<CefRegistration> CefRegistrations => Set<CefRegistration>();

    internal DbSet<DebtorNotification> DebtorNotifications => Set<DebtorNotification>();

    internal DbSet<FundingReport> FundingReports => Set<FundingReport>();

    internal DbSet<FundingConfirmation> FundingConfirmations => Set<FundingConfirmation>();

    internal DbSet<Outcome> Outcomes => Set<Outcome>();

    internal DbSet<Dispute> Disputes => Set<Dispute>();

    internal DbSet<DisputeComment> DisputeComments => Set<DisputeComment>();

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

        modelBuilder.Entity<Signature>(signature =>
        {
            signature.ToTable("signatures", table => table.HasTrigger("tr_signatures_append_only"));
            signature.HasKey(s => s.Id);
            signature.Property(s => s.Id).ValueGeneratedNever();
            signature.Property(s => s.Party).HasMaxLength(10).IsUnicode(false);
            signature.Property(s => s.SignerName).HasMaxLength(200);
            signature.Property(s => s.CertificateSubject).HasMaxLength(400);
            signature.Property(s => s.CertificateIssuer).HasMaxLength(400);
            signature.Property(s => s.CertificateSerial).HasMaxLength(100).IsUnicode(false);
            signature.Property(s => s.Result).HasMaxLength(40).IsUnicode(false);
            signature.HasJsonCheck("report");
            signature.HasOne<Deal>().WithMany().HasForeignKey(s => s.DealId).OnDelete(DeleteBehavior.Restrict);
            signature.HasIndex(s => new { s.DealId, s.ValidatedAt });
        });

        modelBuilder.Entity<TrustedCertificate>(certificate =>
        {
            certificate.ToTable("trusted_certificates");
            certificate.HasKey(c => c.Id);
            certificate.Property(c => c.Id).ValueGeneratedNever();
            certificate.Property(c => c.Name).HasMaxLength(200);
            certificate.Property(c => c.Sha256Thumbprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            certificate.HasIndex(c => c.Sha256Thumbprint).IsUnique();
        });

        modelBuilder.Entity<CefRegistration>(cef =>
        {
            cef.ToTable("cef_registrations");
            cef.HasKey(c => c.DealId);
            cef.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            cef.Property(c => c.Note).HasMaxLength(400);
            cef.Property(c => c.Reference).HasMaxLength(100);
            cef.HasOne<Deal>().WithOne().HasForeignKey<CefRegistration>(c => c.DealId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DebtorNotification>(notification =>
        {
            notification.ToTable("debtor_notifications", table => table.HasTrigger("tr_debtor_notifications_append_only"));
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.Channel).HasMaxLength(20).IsUnicode(false);
            notification.HasOne<Deal>().WithMany().HasForeignKey(n => n.DealId).OnDelete(DeleteBehavior.Restrict);
            notification.HasIndex(n => n.DealId);
        });

        modelBuilder.Entity<FundingReport>(report =>
        {
            report.ToTable("funding_reports", table => table.HasTrigger("tr_funding_reports_append_only"));
            report.HasKey(r => r.Id);
            report.Property(r => r.Id).ValueGeneratedNever();
            report.HasMoney(r => r.Amount, "amount", "currency");
            report.Property(r => r.PaymentReference).HasMaxLength(100);
            report.Property(r => r.PayeeAccountNumber).HasMaxLength(18).IsFixedLength().IsUnicode(false);
            report.HasOne<Deal>().WithMany().HasForeignKey(r => r.DealId).OnDelete(DeleteBehavior.Restrict);
            report.HasIndex(r => r.DealId).IsUnique();
        });

        modelBuilder.Entity<FundingConfirmation>(confirmation =>
        {
            confirmation.ToTable("funding_confirmations", table => table.HasTrigger("tr_funding_confirmations_append_only"));
            confirmation.HasKey(c => c.Id);
            confirmation.Property(c => c.Id).ValueGeneratedNever();
            confirmation.Property(c => c.Reason).HasMaxLength(400);
            confirmation.HasOne<Deal>().WithMany().HasForeignKey(c => c.DealId).OnDelete(DeleteBehavior.Restrict);
            confirmation.HasIndex(c => c.DealId);
        });

        modelBuilder.Entity<Outcome>(outcome =>
        {
            outcome.ToTable("outcomes", table => table.HasTrigger("tr_outcomes_append_only"));
            outcome.HasKey(o => o.Id);
            outcome.Property(o => o.Id).ValueGeneratedNever();
            outcome.Property(o => o.Type).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            outcome.Property(o => o.AmountCollectedAmount).HasColumnName("amount_collected");
            outcome.HasOne<Deal>().WithMany().HasForeignKey(o => o.DealId).OnDelete(DeleteBehavior.Restrict);
            outcome.HasIndex(o => o.DealId).IsUnique();
        });

        modelBuilder.Entity<Dispute>(dispute =>
        {
            dispute.ToTable("disputes");
            dispute.HasKey(d => d.Id);
            dispute.Property(d => d.Id).ValueGeneratedNever();
            dispute.Property(d => d.Kind).HasMaxLength(40).IsUnicode(false);
            dispute.Property(d => d.Status).HasMaxLength(20).IsUnicode(false);
            dispute.Property(d => d.Reason).HasMaxLength(1000);
            dispute.HasOne<Deal>().WithMany().HasForeignKey(d => d.DealId).OnDelete(DeleteBehavior.Restrict);
            dispute.Property(d => d.ResolutionSummary).HasMaxLength(2000);
            dispute.HasIndex(d => new { d.DealId, d.Status });
            dispute.HasIndex(d => new { d.Status, d.OpenedAt });
        });

        modelBuilder.Entity<DisputeComment>(comment =>
        {
            comment.ToTable("dispute_comments", table => table.HasCheckConstraint("ck_dispute_comment_content", "[text] IS NOT NULL OR [document_id] IS NOT NULL"));
            comment.HasKey(c => c.Id);
            comment.Property(c => c.Id).ValueGeneratedNever();
            comment.Property(c => c.Text).HasMaxLength(2000);
            comment.HasOne<Dispute>().WithMany().HasForeignKey(c => c.DisputeId).OnDelete(DeleteBehavior.Restrict);
            comment.HasIndex(c => new { c.DisputeId, c.CreatedAt });
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

/// <summary>One validated signature of an uploaded signed contract, with the validator's report as evidence; append-only.</summary>
internal sealed class Signature
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public Guid DocumentId { get; init; }

    /// <summary><c>Seller</c> or <c>Factor</c> when the signer is one of that party's legal representatives; else null.</summary>
    public string? Party { get; init; }

    public required string SignerName { get; init; }

    public required string CertificateSubject { get; init; }

    public required string CertificateIssuer { get; init; }

    public required string CertificateSerial { get; init; }

    public DateTimeOffset? SigningTime { get; init; }

    /// <summary><c>Valid</c>, or the first failed rule (e.g. <c>signature_invalid</c>, <c>document_mismatch</c>, <c>signer_not_authorized</c>).</summary>
    public required string Result { get; init; }

    public required string Report { get; init; }

    public Guid UploadedBy { get; init; }

    public DateTimeOffset ValidatedAt { get; init; }
}

/// <summary>A root or intermediate certificate of a Serbian qualified trust service provider (integrations.md §7); staff-maintained.</summary>
internal sealed class TrustedCertificate
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Sha256Thumbprint { get; init; }

    /// <summary>The certificate (DER, Base64).</summary>
    public required string Certificate { get; init; }

    public Guid AddedBy { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    public Guid? DeactivatedBy { get; set; }

    public int Version { get; set; }
}

internal enum CefStatus
{
    NotApplicable,
    Pending,
    Registered,
}

/// <summary>The deal's CEF registration evidence (REG-05; manual until the CEF is live, ADR-019).</summary>
internal sealed class CefRegistration
{
    public Guid DealId { get; init; }

    public CefStatus Status { get; set; }

    public string? Note { get; init; }

    public string? Reference { get; set; }

    public Guid? EvidenceDocumentId { get; set; }

    public Guid? RecordedBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RegisteredAt { get; set; }

    public int Version { get; set; }
}

/// <summary>Evidence that the debtor was notified of the assignment (LQ-14); append-only.</summary>
internal sealed class DebtorNotification
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public required string Channel { get; init; }

    public DateTimeOffset SentAt { get; init; }

    public Guid EvidenceDocumentId { get; init; }

    public Guid RecordedByOrgId { get; init; }

    public Guid RecordedBy { get; init; }

    public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>The factor's report that it paid the purchase price (api.md §3.8, INV-F2); one per deal, append-only.</summary>
internal sealed class FundingReport
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public required Money Amount { get; init; }

    public DateOnly ValueDate { get; init; }

    public required string PaymentReference { get; init; }

    public Guid PayeeBankAccountId { get; init; }

    /// <summary>The 18 digits of the payee account at reporting time (evidence; never logged).</summary>
    public required string PayeeAccountNumber { get; init; }

    public Guid ReportedBy { get; init; }

    public DateTimeOffset ReportedAt { get; init; }

    public DateTimeOffset StepUpAt { get; init; }
}

/// <summary>The seller's answer to a funding report (api.md §3.9); never automatic (BQ-12); append-only.</summary>
internal sealed class FundingConfirmation
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public bool Received { get; init; }

    public string? Reason { get; init; }

    public Guid ConfirmedBy { get; init; }

    public DateTimeOffset ConfirmedAt { get; init; }

    public DateTimeOffset StepUpAt { get; init; }
}

/// <summary>The factor's outcome report (api.md §3.10); one per deal, append-only.</summary>
internal sealed class Outcome
{
    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public OutcomeType Type { get; init; }

    public DateOnly Date { get; init; }

    /// <summary>In the deal's currency; optional.</summary>
    public decimal? AmountCollectedAmount { get; init; }

    public Guid ReportedBy { get; init; }

    public DateTimeOffset ReportedAt { get; init; }
}

/// <summary>
/// A dispute on a deal (tasks 017, 021): opened by a party (or automatically by a funding discrepancy), commented on,
/// and resolved by staff or withdrawn by its raising party. Every kind except FeeDispute holds the deal.
/// </summary>
internal sealed class Dispute
{
    public const string Open = "Open";

    public const string FundingDiscrepancy = "FundingDiscrepancy";

    /// <summary>Does not block the deal (task 021).</summary>
    public const string FeeDispute = "FeeDispute";

    public Guid Id { get; init; }

    public Guid DealId { get; init; }

    public required string Kind { get; init; }

    /// <summary><c>Open</c>, <c>Resolved</c> or <c>Withdrawn</c>.</summary>
    public required string Status { get; set; }

    /// <summary>The description given when it was opened.</summary>
    public string? Reason { get; init; }

    public Guid? OpenedBy { get; init; }

    /// <summary>The raising party; null for an automatic dispute.</summary>
    public Guid? OpenedByOrgId { get; init; }

    public DateTimeOffset OpenedAt { get; init; }

    public Guid? AssignedTo { get; set; }

    public string? ResolutionSummary { get; set; }

    public Guid? ClosedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>A comment or attachment on a dispute; internal notes are for staff only.</summary>
internal sealed class DisputeComment
{
    public Guid Id { get; init; }

    public Guid DisputeId { get; init; }

    public Guid? AuthorUserId { get; init; }

    /// <summary>The party's organization; null for staff.</summary>
    public Guid? AuthorOrgId { get; init; }

    public string? Text { get; init; }

    public Guid? DocumentId { get; init; }

    public bool Internal { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class DealsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DealsDbContext>
{
    public DealsDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<DealsDbContext>(DealsDbContext.SchemaName);
}
