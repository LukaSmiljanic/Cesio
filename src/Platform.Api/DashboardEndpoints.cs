using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;
using Modules.Fees.Contracts;
using Modules.Identity.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Notifications.Contracts;
using Modules.Organizations.Contracts;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Requests;

/// <summary>
/// The dashboards (task 022, product-spec §9): read queries composed from the modules' contracts, nothing stored.
/// A customer dashboard shows only the active organization's figures; the admin dashboard is for staff.
/// </summary>
internal static class DashboardEndpoints
{
    /// <summary>"Stuck" means no state change for this many days (product-spec §9: deals stuck by step).</summary>
    private const int StuckDays = 5;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/v1/dashboards").RequireAuthorization().RequireOrganization();
        customer.MapGet("/seller", SellerAsync);
        customer.MapGet("/factor", FactorAsync);
        app.MapGet("/api/v1/admin/dashboard", AdminAsync).RequireAuthorization().AddEndpointFilter(RequireStaff);
        return app;
    }

    private static async Task<IResult> SellerAsync(IRequestContext request, IOrganizationQuery organizations, IReceivableStats receivables,
        IMarketplaceStats marketplace, IDealStats deals, IFeeStats fees, CancellationToken ct)
    {
        var orgId = request.OrganizationId!.Value;
        if (await organizations.GetAsync(orgId, ct) is not { Type: OrganizationType.Seller })
        {
            return NotFound();
        }

        var dealStats = await deals.ForOrganizationAsync(orgId, ct);
        return Results.Ok(new
        {
            receivablesByState = await receivables.CountByStateAsync(orgId, ct),
            openListings = await marketplace.OpenListingsAsync(orgId, ct),
            dealsNeedingAction = dealStats.Open,
            fundedThisMonth = dealStats.FundedThisMonth,
            fundedThisYear = dealStats.FundedThisYear,
            averageDiscountRate = dealStats.AverageDiscountRate,
            feesOutstanding = await fees.OutstandingAsync(orgId, ct),
        });
    }

    private static async Task<IResult> FactorAsync(IRequestContext request, IOrganizationQuery organizations, IMarketplaceStats marketplace,
        IDealStats deals, IFeeStats fees, CancellationToken ct)
    {
        var orgId = request.OrganizationId!.Value;
        if (await organizations.GetAsync(orgId, ct) is not { Type: OrganizationType.Factor })
        {
            return NotFound();
        }

        var offers = await marketplace.FactorOffersAsync(orgId, ct);
        var dealStats = await deals.ForOrganizationAsync(orgId, ct);
        return Results.Ok(new
        {
            activeOffers = offers.Active,
            winRate = offers.Total == 0 ? (decimal?)null : Math.Round((decimal)offers.Accepted / offers.Total, 4, MidpointRounding.AwayFromZero),
            dealsNeedingAction = dealStats.Open,
            portfolio = dealStats.Portfolio,
            overdueOutcomes = dealStats.OverdueOutcomes,
            fundedThisMonth = dealStats.FundedThisMonth,
            feesOutstanding = await fees.OutstandingAsync(orgId, ct),
        });
    }

    private static async Task<IResult> AdminAsync(IComplianceStats compliance, IOrganizationStats organizations, IReceivableStats receivables,
        IMarketplaceStats marketplace, IDealStats deals, INotificationStats notifications, OutboxMonitor outbox, JobRunner jobs, TimeProvider timeProvider, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var complianceQueues = await compliance.QueuesAsync(ct);
        var orgQueues = await organizations.QueuesAsync(ct);
        var dealStats = await deals.PlatformAsync(StuckDays, ct);
        var funnel = await marketplace.FunnelAsync(monthStart, ct);
        var byState = await receivables.CountByStateAsync(null, ct);
        return Results.Ok(new
        {
            queues = new
            {
                kyb = complianceQueues.KybInReview,
                factorAuthorizations = complianceQueues.AuthorizationsPending,
                receivableReview = await receivables.ReviewQueueAsync(ct),
                bankAccounts = orgQueues.BankAccountsPending,
                pristupnice = orgQueues.PristupnicePending,
                mfaResets = orgQueues.MfaResetsOpen,
                activeHolds = complianceQueues.ActiveHolds,
            },
            stuckDeals = dealStats.Stuck,
            month = new { funded = dealStats.FundedThisMonth, gmv = dealStats.Gmv, platformFees = dealStats.PlatformFees },
            funnel = new
            {
                imported = await receivables.ImportedSinceAsync(monthStart, ct),
                verifiedNow = byState.GetValueOrDefault(ReceivableState.Verified),
                listed = funnel.Listed,
                withOffers = funnel.WithOffers,
                accepted = funnel.Accepted,
                funded = dealStats.FundedThisMonth,
            },
            integrationHealth = new
            {
                invalidSefConnections = orgQueues.InvalidSefConnections,
                outbox = await outbox.HealthAsync(ct),
                failedEmails24h = await notifications.FailedEmailsSinceAsync(now.AddDays(-1), ct),
                jobs = await jobs.StatusAsync(ct),
            },
        });
    }

    private static readonly string[] AdminDashboardRoles =
        [PlatformRoles.PlatformAdmin, PlatformRoles.ComplianceOfficer, PlatformRoles.Operations, PlatformRoles.Finance];

    /// <summary>The operating staff roles; anyone else, Auditors included (audit log only), gets 404 (security.md §3).</summary>
    private static async ValueTask<object?> RequireStaff(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        if (Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId))
        {
            foreach (var role in AdminDashboardRoles)
            {
                if (await roles.HasRoleAsync(userId, role, http.RequestAborted))
                {
                    return await next(context);
                }
            }
        }

        return NotFound();
    }

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}
