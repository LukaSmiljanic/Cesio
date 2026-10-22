using Microsoft.AspNetCore.Builder;

namespace Shared.Infrastructure.Requests;

/// <summary>
/// Marks an endpoint as organization-scoped (api.md §1). The Organizations module's middleware validates the active
/// membership before the handler runs and puts the organization into the request context.
/// </summary>
/// <param name="RouteValue">
/// When set, the organization is the route value of that name (<c>/organizations/{id}/…</c>) and a non-member gets
/// 404; otherwise it comes from the <see cref="Header"/> and a missing or foreign one is 403.
/// </param>
public sealed record OrganizationScopedMetadata(string? RouteValue)
{
    public const string Header = "X-Organization-Id";
}

public static class OrganizationScope
{
    /// <summary><c>HttpContext.Items</c> keys set by the organization-context middleware.</summary>
    public const string OrganizationIdItem = "cesio.organization_id";

    public static TBuilder RequireOrganization<TBuilder>(this TBuilder builder, string? routeValue = null)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new OrganizationScopedMetadata(routeValue));
}
