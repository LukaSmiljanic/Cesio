using Microsoft.EntityFrameworkCore;
using Modules.Compliance.Contracts;
using Modules.Compliance.Persistence;

namespace Modules.Compliance;

internal sealed class ComplianceStats(ComplianceDbContext compliance) : IComplianceStats
{
    public async Task<ComplianceQueues> QueuesAsync(CancellationToken cancellationToken) => new(
        await compliance.KybCases.CountAsync(k => k.Status == "Open" || k.Status == "InReview" || k.Status == "PendingSecondApproval", cancellationToken),
        await compliance.FactorAuthorizations.CountAsync(a => a.Status == FactorAuthorizationStatus.Submitted || a.Status == FactorAuthorizationStatus.PendingSecondApproval, cancellationToken),
        await compliance.Holds.CountAsync(h => h.LiftedAt == null, cancellationToken));
}
