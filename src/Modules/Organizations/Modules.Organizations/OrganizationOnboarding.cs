using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Outbox;
using Shared.Kernel;

namespace Modules.Organizations;

/// <summary><see cref="IOrganizationOnboarding"/>: the KYB-driven status changes of an organization (task 007).</summary>
internal sealed class OrganizationOnboarding(OrgsDbContext orgs, IAuditWriter audit, IOutbox outbox) : IOrganizationOnboarding
{
    private static readonly Dictionary<OnboardingStatus, OnboardingStatus[]> Allowed = new()
    {
        [OnboardingStatus.UnderReview] = [OnboardingStatus.Submitted],
        [OnboardingStatus.Approved] = [OnboardingStatus.UnderReview],
        [OnboardingStatus.Rejected] = [OnboardingStatus.UnderReview],
        [OnboardingStatus.Draft] = [OnboardingStatus.UnderReview],
    };

    public async Task MoveAsync(Guid organizationId, OnboardingStatus to, string reason, CancellationToken cancellationToken)
    {
        var organization = await orgs.Organizations.IgnoreQueryFilters().SingleAsync(o => o.Id == organizationId, cancellationToken);
        var from = organization.OnboardingStatus;
        if (!Allowed.TryGetValue(to, out var sources) || !sources.Contains(from))
        {
            throw new DomainConflictException("conflict_state", $"The organization is {from}; it cannot become {to}.");
        }

        organization.OnboardingStatus = to;
        await orgs.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry($"Organization.{to}", "Organization", organizationId, organizationId,
            PreviousState: from.ToString(), NewState: to.ToString(), Reason: reason), cancellationToken);
        if (to == OnboardingStatus.Approved)
        {
            await outbox.AddAsync(new OrganizationApproved(organizationId, organization.OrgType), cancellationToken);
        }
    }
}
