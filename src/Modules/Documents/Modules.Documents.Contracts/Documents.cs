namespace Modules.Documents.Contracts;

/// <summary>What a document is for; the owning module's access policy decides who may read it (security.md §4).</summary>
public enum DocumentPurpose
{
    KybEvidence,
    PowerOfAttorney,
    Pristupnica,
    InvoiceAttachment,
    ReceivableEvidence,
    ListingAttachment,
    Contract,

    /// <summary>The UBL of an imported SEF invoice (task 010).</summary>
    InvoiceUbl,
}

public enum ScanStatus
{
    Pending,
    Clean,
    Infected,
    Failed,
}

/// <summary>Metadata of a stored, immutable document.</summary>
public sealed record DocumentInfo(
    Guid Id, Guid OwnerOrgId, DocumentPurpose Purpose, string FileName, string ContentType, long SizeBytes, string Sha256, ScanStatus ScanStatus);

/// <summary>
/// Read access to a document beyond its owner organization, implemented by the module that uses the purpose
/// (e.g. Marketplace lets factors that can see a listing read its attachments). Without a granting policy only
/// members of the owner organization can read a document.
/// </summary>
public interface IDocumentAccessPolicy
{
    bool AppliesTo(DocumentPurpose purpose);

    Task<bool> CanReadAsync(DocumentInfo document, Guid userId, Guid? organizationId, CancellationToken cancellationToken);
}

/// <summary>Lets other modules check a document they reference (owner, purpose, scan status, hash).</summary>
public interface IDocumentQuery
{
    Task<DocumentInfo?> GetAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>The content of a clean document, for a module that must inspect it (e.g. signature validation, task 016); null otherwise.</summary>
    Task<byte[]?> ReadCleanAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>
/// Stores a file produced by another module (e.g. the UBL of an imported invoice) with the same checks as an upload:
/// allowed type, size, hash, malware scan.
/// </summary>
public interface IDocumentWriter
{
    /// <returns>The stored document, or null when the content is not an allowed type.</returns>
    Task<DocumentInfo?> StoreAsync(Guid ownerOrgId, DocumentPurpose purpose, string fileName, byte[] content, CancellationToken cancellationToken);
}
