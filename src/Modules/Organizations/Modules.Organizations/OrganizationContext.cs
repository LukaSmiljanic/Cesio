using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Organizations;

/// <summary>
/// Validates the organization of every <see cref="OrganizationScopedMetadata"/> endpoint against the user's active
/// membership and puts it into the request context (api.md §1). Runs after authentication and before idempotency.
/// A route-scoped organization the user does not belong to is 404 (no existence leak, security.md §3); a missing or
/// foreign <c>X-Organization-Id</c> header is 403.
/// </summary>
internal sealed class OrganizationContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var scope = http.GetEndpoint()?.Metadata.GetMetadata<OrganizationScopedMetadata>();
        if (scope is null || !Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId))
        {
            await next(http);
            return;
        }

        var raw = scope.RouteValue is { } routeValue
            ? http.GetRouteValue(routeValue)?.ToString()
            : http.Request.Headers[OrganizationScopedMetadata.Header].ToString();
        var membership = Guid.TryParse(raw, out var organizationId)
            ? await http.RequestServices.GetRequiredService<IOrganizationQuery>().GetMembershipAsync(organizationId, userId, http.RequestAborted)
            : null;
        if (membership is null)
        {
            await (scope.RouteValue is null
                    ? Problems.Error(StatusCodes.Status403Forbidden, "forbidden", $"A valid {OrganizationScopedMetadata.Header} of one of your organizations is required.")
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" }))
                .ExecuteAsync(http);
            return;
        }

        http.Items[OrganizationScope.OrganizationIdItem] = organizationId;
        await next(http);
    }
}

internal sealed class OrganizationQuery(OrgsDbContext orgs) : IOrganizationQuery
{
    public Task<OrganizationSummary?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        orgs.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new OrganizationSummary(o.Id, o.OrgType, o.OnboardingStatus, o.Pib, o.LegalName))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<MembershipSummary?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        orgs.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.OrgId == organizationId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => new MembershipSummary(m.OrgId, m.UserId, m.IsSignatory))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> IsSignatoryAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        (await GetMembershipAsync(organizationId, userId, cancellationToken))?.IsSignatory == true;

    public async Task<bool> SellerOnboardingCompleteAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await orgs.Organizations.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == organizationId, cancellationToken);
        return organization.OrgType != OrganizationType.Seller
            || (organization.ClaimedAt is not null
                && await orgs.Pristupnice.AsNoTracking().AnyAsync(p => p.OrgId == organizationId && p.Status == "Accepted", cancellationToken));
    }

    public async Task<IReadOnlyList<string>> AuthorizedSignerNamesAsync(Guid organizationId, DateOnly on, CancellationToken cancellationToken) =>
        await orgs.LegalRepresentatives.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.OrgId == organizationId && (r.ValidFrom == null || r.ValidFrom <= on) && (r.ValidTo == null || r.ValidTo >= on))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);
}
