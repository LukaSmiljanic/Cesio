using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Modules.Identity;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 014: offer acceptance and deal creation.</summary>
public sealed partial class MarketplaceTests
{
    private static readonly object Accept = new { acknowledgements = new { netProceedsShown = true, termsVersion = "2026-10" } };

    [Fact]
    public async Task Accepting_an_offer_closes_the_listing_supersedes_the_rest_reserves_the_receivable_and_creates_the_deal()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var listing = await OpenAsync(seller, receivable);
        var winner = await FactorAsync();
        var loser = await FactorAsync();
        var outsider = await FactorAsync();
        var offerId = await OfferAsync(winner, listing);
        var otherId = await OfferAsync(loser, listing, "1140000.00");
        await seller.Client.StepUpAsync();

        var key = Guid.NewGuid().ToString();
        using var accepted = await SendWithKey(seller, $"/api/v1/offers/{offerId}/accept", Accept, key);
        var deal = await ApiClient.ReadAsync(accepted);
        var replay = await ApiClient.ReadAsync(await SendWithKey(seller, $"/api/v1/offers/{offerId}/accept", Accept, key));
        var dealId = deal.GetProperty("dealId").GetGuid();
        var sellerView = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}"))).GetProperty("deal");
        var factorDeals = (await ApiClient.ReadAsync(await Send(winner, HttpMethod.Get, "/api/v1/deals"))).EnumerateArray().ToList();
        var timeline = (await ApiClient.ReadAsync(await Send(winner, HttpMethod.Get, $"/api/v1/deals/{dealId}/timeline"))).EnumerateArray().ToList();
        using var outsiderView = await Send(outsider, HttpMethod.Get, $"/api/v1/deals/{dealId}");
        using var loserView = await Send(loser, HttpMethod.Get, $"/api/v1/deals/{dealId}/timeline");
        var other = await ApiClient.ReadAsync(await Send(loser, HttpMethod.Get, $"/api/v1/offers/{otherId}"));
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();
        var listingState = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/listings/{listing}"))).GetProperty("state").GetString();

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Matches(@"^DL-\d{4}-\d{6}$", deal.GetProperty("reference").GetString());
        Assert.Equal(dealId, replay.GetProperty("dealId").GetGuid());
        Assert.Equal("AwaitingFactorCdd", sellerView.GetProperty("state").GetString());
        Assert.Equal("1150000.00", sellerView.GetProperty("purchasePrice").GetProperty("amount").GetString());
        Assert.Equal("7500.00", sellerView.GetProperty("platformFee").GetProperty("amount").GetString());
        Assert.Equal("1150000.00", sellerView.GetProperty("sellerNetProceeds").GetProperty("amount").GetString());
        Assert.Contains(factorDeals, d => d.GetProperty("id").GetGuid() == dealId);
        Assert.Single(timeline);
        Assert.Equal(HttpStatusCode.NotFound, outsiderView.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, loserView.StatusCode);
        Assert.Equal("Superseded", other.GetProperty("state").GetString());
        Assert.Equal("Reserved", state);
        Assert.Equal("Closed", listingState);
        foreach (var (entity, action) in new[] { (offerId, "Offer.Accepted"), (otherId, "Offer.Superseded"), (listing, "Listing.Closed"), (receivable, "Receivable.Reserved"), (dealId, "Deal.Created") })
        {
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{entity}' AND action = '{action}'"));
        }

        foreach (var type in new[] { "Marketplace.OfferAccepted", "Deals.DealCreated", "Marketplace.OfferSuperseded" })
        {
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM platform.outbox WHERE type = '{type}'"));
        }
    }

    [Fact]
    public async Task Two_sessions_accepting_different_offers_in_parallel_create_exactly_one_deal()
    {
        var seller = await SellerAsync();
        using var secondSession = _app.NewClient();
        await secondSession.SignInAsync(seller.Email, seller.Client.SharedKey!);
        var second = seller with { Client = secondSession };
        var first = await FactorAsync();
        var other = await FactorAsync();
        await seller.Client.StepUpAsync();
        await secondSession.StepUpAsync();

        for (var i = 0; i < 20; i++)
        {
            var receivable = await VerifiedAsync(seller);
            var listing = await OpenAsync(seller, receivable);
            var a = await OfferAsync(first, listing);
            var b = await OfferAsync(other, listing, "1140000.00");

            var responses = await Task.WhenAll(
                Send(seller, HttpMethod.Post, $"/api/v1/offers/{a}/accept", Accept),
                Send(second, HttpMethod.Post, $"/api/v1/offers/{b}/accept", Accept));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed);
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM deals.deals WHERE receivable_id = '{receivable}'"));
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM marketplace.offers WHERE listing_id = '{listing}' AND state = 'Accepted'"));
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task The_database_refuses_a_second_live_deal_for_a_receivable()
    {
        var seller = await SellerAsync();
        var listing = await OpenAsync(seller, await VerifiedAsync(seller));
        var factor = await FactorAsync();
        var offerId = await OfferAsync(factor, listing);
        var otherOffer = await OfferAsync(await FactorAsync(), listing, "1140000.00");
        await seller.Client.StepUpAsync();
        using (var accepted = await Send(seller, HttpMethod.Post, $"/api/v1/offers/{offerId}/accept", Accept))
        {
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync(
            "INSERT INTO deals.deals (id, reference, receivable_id, listing_id, offer_id, seller_org_id, factor_org_id, purchase_price, recourse_type, " +
            "funding_deadline_bdays, conditions_text, fee_schedule_version, platform_fee, seller_fee, vat_pending, state, signature_deadline, funding_deadline, " +
            "accepted_by, accepted_at, accepted_step_up_at, version, face_value, currency) " +
            $"SELECT NEWID(), 'DL-DUPLICATE', receivable_id, listing_id, '{otherOffer}', seller_org_id, factor_org_id, purchase_price, recourse_type, " +
            "funding_deadline_bdays, conditions_text, fee_schedule_version, platform_fee, seller_fee, vat_pending, state, signature_deadline, funding_deadline, " +
            $"accepted_by, accepted_at, accepted_step_up_at, 0, face_value, currency FROM deals.deals WHERE offer_id = '{offerId}'"));
        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync($"UPDATE deals.deals SET purchase_price = 1 WHERE offer_id = '{offerId}'"));
        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("DELETE FROM deals.deal_state_history"));
    }

    [Fact]
    public async Task Acceptance_refuses_expired_offers_changed_sef_status_non_signatories_missing_step_up_and_other_sellers()
    {
        var seller = await SellerAsync();
        var factor = await FactorAsync();
        var storno = await VerifiedAsync(seller);
        var stornoListing = await OpenAsync(seller, storno);
        var stornoOffer = await OfferAsync(factor, stornoListing);
        var expiringListing = await OpenAsync(seller, await VerifiedAsync(seller));
        var expiring = await OfferAsync(factor, expiringListing, validUntil: DateTimeOffset.UtcNow.AddSeconds(2));
        var plainListing = await OpenAsync(seller, await VerifiedAsync(seller));
        var plain = await OfferAsync(factor, plainListing);
        var other = await SellerAsync();
        using var freshSession = _app.NewClient();
        await freshSession.SignInAsync(seller.Email, seller.Client.SharedKey!);

        var noStepUp = await ApiClient.ErrorCodeAsync(await Send(seller with { Client = freshSession }, HttpMethod.Post, $"/api/v1/offers/{plain}/accept", Accept));
        await seller.Client.StepUpAsync();
        await other.Client.StepUpAsync();
        using var foreign = await Send(other, HttpMethod.Post, $"/api/v1/offers/{plain}/accept", Accept);
        var noAcknowledgement = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/offers/{plain}/accept", new { }));
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var expired = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/offers/{expiring}/accept", Accept));
        await _database.ExecuteAsync($"UPDATE receivables.invoices SET sef_status_checked_at = DATEADD(hour, -2, SYSDATETIMEOFFSET()) WHERE id = (SELECT invoice_id FROM receivables.receivables WHERE id = '{storno}')");
        Sef.SetStatus(seller.Key, await SefIdAsync(storno), "Storno");
        var ineligible = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/offers/{stornoOffer}/accept", Accept));
        var stornoState = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{storno}"))).GetProperty("state").GetString();
        await _database.ExecuteAsync($"UPDATE orgs.memberships SET is_signatory = 0 WHERE org_id = '{seller.Org}'");
        var notSignatory = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/offers/{plain}/accept", Accept));

        Assert.Equal("step_up_required", noStepUp);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("validation_failed", noAcknowledgement);
        Assert.Equal("offer_not_active", expired);
        Assert.Equal("receivable_ineligible", ineligible);
        Assert.Equal("Ineligible", stornoState);
        Assert.Equal("signatory_required", notSignatory);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM deals.deals"));
    }

    private static async Task<Guid> OfferAsync(Party factor, Guid listing, string price = "1150000.00", DateTimeOffset? validUntil = null)
    {
        using var offered = await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody(price, validUntil: validUntil));
        Assert.Equal(HttpStatusCode.Created, offered.StatusCode);
        return (await ApiClient.ReadAsync(offered)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendWithKey(Party party, string path, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(OrganizationScopedMetadata.Header, party.Org.ToString());
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await party.Client.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", key);
        return await party.Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
