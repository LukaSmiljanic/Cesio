using Microsoft.EntityFrameworkCore;
using Modules.Compliance.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Requests;

namespace Modules.Marketplace;

/// <summary>The factor organization a marketplace request acts for, after the INV-A2 checks.</summary>
internal sealed record FactorViewer(Guid OrganizationId, string Pib, string? LegalName);

/// <summary>
/// The one visibility rule of the marketplace (task 012 technical requirement), reused by the feed, the detail, the
/// listing documents and offer creation (task 013): an Approved, authorized factor without a hold sees an Open,
/// unexpired listing whose audience includes it and does not exclude it, unless it is the seller or a related party
/// (the same PIB as the seller or the debtor, INV-A4; ownership links come with task 007).
/// </summary>
internal sealed class ListingVisibility(
    MarketplaceDbContext marketplace,
    IOrganizationQuery organizations,
    IAuthorizationQuery authorizations,
    IHoldQuery holds,
    IRequestContext request,
    TimeProvider timeProvider)
{
    /// <summary>The request's organization as a factor viewer, or null when it may not use the marketplace.</summary>
    public async Task<FactorViewer?> ViewerAsync(CancellationToken cancellationToken)
    {
        if (request.OrganizationId is not { } orgId
            || await organizations.GetAsync(orgId, cancellationToken) is not { Type: OrganizationType.Factor, IsApproved: true } factor
            || !await authorizations.IsFactorAuthorizedAsync(orgId, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Organization, orgId, cancellationToken))
        {
            return null;
        }

        return new FactorViewer(orgId, factor.Pib, factor.LegalName);
    }

    public IQueryable<Listing> Visible(FactorViewer viewer)
    {
        var now = timeProvider.GetUtcNow();
        var orgId = viewer.OrganizationId;
        var pib = viewer.Pib;
        return marketplace.Listings.AsNoTracking().Where(l =>
            l.State == ListingState.Open
            && l.ClosesAt > now
            && l.SellerOrgId != orgId
            && l.SellerPib != pib
            && l.DebtorPib != pib
            && (l.AudienceType == AudienceType.AllVerifiedFactors
                || marketplace.ListingAudience.Any(a => a.ListingId == l.Id && a.FactorOrgId == orgId && a.Mode == AudienceMode.Include))
            && !marketplace.ListingAudience.Any(a => a.ListingId == l.Id && a.FactorOrgId == orgId && a.Mode == AudienceMode.Exclude));
    }

    /// <summary>The listing if the request's organization may see it; null otherwise (callers answer 404).</summary>
    public async Task<(FactorViewer Viewer, Listing Listing)?> VisibleListingAsync(Guid listingId, CancellationToken cancellationToken)
    {
        if (await ViewerAsync(cancellationToken) is not { } viewer)
        {
            return null;
        }

        var listing = await Visible(viewer).SingleOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        return listing is null ? null : (viewer, listing);
    }

    /// <summary>Whether the request's organization sees any listing of the receivable (for its documents).</summary>
    public async Task<bool> SeesReceivableAsync(Guid receivableId, CancellationToken cancellationToken) =>
        await ViewerAsync(cancellationToken) is { } viewer
        && await Visible(viewer).AnyAsync(l => l.ReceivableId == receivableId, cancellationToken);
}
