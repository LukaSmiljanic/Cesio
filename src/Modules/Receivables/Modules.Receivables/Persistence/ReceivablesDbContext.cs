using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Receivables.Persistence;

/// <summary>
/// The <c>receivables</c> schema (database.md §2): debtors (shared reference data), invoices (immutable SEF evidence),
/// receivables (the lifecycle, ADR-012) and the SEF status history. Invoices and receivables are filtered by the
/// request's seller organization (security.md §3, defence in depth).
/// </summary>
public sealed class ReceivablesDbContext : ModuleDbContext
{
    public const string SchemaName = "receivables";

    private readonly IRequestContext? _request;

    public ReceivablesDbContext(DbContextOptions<ReceivablesDbContext> options, UnitOfWork? unitOfWork, IRequestContext? request)
        : base(options, unitOfWork) => _request = request;

    /// <summary>For migrations and design-time tools, which have no request.</summary>
    public ReceivablesDbContext(DbContextOptions<ReceivablesDbContext> options, UnitOfWork? unitOfWork)
        : this(options, unitOfWork, null)
    {
    }

    internal DbSet<Debtor> Debtors => Set<Debtor>();

    internal DbSet<Invoice> Invoices => Set<Invoice>();

    internal DbSet<Receivable> Receivables => Set<Receivable>();

    internal DbSet<SefStatusObservation> SefStatusHistory => Set<SefStatusObservation>();

    internal DbSet<DeclarationVersion> DeclarationVersions => Set<DeclarationVersion>();

    internal DbSet<SellerDeclaration> SellerDeclarations => Set<SellerDeclaration>();

    internal DbSet<ReceivableDocument> ReceivableDocuments => Set<ReceivableDocument>();

    internal DbSet<VerificationCheck> VerificationChecks => Set<VerificationCheck>();

    protected override string Schema => SchemaName;

    private Guid? CurrentOrganizationId => _request?.OrganizationId;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Debtor>(debtor =>
        {
            debtor.ToTable("debtors");
            debtor.HasKey(d => d.Id);
            debtor.Property(d => d.Id).ValueGeneratedNever();
            debtor.Property(d => d.Pib).HasMaxLength(9).IsFixedLength().IsUnicode(false);
            debtor.Property(d => d.Mb).HasMaxLength(8).IsUnicode(false);
            debtor.Property(d => d.LegalName).HasMaxLength(200);
            debtor.HasIndex(d => d.Pib).IsUnique();
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.ToTable("invoices", table =>
            {
                table.HasCheckConstraint("ck_invoice_gross", "[gross_amount] = [net_amount] + [vat_amount]");
                table.HasCheckConstraint("ck_invoice_dates", "[due_date] >= [issue_date]");
                table.HasCheckConstraint("ck_invoice_currency", "[currency] IN ('RSD','EUR')");
            });
            invoice.HasKey(i => i.Id);
            invoice.Property(i => i.Id).ValueGeneratedNever();
            invoice.Property(i => i.SefInvoiceId).HasMaxLength(200).IsUnicode(false);
            invoice.Property(i => i.InvoiceNumber).HasMaxLength(200);
            invoice.Property(i => i.UblSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            invoice.Property(i => i.SefStatus).HasMaxLength(40).IsUnicode(false);
            invoice.HasOne<Debtor>().WithMany().HasForeignKey(i => i.DebtorId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasIndex(i => i.SefInvoiceId).IsUnique();
            invoice.HasIndex(i => new { i.SellerOrgId, i.InvoiceNumber, i.IssueDate }).IsUnique();
            invoice.HasQueryFilter(i => i.SellerOrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<Receivable>(receivable =>
        {
            receivable.ToTable("receivables", table => table.HasCheckConstraint("ck_receivable_face_value", "[face_value] > 0"));
            receivable.HasKey(r => r.Id);
            receivable.Property(r => r.Id).ValueGeneratedNever();
            receivable.HasMoney(r => r.FaceValue, "face_value", "currency");
            receivable.Property(r => r.State).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            receivable.Property(r => r.Fingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            receivable.HasOne<Invoice>().WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            receivable.HasOne<Debtor>().WithMany().HasForeignKey(r => r.DebtorId).OnDelete(DeleteBehavior.Restrict);
            // MVP: one live receivable per invoice; a Rejected or Cancelled one may be replaced (database.md §2).
            receivable.HasIndex(r => r.InvoiceId).IsUnique()
                .HasFilter($"[state] <> '{nameof(ReceivableState.Rejected)}' AND [state] <> '{nameof(ReceivableState.Cancelled)}'")
                .HasDatabaseName("ux_receivable_per_invoice");
            receivable.HasIndex(r => r.Fingerprint).HasDatabaseName("ix_receivable_fingerprint");
            receivable.HasIndex(r => new { r.SellerOrgId, r.State });
            receivable.HasQueryFilter(r => r.SellerOrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<SefStatusObservation>(observation =>
        {
            observation.ToTable("sef_status_history");
            observation.HasKey(o => o.Id);
            observation.Property(o => o.Id).ValueGeneratedNever();
            observation.Property(o => o.Status).HasMaxLength(40).IsUnicode(false);
            observation.HasJsonCheck("raw");
            observation.HasOne<Invoice>().WithMany().HasForeignKey(o => o.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            observation.HasIndex(o => new { o.InvoiceId, o.ObservedAt });
        });

        modelBuilder.Entity<DeclarationVersion>(version =>
        {
            version.ToTable("declaration_versions");
            version.HasKey(v => v.Version);
            version.Property(v => v.Version).HasMaxLength(40).IsUnicode(false);
            version.Property(v => v.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            version.HasJsonCheck("item_codes");
            version.HasData(DeclarationVersion.Draft0);
        });

        modelBuilder.Entity<SellerDeclaration>(declaration =>
        {
            declaration.ToTable("seller_declarations");
            declaration.HasKey(d => d.Id);
            declaration.Property(d => d.Id).ValueGeneratedNever();
            declaration.Property(d => d.Version).HasMaxLength(40).IsUnicode(false);
            declaration.Property(d => d.IpAddress).HasMaxLength(45).IsUnicode(false);
            declaration.HasJsonCheck("answers");
            declaration.HasOne<Receivable>().WithMany().HasForeignKey(d => d.ReceivableId).OnDelete(DeleteBehavior.Restrict);
            declaration.HasOne<DeclarationVersion>().WithMany().HasForeignKey(d => d.Version).OnDelete(DeleteBehavior.Restrict);
            declaration.HasIndex(d => new { d.ReceivableId, d.AcceptedAt });
        });

        modelBuilder.Entity<ReceivableDocument>(document =>
        {
            document.ToTable("receivable_documents");
            document.HasKey(d => new { d.ReceivableId, d.DocumentId });
            document.HasOne<Receivable>().WithMany().HasForeignKey(d => d.ReceivableId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VerificationCheck>(check =>
        {
            check.ToTable("verification_checks");
            check.HasKey(c => c.Id);
            check.Property(c => c.Id).ValueGeneratedNever();
            check.Property(c => c.Code).HasMaxLength(40).IsUnicode(false);
            check.Property(c => c.Result).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
            check.Property(c => c.Details).HasMaxLength(400);
            check.HasOne<Receivable>().WithMany().HasForeignKey(c => c.ReceivableId).OnDelete(DeleteBehavior.Restrict);
            check.HasIndex(c => new { c.ReceivableId, c.Run });
        });
    }
}

internal sealed class Debtor
{
    public Guid Id { get; init; }

    public required string Pib { get; init; }

    public string? Mb { get; set; }

    public required string LegalName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Immutable evidence from SEF (ADR-012); corrections arrive as credit notes, never as edits.</summary>
internal sealed class Invoice
{
    public Guid Id { get; init; }

    public Guid SellerOrgId { get; init; }

    public Guid DebtorId { get; init; }

    public required string SefInvoiceId { get; init; }

    public required string InvoiceNumber { get; init; }

    public DateOnly IssueDate { get; init; }

    public DateOnly DueDate { get; init; }

    public required Currency Currency { get; init; }

    public decimal NetAmount { get; init; }

    public decimal VatAmount { get; init; }

    public decimal GrossAmount { get; init; }

    public Guid UblDocumentId { get; init; }

    public required string UblSha256 { get; init; }

    public required string SefStatus { get; set; }

    public DateTimeOffset SefStatusCheckedAt { get; set; }

    public DateTimeOffset ImportedAt { get; init; }
}

internal sealed class Receivable
{
    public Guid Id { get; init; }

    public Guid InvoiceId { get; init; }

    public Guid SellerOrgId { get; init; }

    public Guid DebtorId { get; init; }

    public required Money FaceValue { get; init; }

    public DateOnly MaturityDate { get; init; }

    public ReceivableState State { get; set; }

    /// <summary>INV-R3: SHA-256 of debtor PIB, gross, currency, issue and due date, checked across all sellers (task 011).</summary>
    public required string Fingerprint { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Checks warned (or the amount is above the auto-approval limit): a compliance officer decides (task 011).</summary>
    public bool ReviewRequired { get; set; }

    /// <summary>Why it was rejected: failed check codes, or the reviewer's reason code (e.g. <c>Fraud</c>).</summary>
    public string? RejectionReason { get; set; }

    public int Version { get; set; }
}

/// <summary>One observed SEF status of an invoice, with the raw SEF answer (integrations.md §1 audit).</summary>
internal sealed class SefStatusObservation
{
    public Guid Id { get; init; }

    public Guid InvoiceId { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public required string Raw { get; init; }
}

internal sealed class ReceivablesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ReceivablesDbContext>
{
    public ReceivablesDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<ReceivablesDbContext>(ReceivablesDbContext.SchemaName);
}
