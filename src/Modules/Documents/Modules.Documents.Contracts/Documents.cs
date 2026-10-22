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
}
