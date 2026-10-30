using System.Net;
using Microsoft.Data.SqlClient;
using Modules.Marketplace;
using Platform.Integration.Tests.Infrastructure;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 013: offers on listings.</summary>
public sealed partial class MarketplaceTests
{
    private static object OfferBody(string price = "1150000.00", int fundingDays = 2, DateTimeOffset? validUntil = null, string recourse = "NonRecourse") => new
    {
        purchasePrice = new { amount = price, currency = "RSD" },
        recourseType = recourse,
        fundingDeadlineBusinessDays = fundingDays,
        validUntil = validUntil ?? DateTimeOffset.UtcNow.AddDays(5),
        conditions = "Standardni ugovor v3",
    };

    [Fact]
    public async Task The_feed_shows_the_debtor_the_SEF_status_and_only_the_factors_own_offer()
    {
        var seller = await SellerAsync();
        var factorA = await FactorAsync();
        var factorB = await FactorAsync();
        var first = await OpenAsync(seller, await VerifiedAsync(seller));
        var second = await OpenAsync(seller, await VerifiedAsync(seller));
        var withdrawn = await OpenAsync(seller, await VerifiedAsync(seller));
        Guid hidden;
        using (var opened = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{await VerifiedAsync(seller)}/listings", new
        {
            closesAt = InTenDays,
            audience = new { type = "AllVerifiedFactors" },
            excludedFactorOrgIds = new[] { factorA.Org },
            reservePrice = new { amount = "1111111.00", currency = "RSD" },
        }))
        {
            hidden = (await ApiClient.ReadAsync(opened)).GetProperty("id").GetGuid();
        }

        var offerA = await OfferAsync(factorA, first, "1150000.00");
        await OfferAsync(factorB, first, "1130000.00");
        await OfferAsync(factorB, second, "1120000.00");
        await OfferAsync(factorB, hidden, "1110000.00");
        var withdrawnOffer = await OfferAsync(factorA, withdrawn, "1140000.00");
        using (var pulled = await Send(factorA, HttpMethod.Post, $"/api/v1/offers/{withdrawnOffer}/withdraw"))
        {
            Assert.Equal(HttpStatusCode.OK, pulled.StatusCode);
        }

        var feedA = await FeedAsync(factorA, "");
        var withoutOwnA = await FeedAsync(factorA, "?withoutOwnOffer=true");
        using var rawFeedA = await Send(factorA, HttpMethod.Get, "/api/v1/marketplace/listings");
        var feedBodyA = await rawFeedA.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var feedB = await FeedAsync(factorB, "");
        var detail = await ApiClient.ReadAsync(await Send(factorA, HttpMethod.Get, $"/api/v1/marketplace/listings/{first}"));

        // Factor A sees its own offer and nothing of B's, also where both offered on one listing.
        Assert.Equal(offerA, feedA[first].GetProperty("ownOffer").GetProperty("id").GetGuid());
        Assert.Equal("1150000.00", feedA[first].GetProperty("ownOffer").GetProperty("purchasePrice").GetProperty("amount").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, feedA[second].GetProperty("ownOffer").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, feedA[withdrawn].GetProperty("ownOffer").ValueKind);
        Assert.DoesNotContain("113", feedBodyA, StringComparison.Ordinal);
        Assert.DoesNotContain("112", feedBodyA, StringComparison.Ordinal);
        Assert.DoesNotContain("reserve", feedBodyA, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(detail.GetProperty("debtor").GetProperty("legalName").GetString(), feedA[first].GetProperty("debtorName").GetString());
        Assert.Equal(detail.GetProperty("sef").GetProperty("status").GetString(), feedA[first].GetProperty("sefStatus").GetString());
        // The listing A is excluded from never appears, not even through the summaries read.
        Assert.DoesNotContain(hidden, feedA.Keys);
        Assert.DoesNotContain(hidden, withoutOwnA.Keys);
        Assert.DoesNotContain(hidden.ToString(), feedBodyA, StringComparison.Ordinal);
        // B's offer on the first listing does not hide it from A's "without my offer" view; the withdrawn one does not count.
        Assert.Equal(new[] { second, withdrawn }.Order(), withoutOwnA.Keys.Order());
        Assert.Equal("1130000.00", feedB[first].GetProperty("ownOffer").GetProperty("purchasePrice").GetProperty("amount").GetString());
        Assert.Contains(hidden, feedB.Keys);
    }

    private static async Task<Dictionary<Guid, System.Text.Json.JsonElement>> FeedAsync(Party factor, string query) =>
        (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings{query}"))).GetProperty("items")
            .EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());

    [Fact]
    public async Task A_factor_offers_with_a_fee_snapshot_and_the_seller_compares_offers_it_alone_sees()
    {
        var seller = await SellerAsync();
        var listing = await OpenAsync(seller, await VerifiedAsync(seller));
        var first = await FactorAsync();
        var second = await FactorAsync();

        var preview = await ApiClient.ReadAsync(await Send(first, HttpMethod.Get, $"/api/v1/marketplace/listings/{listing}/fee-preview?purchasePrice=1150000.00"));
        using var submitted = await Send(first, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody());
        var offer = await ApiClient.ReadAsync(submitted);
        var offerId = offer.GetProperty("id").GetGuid();
        using (var other = await Send(second, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody("1140000.00", recourse: "Recourse")))
        {
            Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        }

        var comparison = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/listings/{listing}/offers"))).EnumerateArray().ToList();
        using var othersOffer = await Send(second, HttpMethod.Get, $"/api/v1/offers/{offerId}");
        var secondsOffers = (await ApiClient.ReadAsync(await Send(second, HttpMethod.Get, "/api/v1/offers"))).EnumerateArray().ToList();
        using var othersComparison = await Send(second, HttpMethod.Get, $"/api/v1/listings/{listing}/offers");
        var detail = await ApiClient.ReadAsync(await Send(first, HttpMethod.Get, $"/api/v1/marketplace/listings/{listing}"));
        var sellerReads = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/offers/{offerId}"));

        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        Assert.Equal("7500.00", preview.GetProperty("factorFee").GetProperty("amount").GetString());
        Assert.Equal("7500.00", offer.GetProperty("platformFee").GetProperty("amount").GetString());
        Assert.Equal(1, offer.GetProperty("feeScheduleVersion").GetInt32());
        Assert.Equal(2, comparison.Count);
        Assert.Equal(offerId, comparison[0].GetProperty("id").GetGuid());
        Assert.Equal("1150000.00", comparison[0].GetProperty("metrics").GetProperty("sellerNetProceeds").GetProperty("amount").GetString());
        Assert.Equal("50000.00", comparison[0].GetProperty("metrics").GetProperty("discount").GetProperty("amount").GetString());
        Assert.Equal(4.17m, comparison[0].GetProperty("metrics").GetProperty("discountPercent").GetDecimal());
        Assert.True(comparison[0].GetProperty("metrics").GetProperty("indicativeAnnualCostPercent").GetDecimal() > 0);
        Assert.Equal(HttpStatusCode.NotFound, othersOffer.StatusCode);
        Assert.DoesNotContain(secondsOffers, o => o.GetProperty("id").GetGuid() == offerId);
        Assert.Equal(HttpStatusCode.NotFound, othersComparison.StatusCode);
        Assert.Equal(offerId, detail.GetProperty("ownOffer").GetProperty("id").GetGuid());
        Assert.Equal(offerId, sellerReads.GetProperty("id").GetGuid());
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{offerId}' AND action = 'Offer.Submitted'"));
    }

    [Fact]
    public async Task Offer_validation_rejects_bad_prices_terms_and_invisible_listings()
    {
        var seller = await SellerAsync();
        var listing = await OpenAsync(seller, await VerifiedAsync(seller));
        var factor = await FactorAsync();
        var unapproved = await FactorAsync(approve: false);
        string?[] codes =
        [
            await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody("1200000.00"))),
            await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody("0.00"))),
            await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody(fundingDays: 31))),
            await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody(validUntil: InTenDays.AddDays(1)))),
            await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody(validUntil: DateTimeOffset.UtcNow.AddMinutes(-1)))),
        ];
        using var euro = await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers",
            new { purchasePrice = new { amount = "1000.00", currency = "EUR" }, recourseType = "Recourse", fundingDeadlineBusinessDays = 2, validUntil = DateTimeOffset.UtcNow.AddDays(1) });
        using var invisible = await Send(unapproved, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody());
        using var bySeller = await Send(seller, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody());

        Assert.All(codes, code => Assert.Equal("validation_failed", code));
        Assert.Equal(HttpStatusCode.BadRequest, euro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bySeller.StatusCode);
    }

    [Fact]
    public async Task One_active_offer_per_factor_even_in_parallel_and_withdraw_allows_a_new_one()
    {
        var seller = await SellerAsync();
        var listing = await OpenAsync(seller, await VerifiedAsync(seller));
        var factor = await FactorAsync();

        var responses = await Task.WhenAll(
            Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody()),
            Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody("1140000.00")));
        var created = responses.Single(r => r.StatusCode == HttpStatusCode.Created);
        var duplicate = await ApiClient.ErrorCodeAsync(responses.Single(r => r != created));
        var offerId = (await ApiClient.ReadAsync(created)).GetProperty("id").GetGuid();
        var withdrawn = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/offers/{offerId}/withdraw"));
        var again = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/offers/{offerId}/withdraw"));
        using var replacement = await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody("1140000.00"));

        Assert.Equal("offer_exists", duplicate);
        Assert.Equal("Withdrawn", withdrawn.GetProperty("state").GetString());
        Assert.Equal("offer_not_active", again);
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
    }

    [Fact]
    public async Task The_seller_rejects_an_offer_and_withdrawing_the_listing_supersedes_the_rest()
    {
        var seller = await SellerAsync();
        var listing = await OpenAsync(seller, await VerifiedAsync(seller));
        var first = await FactorAsync();
        var second = await FactorAsync();
        var rejectedId = (await ApiClient.ReadAsync(await Send(first, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody()))).GetProperty("id").GetGuid();
        var activeId = (await ApiClient.ReadAsync(await Send(second, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody()))).GetProperty("id").GetGuid();

        using var factorRejects = await Send(second, HttpMethod.Post, $"/api/v1/offers/{rejectedId}/reject", new { reason = "x" });
        var rejected = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/offers/{rejectedId}/reject", new { reason = "Prenizak iznos" }));
        using (var withdraw = await Send(seller, HttpMethod.Post, $"/api/v1/listings/{listing}/withdraw"))
        {
            Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
        }

        var superseded = await ApiClient.ReadAsync(await Send(second, HttpMethod.Get, $"/api/v1/offers/{activeId}"));
        var stillRejected = await ApiClient.ReadAsync(await Send(first, HttpMethod.Get, $"/api/v1/offers/{rejectedId}"));

        Assert.Equal(HttpStatusCode.NotFound, factorRejects.StatusCode);
        Assert.Equal("Rejected", rejected.GetProperty("state").GetString());
        Assert.Equal("Superseded", superseded.GetProperty("state").GetString());
        Assert.Equal("Rejected", stillRejected.GetProperty("state").GetString());
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{activeId}' AND action = 'Offer.Superseded'"));
    }

    [Fact]
    public async Task Offers_are_immutable_in_the_database()
    {
        var seller = await SellerAsync();
        var listing = await OpenAsync(seller, await VerifiedAsync(seller));
        var factor = await FactorAsync();
        var offerId = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/marketplace/listings/{listing}/offers", OfferBody()))).GetProperty("id").GetGuid();

        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync($"UPDATE marketplace.offers SET purchase_price = 1 WHERE id = '{offerId}'"));
        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync($"UPDATE marketplace.offers SET platform_fee = 0 WHERE id = '{offerId}'"));
        var denied = await _database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals r ON r.principal_id = p.grantee_principal_id " +
            "WHERE r.name = 'app_rw' AND p.state = 'D' AND p.permission_name = 'UPDATE' AND p.major_id = OBJECT_ID('marketplace.offers')");
        Assert.True(denied > 0);
    }

    [Fact]
    public void The_seller_metrics_match_the_financial_model()
    {
        // financial-model.md §2, €100,000 example: t = 90 days. The funding date is 2 business days after a Monday.
        var monday = new DateOnly(2026, 10, 19);
        var maturity = BusinessDays.Add(monday, 2).AddDays(90);
        var factorPays = OfferMetrics.Compute(Money.Parse("100000.00", Currency.Rsd), Money.Parse("97000.00", Currency.Rsd), Money.Zero(Currency.Rsd), maturity, monday, 2);
        var sellerPays = OfferMetrics.Compute(Money.Parse("100000.00", Currency.Rsd), Money.Parse("97000.00", Currency.Rsd), Money.Parse("450.00", Currency.Rsd), maturity, monday, 2);

        Assert.Equal(90, factorPays.TenorDays);
        Assert.Equal(3.00m, factorPays.DiscountPercent);
        Assert.Equal(Money.Parse("97000.00", Currency.Rsd), factorPays.SellerNetProceeds);
        Assert.Equal(12.54m, factorPays.IndicativeAnnualCostPercent);
        Assert.Equal(Money.Parse("96550.00", Currency.Rsd), sellerPays.SellerNetProceeds);
        Assert.Equal(14.49m, sellerPays.IndicativeAnnualCostPercent);
        Assert.Equal(new DateOnly(2026, 10, 26), BusinessDays.Add(new DateOnly(2026, 10, 23), 1));
    }
}
