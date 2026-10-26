using System.Net;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 021: disputes hold the deal, staff resolve them, internal notes stay internal.</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task A_dispute_holds_the_deal_until_staff_resolve_it_and_internal_notes_stay_internal()
    {
        var (seller, factor, dealId, _) = await SignedDealAsync();
        await seller.Client.StepUpAsync();
        await factor.Client.StepUpAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();
        object Funding() => new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-1", payeeBankAccountId = account };

        var opened = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "ContractMismatch", description = "Ugovor nije usaglašen" }));
        var disputeId = opened.GetProperty("dispute").GetProperty("id").GetGuid();
        var blocked = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report", Funding()));
        using var operations = await StaffAsync("Operations");
        await operations.StepUpAsync();
        await ApiClient.ReadAsync(await operations.PostAsync($"/api/v1/admin/disputes/{disputeId}/assign"));
        using (var note = await operations.PostAsync($"/api/v1/admin/disputes/{disputeId}/comments", new { text = "Interna beleška", @internal = true }))
        {
            Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        }

        using (var comment = await Send(seller, HttpMethod.Post, $"/api/v1/disputes/{disputeId}/comments", new { text = "Ugovor je potpisan kako je dogovoreno" }))
        {
            Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        }

        var partyInternal = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/disputes/{disputeId}/comments", new { text = "x", @internal = true }));
        var sellerView = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/disputes/{disputeId}"));
        var staffView = await ApiClient.ReadAsync(await operations.GetAsync($"/api/v1/admin/disputes/{disputeId}"));
        var queue = (await ApiClient.ReadAsync(await operations.GetAsync("/api/v1/admin/disputes"))).EnumerateArray().ToList();
        var notRaiser = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/disputes/{disputeId}/withdraw"));
        var resolved = await ApiClient.ReadAsync(await operations.PostAsync($"/api/v1/admin/disputes/{disputeId}/resolve", new { summary = "Strane su usaglasile ugovor" }));
        var reported = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report", Funding()));

        Assert.Equal("hold_active", blocked);
        Assert.Equal("validation_failed", partyInternal);
        Assert.DoesNotContain("Interna beleška", sellerView.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("Interna beleška", staffView.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(queue, d => d.GetProperty("id").GetGuid() == disputeId);
        Assert.Equal("forbidden", notRaiser);
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal("FundingReported", reported.GetProperty("state").GetString());
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM compliance.holds WHERE subject_id = '{dealId}' AND lifted_at IS NULL"));
    }

    [Fact]
    public async Task A_fee_dispute_does_not_block_the_deal_and_the_raiser_can_withdraw_it()
    {
        var (_, factor, dealId, _) = await SignedDealAsync();
        await factor.Client.StepUpAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();

        var opened = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "FeeDispute", description = "Naknada nije tačna" }));
        var reported = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-2", payeeBankAccountId = account }));
        var withdrawn = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/disputes/{opened.GetProperty("dispute").GetProperty("id").GetGuid()}/withdraw"));
        var invalid = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "Nope", description = "x" }));

        Assert.Equal("FundingReported", reported.GetProperty("state").GetString());
        Assert.Equal("Withdrawn", withdrawn.GetProperty("status").GetString());
        Assert.Equal("validation_failed", invalid);
    }

    [Fact]
    public async Task Withdrawing_a_dispute_never_lifts_a_hold_staff_placed()
    {
        var (_, factor, dealId, _) = await SignedDealAsync();
        await factor.Client.StepUpAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        await ApiClient.ReadAsync(await officer.PostAsync("/api/v1/admin/holds", new { subjectType = "Deal", subjectId = dealId, kind = "Disputed", reason = "Provera" }));

        var opened = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "Other", description = "x" }));
        await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/disputes/{opened.GetProperty("dispute").GetProperty("id").GetGuid()}/withdraw"));
        var blocked = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-4", payeeBankAccountId = account }));

        Assert.Equal("hold_active", blocked);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM compliance.holds WHERE subject_id = '{dealId}' AND lifted_at IS NULL"));
    }

    [Theory]
    [InlineData("ComplianceHold")]
    [InlineData("Disputed")]
    [InlineData("FraudInvestigation")]
    public async Task Every_hold_kind_on_a_deal_blocks_its_next_step(string kind)
    {
        var (_, factor, dealId, _) = await SignedDealAsync();
        await factor.Client.StepUpAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        await ApiClient.ReadAsync(await officer.PostAsync("/api/v1/admin/holds", new { subjectType = "Deal", subjectId = dealId, kind, reason = "Provera" }));

        var blocked = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-3", payeeBankAccountId = account }));

        Assert.Equal("hold_active", blocked);
    }

    [Fact]
    public async Task Disputes_are_only_for_the_parties_and_staff()
    {
        var (_, factor, dealId, _) = await SignedDealAsync();
        var opened = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "Other", description = "Pitanje" }));
        var disputeId = opened.GetProperty("dispute").GetProperty("id").GetGuid();
        var outsider = await SellerAsync();

        using var open = await Send(outsider, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "Other", description = "x" });
        using var list = await Send(outsider, HttpMethod.Get, $"/api/v1/deals/{dealId}/disputes");
        using var read = await Send(outsider, HttpMethod.Get, $"/api/v1/disputes/{disputeId}");
        using var comment = await Send(outsider, HttpMethod.Post, $"/api/v1/disputes/{disputeId}/comments", new { text = "x" });
        using var withdraw = await Send(outsider, HttpMethod.Post, $"/api/v1/disputes/{disputeId}/withdraw");
        using var queue = await outsider.Client.GetAsync("/api/v1/admin/disputes");
        using var resolve = await outsider.Client.PostAsync($"/api/v1/admin/disputes/{disputeId}/resolve", new { summary = "x" });

        Assert.All([open, list, read, comment, withdraw, queue, resolve], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }
}
