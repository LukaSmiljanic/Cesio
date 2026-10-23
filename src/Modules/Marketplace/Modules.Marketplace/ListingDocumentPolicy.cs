using Modules.Documents.Contracts;
using Modules.Receivables.Contracts;

namespace Modules.Marketplace;

/// <summary>
/// A factor that sees a listing may read its receivable's documents (the invoice UBL and the supporting documents);
/// the Documents module logs every download (task 012, security.md §4).
/// </summary>
internal sealed class ListingDocumentPolicy(IReceivableMarket receivables, ListingVisibility visibility) : IDocumentAccessPolicy
{
    public bool AppliesTo(DocumentPurpose purpose) => purpose is DocumentPurpose.InvoiceUbl or DocumentPurpose.ReceivableEvidence;

    public async Task<bool> CanReadAsync(DocumentInfo document, Guid userId, Guid? organizationId, CancellationToken cancellationToken) =>
        organizationId is not null
        && await receivables.ReceivableOfDocumentAsync(document.Id, cancellationToken) is { } receivableId
        && await visibility.SeesReceivableAsync(receivableId, cancellationToken);
}
