using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Receivables.Contracts;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

/// <summary>Task 020: the scheduled jobs on real listings, offers and deals; time moves by setting the timestamps back.</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task Expired_listings_and_offers_are_closed_by_their_jobs_and_operations_see_the_runs()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var listing = await OpenAsync(seller, receivable);
        var otherReceivable = await VerifiedAsync(seller);
        var otherListing = await OpenAsync(seller, otherReceivable);
        var factor = await FactorAsync();
        var offer = await OfferAsync(factor, listing);
        var lateOffer = await OfferAsync(factor, otherListing, validUntil: DateTimeOffset.UtcNow.AddSeconds(2));
        using var operations = await StaffAsync("Operations");
        await operations.StepUpAsync();
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var offers = await ApiClient.ReadAsync(await operations.PostAsync("/api/v1/admin/jobs/marketplace-offer-expiry/run"));
        int expiredListings;
        await using (var scope = JobScope())
        {
            // Eleven days later both listings (closing in ten days) are past closesAt.
            expiredListings = await ActivatorUtilities.CreateInstance<Modules.Marketplace.MarketplaceJobs>(scope.ServiceProvider, new ShiftedTime(TimeSpan.FromDays(11)))
                .ExpireListingsAsync(TestContext.Current.CancellationToken);
        }

        var listings = await ApiClient.ReadAsync(await operations.PostAsync("/api/v1/admin/jobs/marketplace-listing-expiry/run"));
        var status = (await ApiClient.ReadAsync(await operations.GetAsync("/api/v1/admin/jobs"))).EnumerateArray().ToList();
        using var unknown = await operations.PostAsync("/api/v1/admin/jobs/nope/run");
        using var customer = await seller.Client.GetAsync("/api/v1/admin/jobs");

        Assert.Equal(2, expiredListings);
        Assert.Equal(0, listings.GetProperty("items").GetInt32());
        Assert.Equal(1, offers.GetProperty("items").GetInt32());
        Assert.Equal("Expired", await _database.ScalarAsync<string>($"SELECT state FROM marketplace.listings WHERE id = '{listing}'"));
        Assert.Equal("Verified", await _database.ScalarAsync<string>($"SELECT state FROM receivables.receivables WHERE id = '{receivable}'"));
        Assert.Equal("Superseded", await _database.ScalarAsync<string>($"SELECT state FROM marketplace.offers WHERE id = '{offer}'"));
        Assert.Equal("Expired", await _database.ScalarAsync<string>($"SELECT state FROM marketplace.offers WHERE id = '{lateOffer}'"));
        Assert.Contains(status, j => j.GetProperty("name").GetString() == "marketplace-listing-expiry" && j.GetProperty("lastStatus").GetString() == "Succeeded");
        Assert.Contains(status, j => j.GetProperty("name").GetString() == "deals-signature-deadline");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, customer.StatusCode);
    }

    [Fact]
    public async Task A_missed_signature_deadline_cancels_the_deal_and_a_storno_after_signing_holds_it_without_rollback()
    {
        var (_, _, dealId, receivable) = await AcceptedDealAsync();
        var (signedSeller, _, signedDeal, signedReceivable) = await SignedDealAsync();

        await using (var scope = JobScope())
        {
            // The deadline (5 business days) has passed ten days later; the deal row itself is immutable.
            var later = ActivatorUtilities.CreateInstance<Modules.Deals.DealJobs>(scope.ServiceProvider,
                ActivatorUtilities.CreateInstance<Modules.Deals.DealStepService>(scope.ServiceProvider, new ShiftedTime(TimeSpan.FromDays(10))), new ShiftedTime(TimeSpan.FromDays(10)));
            Assert.True(await later.SignatureDeadlinesAsync(TestContext.Current.CancellationToken) >= 1);
        }

        await using (var scope = JobScope())
        {
            var consumer = scope.ServiceProvider.GetRequiredService<Modules.Deals.DealJobs>();
            await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteInTransactionAsync(
                ct => consumer.HandleAsync(new ReceivableSefStatusChanged(signedReceivable, signedSeller.Org, "Storno", ReceivableState.Assigned), Guid.NewGuid(), ct),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal("Cancelled", await _database.ScalarAsync<string>($"SELECT state FROM deals.deals WHERE id = '{dealId}'"));
        Assert.Equal("Verified", await _database.ScalarAsync<string>($"SELECT state FROM receivables.receivables WHERE id = '{receivable}'"));
        Assert.Equal("Signed", await _database.ScalarAsync<string>($"SELECT state FROM deals.deals WHERE id = '{signedDeal}'"));
        Assert.Equal("Assigned", await _database.ScalarAsync<string>($"SELECT state FROM receivables.receivables WHERE id = '{signedReceivable}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM compliance.holds WHERE subject_id = '{signedDeal}' AND kind = 'FraudInvestigation' AND lifted_at IS NULL"));
    }

    [Fact]
    public async Task A_receivable_too_close_to_maturity_becomes_ineligible_and_its_listing_is_withdrawn()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var listing = await OpenAsync(seller, receivable);
        var maturity = DateOnly.FromDateTime(await _database.ScalarAsync<DateTime>($"SELECT maturity_date FROM receivables.receivables WHERE id = '{receivable}'"));
        var shift = TimeSpan.FromDays(maturity.DayNumber - Shared.Kernel.BusinessDayCalendar.Today(TimeProvider.System).DayNumber - 3);

        await using (var scope = JobScope())
        {
            var later = ActivatorUtilities.CreateInstance<Modules.Receivables.ReceivableJobs>(scope.ServiceProvider, new ShiftedTime(shift));
            Assert.True(await later.MarkMaturityIneligibleAsync(TestContext.Current.CancellationToken) >= 1);
        }

        await using (var scope = JobScope())
        {
            var consumer = scope.ServiceProvider.GetRequiredService<Modules.Marketplace.MarketplaceJobs>();
            await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteInTransactionAsync(
                ct => consumer.HandleAsync(new ReceivableBecameIneligible(receivable, seller.Org, ReceivableState.Listed, "maturity_too_close"), Guid.NewGuid(), ct),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal("Ineligible", await _database.ScalarAsync<string>($"SELECT state FROM receivables.receivables WHERE id = '{receivable}'"));
        Assert.Equal("Withdrawn", await _database.ScalarAsync<string>($"SELECT state FROM marketplace.listings WHERE id = '{listing}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Receivables.ReceivableBecameIneligible'"));
    }

    [Fact]
    public async Task A_revoked_factor_loses_its_active_offers_and_its_deals_before_signing_are_held()
    {
        var (_, factor, dealId, _) = await AcceptedDealAsync();
        var seller = await SellerAsync();
        var otherListing = await OpenAsync(seller, await VerifiedAsync(seller));
        var offer = await OfferAsync(factor, otherListing);
        var revoked = new Modules.Compliance.Contracts.FactorAuthorizationRevoked(factor.Org, Guid.NewGuid());

        await using (var scope = JobScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(ct => scope.ServiceProvider.GetRequiredService<Modules.Marketplace.MarketplaceJobs>().HandleAsync(revoked, Guid.NewGuid(), ct), TestContext.Current.CancellationToken);
            await unitOfWork.ExecuteInTransactionAsync(ct => scope.ServiceProvider.GetRequiredService<Modules.Deals.DealJobs>().HandleAsync(revoked, Guid.NewGuid(), ct), TestContext.Current.CancellationToken);
        }

        Assert.Equal("Expired", await _database.ScalarAsync<string>($"SELECT state FROM marketplace.offers WHERE id = '{offer}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM compliance.holds WHERE subject_id = '{dealId}' AND kind = 'ComplianceHold' AND lifted_at IS NULL"));
    }

    [Fact]
    public async Task Sef_monitoring_makes_a_withdrawn_listed_invoice_ineligible()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        await OpenAsync(seller, receivable);
        Sef.SetStatus(seller.Key, await SefIdAsync(receivable), "Storno");

        await using (var scope = JobScope())
        {
            await scope.ServiceProvider.GetRequiredService<Modules.Receivables.ReceivableJobs>().MonitorSefAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("Ineligible", await _database.ScalarAsync<string>($"SELECT state FROM receivables.receivables WHERE id = '{receivable}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Receivables.ReceivableBecameIneligible'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Receivables.ReceivableSefStatusChanged'"));
    }

    [Fact]
    public async Task Two_instances_running_one_job_at_once_run_it_once()
    {
        var runner = _app.Services.GetRequiredService<JobRunner>();

        var runs = await Task.WhenAll(
            runner.RunAsync("marketplace-listing-expiry", TestContext.Current.CancellationToken),
            runner.RunAsync("marketplace-listing-expiry", TestContext.Current.CancellationToken));

        Assert.Contains(runs, r => r.Status == "Succeeded");
        Assert.True(runs.Count(r => r.Status == "Skipped") <= 1);
    }

    private AsyncServiceScope JobScope()
    {
        var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return scope;
    }
}
