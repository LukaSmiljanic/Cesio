using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Requests;

namespace Modules.Organizations;

/// <summary>Organizations, members, legal representatives and Terms acceptances (api.md Organizations, task 005).</summary>
public static class OrganizationEndpoints
{
    public const string OrganizationRouteValue = "id";

    /// <param name="includeDevelopmentEndpoints">Maps the factor-organization seed path; Development only (tasks/README stubs).</param>
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app, bool includeDevelopmentEndpoints)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();

        api.MapGet("/me/organizations", (OrganizationService service, CancellationToken ct) => service.ListMineAsync(ct));
        api.MapGet("/terms/current", () => Results.Ok(new { version = TermsVersions.Current }));
        api.MapPost("/organizations", (CreateOrganizationRequest body, OrganizationService service, CancellationToken ct) => service.CreateSellerAsync(body, ct))
            .RequireIdempotency();
        api.MapPost("/invitations/accept", (AcceptInvitationRequest body, OrganizationService service, CancellationToken ct) => service.AcceptInvitationAsync(body, ct))
            .RequireIdempotency();
        if (includeDevelopmentEndpoints)
        {
            api.MapPost("/dev/factor-organizations", (CreateFactorOrganizationRequest body, OrganizationService service, CancellationToken ct) => service.CreateFactorAsync(body, ct))
                .RequireIdempotency();
        }

        var organization = api.MapGroup($"/organizations/{{{OrganizationRouteValue}:guid}}").RequireOrganization(OrganizationRouteValue);
        organization.MapGet("", (OrganizationService service, CancellationToken ct) => service.GetAsync(ct));
        organization.MapPatch("", (HttpContext http, UpdateOrganizationRequest body, OrganizationService service, CancellationToken ct) =>
            service.UpdateAsync(body, IfMatch(http), ct)).RequireIdempotency();
        organization.MapPost("/submit", (OrganizationService service, CancellationToken ct) => service.SubmitAsync(ct)).RequireIdempotency();

        organization.MapGet("/members", (OrganizationService service, CancellationToken ct) => service.ListMembersAsync(ct));
        organization.MapPost("/members", (InviteMemberRequest body, OrganizationService service, CancellationToken ct) => service.InviteAsync(body, ct))
            .RequireIdempotency().RequireStepUp();
        organization.MapPatch("/members/{memberId:guid}", (Guid memberId, HttpContext http, UpdateMemberRequest body, OrganizationService service, CancellationToken ct) =>
            service.UpdateMemberAsync(memberId, body, IfMatch(http), ct)).RequireIdempotency().RequireStepUp();
        organization.MapDelete("/members/{memberId:guid}", (Guid memberId, OrganizationService service, CancellationToken ct) =>
            service.RemoveMemberAsync(memberId, ct)).RequireIdempotency().RequireStepUp();

        organization.MapGet("/legal-representatives", (OrganizationService service, CancellationToken ct) => service.ListRepresentativesAsync(ct));
        organization.MapPost("/legal-representatives", (LegalRepresentativeRequest body, OrganizationService service, CancellationToken ct) =>
            service.AddRepresentativeAsync(body, ct)).RequireIdempotency();
        organization.MapPut("/legal-representatives/{representativeId:guid}", (Guid representativeId, LegalRepresentativeRequest body, OrganizationService service, CancellationToken ct) =>
            service.UpdateRepresentativeAsync(representativeId, body, ct)).RequireIdempotency();
        organization.MapDelete("/legal-representatives/{representativeId:guid}", (Guid representativeId, OrganizationService service, CancellationToken ct) =>
            service.RemoveRepresentativeAsync(representativeId, ct)).RequireIdempotency();

        organization.MapGet("/sef-connection", (SefConnectionService service, CancellationToken ct) => service.GetStatusAsync(ct));
        organization.MapPut("/sef-connection", (SefConnectionRequest body, SefConnectionService service, CancellationToken ct) => service.ConnectAsync(body, ct))
            .RequireIdempotency().RequireStepUp();

        organization.MapGet("/terms-acceptances", (OrganizationService service, CancellationToken ct) => service.ListTermsAcceptancesAsync(ct));
        organization.MapPost("/terms-acceptances", (AcceptTermsRequest body, OrganizationService service, CancellationToken ct) => service.AcceptTermsAsync(body, ct))
            .RequireIdempotency();

        return app;
    }

    /// <summary><c>If-Match: "3"</c> → 3 (api.md §1); absent or unparsable → no precondition.</summary>
    private static int? IfMatch(HttpContext http) =>
        int.TryParse(http.Request.Headers.IfMatch.ToString().Trim('"', 'W', '/'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
}
