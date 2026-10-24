using Microsoft.EntityFrameworkCore;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;

namespace Modules.Organizations;

internal sealed class OrganizationStats(OrgsDbContext orgs) : IOrganizationStats
{
    public async Task<OrganizationQueues> QueuesAsync(CancellationToken cancellationToken) => new(
        await orgs.BankAccounts.IgnoreQueryFilters().CountAsync(a => a.Status == BankAccountStatus.Pending, cancellationToken),
        await orgs.Pristupnice.CountAsync(p => p.Status == PristupnicaStatus.Uploaded || p.Status == PristupnicaStatus.EmailConfirmed, cancellationToken),
        await orgs.MfaResetRequests.CountAsync(r => MfaResetStatus.Open.Contains(r.Status), cancellationToken),
        await orgs.SefConnections.IgnoreQueryFilters().CountAsync(c => c.Status == SefConnectionStatus.Invalid, cancellationToken));
}
