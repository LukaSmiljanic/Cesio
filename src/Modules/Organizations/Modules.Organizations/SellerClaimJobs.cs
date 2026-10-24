using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;

namespace Modules.Organizations;

/// <summary>
/// Task 007a requirement 2: a seller Draft that no valid SEF key claimed within <c>orgs.draftClaimDays</c> (7) is
/// closed, so a typed PIB never blocks anything for long.
/// </summary>
internal sealed class DraftExpiry(OrgsDbContext orgs, UnitOfWork unitOfWork, IAuditWriter audit, IBusinessParameters parameters, TimeProvider timeProvider)
{
    public const string DraftClaimDaysParameter = "orgs.draftClaimDays";

    /// <returns>How many Drafts were closed.</returns>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-await parameters.GetAsync(DraftClaimDaysParameter, 7, cancellationToken));
        var expired = await orgs.Organizations.IgnoreQueryFilters()
            .Where(o => o.OrgType == OrganizationType.Seller && o.ClaimedAt == null && o.OnboardingStatus == OnboardingStatus.Draft && o.CreatedAt <= cutoff)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var organization in expired)
        {
            organization.OnboardingStatus = OnboardingStatus.ClaimClosed;
        }

        if (expired.Count > 0)
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    foreach (var organization in expired)
                    {
                        await audit.WriteAsync(new AuditEntry("Organization.ClaimClosed", "Organization", organization.Id, organization.Id,
                            PreviousState: nameof(OnboardingStatus.Draft), NewState: nameof(OnboardingStatus.ClaimClosed), Reason: "claim_expired"), ct);
                    }
                },
                cancellationToken);
        }

        return expired.Count;
    }
}

/// <summary>Task 007a requirement 8: an approved seller's official email learns that the company is registered on Cesio.</summary>
internal sealed class ApprovalNotice(OrgsDbContext orgs, IEmailSender email) : IIntegrationEventConsumer<OrganizationApproved>
{
    public const string ConsumerName = "orgs.approval-notice";

    public async Task HandleAsync(OrganizationApproved integrationEvent, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var organization = await orgs.Organizations.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == integrationEvent.OrganizationId, cancellationToken);
        if (organization.OrgType != OrganizationType.Seller || organization.OfficialEmail is null)
        {
            return;
        }

        await email.SendAsync(new EmailMessage(organization.OfficialEmail, "Cesio: vaša firma je registrovana",
            $"Firma {organization.LegalName ?? organization.Pib} (PIB {organization.Pib}) registrovana je na platformi Cesio. Ako to niste vi tražili, odmah se javite podršci Cesio."),
            cancellationToken);
    }
}
