using Microsoft.EntityFrameworkCore;
using Modules.Deals.Contracts;
using Modules.Deals.Persistence;
using Modules.Receivables.Contracts;
using Shared.Kernel;

namespace Modules.Deals;

internal sealed class DealStats(DealsDbContext deals, IReceivableMarket receivables, TimeProvider timeProvider) : IDealStats
{
    private static readonly DealState[] Ended = [DealState.Closed, DealState.Cancelled, DealState.Terminated];

    private static readonly DealState[] FundedOrLater = [DealState.Funded, DealState.Closed];

    public async Task<OrganizationDealStats> ForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var mine = await deals.Deals.AsNoTracking().Where(d => d.SellerOrgId == organizationId || d.FactorOrgId == organizationId).ToListAsync(cancellationToken);
        var fundedAt = await FundedAtAsync(mine.Select(d => d.Id).ToList(), cancellationToken);
        var now = timeProvider.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var funded = mine.Where(d => FundedOrLater.Contains(d.State) && fundedAt.ContainsKey(d.Id)).ToList();
        var faceSum = funded.Sum(d => d.FaceValue.Amount);
        decimal? rate = faceSum == 0 ? null : Math.Round(funded.Sum(d => d.FaceValue.Amount - d.PurchasePriceAmount) / faceSum, 4, MidpointRounding.ToEven);

        var today = BusinessDayCalendar.Today(timeProvider);
        var buckets = new Dictionary<string, (int Count, List<Money> Face)>();
        var overdue = 0;
        foreach (var deal in mine.Where(d => d.State == DealState.Funded).Take(500))
        {
            if (await receivables.GetAsync(deal.ReceivableId, cancellationToken) is not { } receivable)
            {
                continue;
            }

            var days = receivable.MaturityDate.DayNumber - today.DayNumber;
            var bucket = days < 0 ? "overdue" : days <= 30 ? "0-30" : days <= 60 ? "31-60" : days <= 90 ? "61-90" : "90+";
            if (!buckets.TryGetValue(bucket, out var entry))
            {
                entry = (0, []);
            }

            entry.Face.Add(deal.FaceValue);
            buckets[bucket] = (entry.Count + 1, entry.Face);
            overdue += receivable.MaturityDate.AddDays(DealJobs.OutcomeOverdueDays) < today ? 1 : 0;
        }

        return new OrganizationDealStats(
            mine.Count(d => !Ended.Contains(d.State) && d.State != DealState.Funded),
            Sum(funded.Where(d => fundedAt[d.Id] >= monthStart).Select(d => new Money(d.PurchasePriceAmount, d.FaceValue.Currency))),
            Sum(funded.Where(d => fundedAt[d.Id] >= yearStart).Select(d => new Money(d.PurchasePriceAmount, d.FaceValue.Currency))),
            rate,
            overdue,
            buckets.Select(b => new MaturityBucket(b.Key, b.Value.Count, Sum(b.Value.Face))).OrderBy(b => b.Bucket, StringComparer.Ordinal).ToList());
    }

    public async Task<PlatformDealStats> PlatformAsync(int stuckDays, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-stuckDays);
        var open = await deals.Deals.AsNoTracking().Where(d => !Ended.Contains(d.State) && d.State != DealState.Funded).Select(d => new { d.Id, d.State }).ToListAsync(cancellationToken);
        var ids = open.Select(d => d.Id).ToList();
        var lastChange = await deals.StateHistory.AsNoTracking().Where(c => ids.Contains(c.DealId)).GroupBy(c => c.DealId)
            .Select(g => new { DealId = g.Key, At = g.Max(c => c.At) }).ToListAsync(cancellationToken);
        var stuck = open.Where(d => lastChange.SingleOrDefault(c => c.DealId == d.Id)?.At is { } at && at < cutoff)
            .GroupBy(d => d.State).Select(g => new StuckDeals(g.Key, g.Count())).ToList();

        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var fundedIds = await deals.StateHistory.AsNoTracking().Where(c => c.ToState == DealState.Funded && c.At >= monthStart).Select(c => c.DealId).Distinct().ToListAsync(cancellationToken);
        var funded = await deals.Deals.AsNoTracking().Where(d => fundedIds.Contains(d.Id)).ToListAsync(cancellationToken);
        return new PlatformDealStats(
            stuck,
            funded.Count,
            Sum(funded.Select(d => new Money(d.PurchasePriceAmount, d.FaceValue.Currency))),
            Sum(funded.Select(d => new Money(d.PlatformFeeAmount, d.FaceValue.Currency))));
    }

    private async Task<Dictionary<Guid, DateTimeOffset>> FundedAtAsync(List<Guid> ids, CancellationToken cancellationToken) =>
        (await deals.StateHistory.AsNoTracking().Where(c => ids.Contains(c.DealId) && c.ToState == DealState.Funded)
            .Select(c => new { c.DealId, c.At }).ToListAsync(cancellationToken))
        .GroupBy(c => c.DealId).ToDictionary(g => g.Key, g => g.Min(c => c.At));

    /// <summary>One sum per currency, rounded to minor units (money-rules: never add RSD and EUR).</summary>
    private static List<Money> Sum(IEnumerable<Money> amounts) =>
        amounts.GroupBy(m => m.Currency).Select(g => new Money(g.Sum(m => m.Amount), g.Key).Round()).ToList();
}
