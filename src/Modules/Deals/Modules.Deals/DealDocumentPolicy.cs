using Microsoft.EntityFrameworkCore;
using Modules.Deals.Persistence;
using Modules.Documents.Contracts;

namespace Modules.Deals;

/// <summary>Both parties of a deal may read the agreement versions uploaded for it (task 015).</summary>
internal sealed class DealDocumentPolicy(DealsDbContext deals) : IDocumentAccessPolicy
{
    public bool AppliesTo(DocumentPurpose purpose) => purpose == DocumentPurpose.Contract;

    public async Task<bool> CanReadAsync(DocumentInfo document, Guid userId, Guid? organizationId, CancellationToken cancellationToken) =>
        organizationId is { } orgId
        && await deals.ContractDocuments.AsNoTracking()
            .Where(c => c.DocumentId == document.Id)
            .AnyAsync(c => deals.Deals.Any(d => d.Id == c.DealId && (d.SellerOrgId == orgId || d.FactorOrgId == orgId)), cancellationToken);
}
