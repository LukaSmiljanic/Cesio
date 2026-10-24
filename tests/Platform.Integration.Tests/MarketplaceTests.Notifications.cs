using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Marketplace.Contracts;
using Modules.Notifications;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 019: the factor appetite chooses who hears about a new listing in the digest.</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task A_new_listing_reaches_the_digest_of_factors_whose_appetite_it_fits()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var interested = await FactorAsync();
        var tooSmall = await FactorAsync();
        var noAppetite = await FactorAsync();
        using (var set = await Send(interested, HttpMethod.Put, $"/api/v1/organizations/{interested.Org}/factor-appetite",
            new { minAmount = new { amount = "1.00", currency = "RSD" }, maxAmount = new { amount = "100000000.00", currency = "RSD" }, minTenorDays = 1, maxTenorDays = 3650, digestEnabled = true }))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        using (var set = await Send(tooSmall, HttpMethod.Put, $"/api/v1/organizations/{tooSmall.Org}/factor-appetite",
            new { maxAmount = new { amount = "1000.00", currency = "RSD" }, digestEnabled = true }))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        var invalid = await ApiClient.ErrorCodeAsync(await Send(interested, HttpMethod.Put, $"/api/v1/organizations/{interested.Org}/factor-appetite",
            new { minAmount = new { amount = "10.00", currency = "RSD" }, maxAmount = new { amount = "1.00", currency = "RSD" }, digestEnabled = true }));
        var sellerAppetite = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Put, $"/api/v1/organizations/{seller.Org}/factor-appetite", new { digestEnabled = true }));
        using var foreign = await Send(tooSmall, HttpMethod.Put, $"/api/v1/organizations/{interested.Org}/factor-appetite", new { digestEnabled = false });
        var listing = await OpenAsync(seller, receivable);

        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var matching = await scope.ServiceProvider.GetRequiredService<IListingMatches>().FactorsMatchingAsync(listing, TestContext.Current.CancellationToken);
        var eventId = Guid.NewGuid();
        var consumer = scope.ServiceProvider.GetRequiredService<NotificationConsumers>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Shared.Infrastructure.Persistence.UnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(ct => consumer.HandleAsync(new ListingOpened(listing, receivable, seller.Org), eventId, ct), TestContext.Current.CancellationToken);

        Assert.Equal([interested.Org], matching);
        Assert.Equal("validation_failed", invalid);
        Assert.Equal("conflict_state", sellerAppetite);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM notifications.notifications WHERE event_id = '{eventId}' AND org_id = '{interested.Org}' AND email_status = 'Digest'"));
        Assert.Equal(0, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM notifications.notifications WHERE event_id = '{eventId}' AND org_id IN ('{tooSmall.Org}', '{noAppetite.Org}')"));
    }
}
