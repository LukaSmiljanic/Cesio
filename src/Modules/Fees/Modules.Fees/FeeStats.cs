using Microsoft.EntityFrameworkCore;
using Modules.Fees.Contracts;
using Modules.Fees.Persistence;
using Shared.Kernel;

namespace Modules.Fees;

internal sealed class FeeStats(FeesDbContext fees) : IFeeStats
{
    public async Task<IReadOnlyList<Money>> OutstandingAsync(Guid payerOrgId, CancellationToken cancellationToken)
    {
        var charges = await fees.FeeCharges.AsNoTracking()
            .Where(c => c.PayerOrgId == payerOrgId && c.Kind == FeeCharge.ChargeKind && (c.Status == "Due" || c.Status == "Invoiced")
                && !fees.FeeCharges.Any(r => r.ReversesId == c.Id && r.Status == "Effective"))
            .ToListAsync(cancellationToken);
        return charges.GroupBy(c => c.Amount.Currency).Select(g => new Money(g.Sum(c => c.Amount.Amount + c.VatAmount), g.Key).Round()).ToList();
    }
}
