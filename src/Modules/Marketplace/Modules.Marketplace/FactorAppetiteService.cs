using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Marketplace;

/// <param name="MinAmount">Face value in RSD (<c>{ amount: "…", currency: "RSD" }</c>), inclusive; null for no lower bound.</param>
/// <param name="MinTenorDays">Days from today to maturity, inclusive.</param>
public sealed record FactorAppetiteRequest(Money? MinAmount, Money? MaxAmount, int? MinTenorDays, int? MaxTenorDays, bool DigestEnabled);

/// <summary>
/// A factor's appetite filters (task 019): simple amount and tenor ranges that choose which new listings it hears about
/// in the hourly digest. They do not change what the factor may see.
/// </summary>
internal sealed class FactorAppetiteService(
    MarketplaceDbContext marketplace,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOrganizationQuery organizations,
    IAuthorizationQuery authorizations,
    IHoldQuery holds,
    TimeProvider timeProvider) : IListingMatches
{
    /// <summary>The largest range bound accepted (fits decimal(19,4) with room).</summary>
    private const decimal MaxAmount = 1_000_000_000_000m;

    public async Task<IResult> GetAsync(Guid orgId, CancellationToken cancellationToken) =>
        Results.Ok(View(await marketplace.FactorAppetites.AsNoTracking().SingleOrDefaultAsync(a => a.OrgId == orgId, cancellationToken)));

    /// <summary><c>PUT /organizations/{id}/factor-appetite</c>.</summary>
    public async Task<IResult> SetAsync(Guid orgId, FactorAppetiteRequest body, CancellationToken cancellationToken)
    {
        if (await organizations.GetAsync(orgId, cancellationToken) is not { Type: OrganizationType.Factor })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only factor organizations set an appetite.");
        }

        var amounts = new[] { body.MinAmount, body.MaxAmount }.OfType<Money>().ToList();
        if (amounts.Any(m => m.Currency != Currency.Rsd || m.Amount < 0 || m.Amount > MaxAmount || m.Amount != m.Round().Amount)
            || body.MinAmount?.Amount > body.MaxAmount?.Amount || body.MinTenorDays < 0 || body.MaxTenorDays < 0
            || body.MinTenorDays > body.MaxTenorDays || body.MaxTenorDays > 3650)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give non-negative ranges with the minimum not above the maximum.");
        }

        var appetite = await marketplace.FactorAppetites.SingleOrDefaultAsync(a => a.OrgId == orgId, cancellationToken);
        var before = Describe(appetite);
        if (appetite is null)
        {
            appetite = new FactorAppetite { OrgId = orgId };
            marketplace.FactorAppetites.Add(appetite);
        }

        appetite.MinAmount = body.MinAmount?.Amount;
        appetite.MaxAmount = body.MaxAmount?.Amount;
        appetite.MinTenorDays = body.MinTenorDays;
        appetite.MaxTenorDays = body.MaxTenorDays;
        appetite.DigestEnabled = body.DigestEnabled;
        appetite.UpdatedAt = timeProvider.GetUtcNow();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await marketplace.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("FactorAppetite.Set", "Organization", orgId, orgId,
                    Changes: new Dictionary<string, string?> { ["appetite"] = $"{before} → {Describe(appetite)}" }), ct);
            },
            cancellationToken);
        return Results.Ok(View(appetite));
    }

    public async Task<IReadOnlyList<Guid>> FactorsMatchingAsync(Guid listingId, CancellationToken cancellationToken)
    {
        if (await marketplace.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId, cancellationToken) is not { State: ListingState.Open } listing)
        {
            return [];
        }

        var tenor = listing.MaturityDate.DayNumber - BusinessDayCalendar.Today(timeProvider).DayNumber;
        var audience = await marketplace.ListingAudience.AsNoTracking().Where(a => a.ListingId == listingId).ToListAsync(cancellationToken);
        var result = new List<Guid>();
        foreach (var appetite in await marketplace.FactorAppetites.AsNoTracking().Where(a => a.DigestEnabled).ToListAsync(cancellationToken))
        {
            var orgId = appetite.OrgId;
            var inAudience = listing.AudienceType == AudienceType.AllVerifiedFactors
                ? audience.All(a => !(a.FactorOrgId == orgId && a.Mode == AudienceMode.Exclude))
                : audience.Any(a => a.FactorOrgId == orgId && a.Mode == AudienceMode.Include);
            var fits = listing.FaceValue.Currency.Code == appetite.Currency
                && (appetite.MinAmount is null || listing.FaceValue.Amount >= appetite.MinAmount)
                && (appetite.MaxAmount is null || listing.FaceValue.Amount <= appetite.MaxAmount)
                && (appetite.MinTenorDays is null || tenor >= appetite.MinTenorDays)
                && (appetite.MaxTenorDays is null || tenor <= appetite.MaxTenorDays);
            if (!inAudience || !fits || orgId == listing.SellerOrgId)
            {
                continue;
            }

            // The marketplace visibility rule (task 012): an Approved, authorized factor without a hold, not a related party.
            if (await organizations.GetAsync(orgId, cancellationToken) is { Type: OrganizationType.Factor, IsApproved: true } factor
                && factor.Pib != listing.SellerPib && factor.Pib != listing.DebtorPib
                && await authorizations.IsFactorAuthorizedAsync(orgId, cancellationToken)
                && !await holds.HasActiveHoldAsync(HoldSubject.Organization, orgId, cancellationToken))
            {
                result.Add(orgId);
            }
        }

        return result;
    }

    private static string Describe(FactorAppetite? a) =>
        a is null ? "none" : FormattableString.Invariant($"{a.MinAmount}-{a.MaxAmount} {a.Currency}, {a.MinTenorDays}-{a.MaxTenorDays} days, digest {a.DigestEnabled}");

    private static object View(FactorAppetite? a) => new
    {
        minAmount = a?.MinAmount is { } min ? new Money(min, Currency.Rsd).Round() : null,
        maxAmount = a?.MaxAmount is { } max ? new Money(max, Currency.Rsd).Round() : null,
        minTenorDays = a?.MinTenorDays,
        maxTenorDays = a?.MaxTenorDays,
        digestEnabled = a?.DigestEnabled ?? false,
    };
}
