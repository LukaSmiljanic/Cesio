using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.Documents.Contracts;
using Shared.Infrastructure.Persistence;

namespace Modules.Documents.Persistence;

/// <summary>The <c>documents</c> schema: one row per stored, immutable file (task 006).</summary>
public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options, UnitOfWork? unitOfWork)
    : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "documents";

    internal DbSet<StoredDocument> StoredDocuments => Set<StoredDocument>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredDocument>(document =>
        {
            document.ToTable("stored_documents");
            document.HasKey(d => d.Id);
            document.Property(d => d.Id).ValueGeneratedNever();
            document.Property(d => d.Purpose).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            document.Property(d => d.ScanStatus).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            document.Property(d => d.FileName).HasMaxLength(120);
            document.Property(d => d.ContentType).HasMaxLength(100).IsUnicode(false);
            document.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            document.Property(d => d.BlobKey).HasMaxLength(64).IsUnicode(false);
            document.HasIndex(d => d.BlobKey).IsUnique();
            document.HasIndex(d => d.OwnerOrgId);
        });
}

/// <summary>A stored file. Never updated except for its scan result; "replace" is a new document (task 006).</summary>
internal sealed class StoredDocument
{
    public Guid Id { get; init; }

    public Guid OwnerOrgId { get; init; }

    public Guid UploadedBy { get; init; }

    public DocumentPurpose Purpose { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>Random blob name; the file name is never used as a key.</summary>
    public required string BlobKey { get; init; }

    public ScanStatus ScanStatus { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ScannedAt { get; set; }

    public DocumentInfo ToInfo() => new(Id, OwnerOrgId, Purpose, FileName, ContentType, SizeBytes, Sha256, ScanStatus);
}

internal sealed class DocumentsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<DocumentsDbContext>(DocumentsDbContext.SchemaName);
}
