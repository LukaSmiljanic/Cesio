using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Deals;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 015: the factor's CDD gate, the agreement, the terms confirmation and cancellation.</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task The_deal_reaches_awaiting_signatures_only_when_both_confirm_the_same_agreement()
    {
        var (seller, factor, dealId, _) = await AcceptedDealAsync();
        var outsider = await FactorAsync();

        var kyb = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/kyb-package"));
        using var sellerKyb = await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}/kyb-package");
        var early = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/contract", new { documentId = await UploadContractAsync(factor, "v0") }));
        using var sellerCdd = await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/cdd-decision", new { decision = "Approved" });
        var cdd = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/cdd-decision", new { decision = "Approved" }));
        var first = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/contract", new { documentId = await UploadContractAsync(factor, "v1") }));
        using (var confirmFirst = await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/terms-confirmation", new { contractDocumentId = first.GetProperty("contractDocumentId").GetGuid(), confirmed = true }))
        {
            Assert.Equal(HttpStatusCode.OK, confirmFirst.StatusCode);
        }

        var second = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/contract", new { documentId = await UploadContractAsync(factor, "v2") }));
        var secondId = second.GetProperty("contractDocumentId").GetGuid();
        var stale = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/terms-confirmation", new { contractDocumentId = first.GetProperty("contractDocumentId").GetGuid(), confirmed = true }));
        using (var factorConfirms = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/terms-confirmation", new { contractDocumentId = secondId, confirmed = true }))
        {
            Assert.Equal(HttpStatusCode.OK, factorConfirms.StatusCode);
        }

        var afterReplace = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}"));
        using var sellerReadsContract = await Send(seller, HttpMethod.Get, $"/api/v1/documents/{second.GetProperty("documentId").GetGuid()}/content");
        using var outsiderReadsContract = await Send(outsider, HttpMethod.Get, $"/api/v1/documents/{second.GetProperty("documentId").GetGuid()}/content");
        var agreed = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/terms-confirmation", new { contractDocumentId = secondId, confirmed = true }));
        var deal = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}"))).GetProperty("deal");

        Assert.Equal("kyb_consent_missing", kyb);
        Assert.Equal(HttpStatusCode.NotFound, sellerKyb.StatusCode);
        Assert.Equal("conflict_state", early);
        Assert.Equal(HttpStatusCode.NotFound, sellerCdd.StatusCode);
        Assert.Equal("AwaitingContract", cdd.GetProperty("state").GetString());
        Assert.Equal("contract_changed", stale);
        Assert.Equal("AwaitingContract", afterReplace.GetProperty("deal").GetProperty("state").GetString());
        Assert.False(afterReplace.GetProperty("steps").GetProperty("sellerConfirmed").GetBoolean());
        Assert.Equal("Seller", afterReplace.GetProperty("steps").GetProperty("waitingFor").GetString());
        Assert.Equal(HttpStatusCode.OK, sellerReadsContract.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsiderReadsContract.StatusCode);
        Assert.Equal("AwaitingSignatures", agreed.GetProperty("state").GetString());
        Assert.Equal(second.GetProperty("sha256").GetString(), deal.GetProperty("agreedContractSha256").GetString());
        foreach (var (action, count) in new[] { ("Deal.CddApproved", 1), ("Deal.ContractUploaded", 1), ("Deal.ContractReplaced", 1), ("Deal.TermsConfirmed", 3), ("Deal.ContractAgreed", 1), ("Deal.AwaitingSignatures", 1) })
        {
            Assert.Equal(count, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{dealId}' AND action = '{action}' AND actor_user_id IS NOT NULL"));
        }
    }

    [Fact]
    public async Task A_declined_cdd_cancels_the_deal_and_the_seller_may_list_the_receivable_again()
    {
        var (seller, factor, dealId, receivable) = await AcceptedDealAsync();

        var noReason = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/cdd-decision", new { decision = "Declined" }));
        var declined = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/cdd-decision", new { decision = "Declined", reason = "Visok rizik" }));
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();
        var relisted = await OpenAsync(seller, receivable);

        Assert.Equal("validation_failed", noReason);
        Assert.Equal("Cancelled", declined.GetProperty("state").GetString());
        Assert.Equal("Verified", state);
        Assert.NotEqual(Guid.Empty, relisted);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Deals.DealCancelled'"));
    }

    [Fact]
    public async Task Either_party_cancels_before_signing_and_only_signatories_take_the_steps()
    {
        var (seller, factor, dealId, receivable) = await AcceptedDealAsync();
        using (var approved = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/cdd-decision", new { decision = "Approved" }))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        var contract = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/contract", new { documentId = await UploadContractAsync(factor, "v1") }));
        await _database.ExecuteAsync($"UPDATE orgs.memberships SET is_signatory = 0 WHERE org_id = '{factor.Org}'");
        var notSignatory = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/terms-confirmation",
            new { contractDocumentId = contract.GetProperty("contractDocumentId").GetGuid(), confirmed = true }));
        var badReason = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/cancel", new { reasonCode = "Not ok!" }));
        var cancelled = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/cancel", new { reasonCode = "ChangedMind", comment = "Druga ponuda" }));
        var again = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/cancel", new { reasonCode = "ChangedMind" }));
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();

        Assert.Equal("signatory_required", notSignatory);
        Assert.Equal("validation_failed", badReason);
        Assert.Equal("Cancelled", cancelled.GetProperty("state").GetString());
        Assert.Equal("conflict_state", again);
        Assert.Equal("Verified", state);
    }

    [Fact]
    public async Task A_deal_not_signed_by_its_deadline_is_cancelled_by_the_system()
    {
        var (seller, _, dealId, receivable) = await AcceptedDealAsync();
        await using var scope = _app.Services.CreateAsyncScope();
        // The job of task 020 runs as the system; here a request without a user stands in for it.
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var now = ActivatorUtilities.CreateInstance<DealStepService>(scope.ServiceProvider);
        var cancelledNow = await now.CancelIfSignatureDeadlinePassedAsync(dealId, TestContext.Current.CancellationToken);
        var later = ActivatorUtilities.CreateInstance<DealStepService>(scope.ServiceProvider, new ShiftedTime(TimeSpan.FromDays(10)));
        var cancelledLater = await later.CancelIfSignatureDeadlinePassedAsync(dealId, TestContext.Current.CancellationToken);
        var deal = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}"))).GetProperty("deal");
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();
        var timeline = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}/timeline"))).EnumerateArray().ToList();

        Assert.False(cancelledNow);
        Assert.True(cancelledLater);
        Assert.Equal("Cancelled", deal.GetProperty("state").GetString());
        Assert.Equal("Verified", state);
        Assert.Equal("signature_deadline_missed", timeline[^1].GetProperty("reason").GetString());
    }

    /// <summary>A deal in AwaitingFactorCdd; both parties have a recent step-up.</summary>
    private async Task<(Party Seller, Party Factor, Guid DealId, Guid Receivable)> AcceptedDealAsync()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var listing = await OpenAsync(seller, receivable);
        var factor = await FactorAsync();
        var offer = await OfferAsync(factor, listing);
        await seller.Client.StepUpAsync();
        await factor.Client.StepUpAsync();
        using var accepted = await Send(seller, HttpMethod.Post, $"/api/v1/offers/{offer}/accept", Accept);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        return (seller, factor, (await ApiClient.ReadAsync(accepted)).GetProperty("dealId").GetGuid(), receivable);
    }

    private static async Task<Guid> UploadContractAsync(Party factor, string version)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes($"%PDF-1.7\nugovor o faktoringu {version} {Guid.NewGuid()}\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"ugovor-{version}.pdf");
        form.Add(new StringContent("Contract"), "purpose");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        AddHeaders(upload, factor, await factor.Client.AntiforgeryTokenAsync());
        var body = await ApiClient.ReadAsync(await factor.Client.Http.SendAsync(upload, TestContext.Current.CancellationToken));
        return body.GetProperty("id").GetGuid();
    }

    private sealed class ShiftedTime(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + offset;
    }
}
