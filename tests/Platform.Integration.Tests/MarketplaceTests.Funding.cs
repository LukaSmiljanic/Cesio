using System.Net;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 017: CEF and debtor-notification evidence, funding, confirmation, outcome and termination.</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task A_deal_is_driven_end_to_end_to_closed()
    {
        var (seller, factor, dealId, receivable) = await SignedDealAsync();

        var cefNow = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/cef-registration",
            new { reference = "CEF-1", evidenceDocumentId = await UploadContractAsync(seller, "cef") }));
        using var notified = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/debtor-notification",
            new { channel = "Email", sentAt = DateTimeOffset.UtcNow.AddMinutes(-5), evidenceDocumentId = await UploadContractAsync(factor, "notice") });
        var early = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/outcome", new { type = "Settled", date = Today }));
        using var sellerSeesAccounts = await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts");
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0];
        var key = Guid.NewGuid().ToString();
        var funding = new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-12345", payeeBankAccountId = account.GetProperty("id").GetGuid() };
        var reported = await ApiClient.ReadAsync(await SendWithKey(factor, $"/api/v1/deals/{dealId}/funding-report", funding, key));
        var replay = await ApiClient.ReadAsync(await SendWithKey(factor, $"/api/v1/deals/{dealId}/funding-report", funding, key));
        using var factorConfirms = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-confirmation", new { received = true });
        var funded = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-confirmation", new { received = true }));
        var fundedState = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();
        using var operations = await StaffAsync("Operations");
        await operations.StepUpAsync();
        var terminateFunded = await ApiClient.ErrorCodeAsync(await operations.PostAsync($"/api/v1/admin/deals/{dealId}/terminate",
            new { reason = "x", evidenceDocumentIds = new[] { Guid.NewGuid() }, sellerConfirmationDocumentId = Guid.NewGuid(), factorConfirmationDocumentId = Guid.NewGuid() }));
        var closed = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/outcome",
            new { type = "Settled", date = Today, amountCollected = new { amount = "1200000.00", currency = "RSD" } }));
        var again = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/outcome", new { type = "Settled", date = Today }));
        var finalState = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();

        Assert.Equal("conflict_state", cefNow);
        Assert.Equal(HttpStatusCode.OK, notified.StatusCode);
        Assert.Equal("conflict_state", early);
        Assert.Equal(HttpStatusCode.NotFound, sellerSeesAccounts.StatusCode);
        Assert.Equal(18, account.GetProperty("accountNumber").GetString()!.Length);
        Assert.Equal("FundingReported", reported.GetProperty("state").GetString());
        Assert.True(reported.GetProperty("amountMatches").GetBoolean());
        Assert.Equal("FundingReported", replay.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NotFound, factorConfirms.StatusCode);
        Assert.Equal("Funded", funded.GetProperty("state").GetString());
        Assert.Equal("Funded", fundedState);
        Assert.Equal("conflict_state", terminateFunded);
        Assert.Equal("Closed", closed.GetProperty("state").GetString());
        Assert.Equal("conflict_state", again);
        Assert.Equal("Settled", finalState);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM deals.cef_registrations WHERE deal_id = '{dealId}' AND status = 'NotApplicable'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM deals.funding_reports WHERE deal_id = '{dealId}'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.access_log WHERE entity_id = '{dealId}' AND action = 'Deal.PayoutAccountViewed'"));
        foreach (var type in new[] { "Deals.DealFundingReported", "Deals.DealFunded", "Deals.DealClosed" })
        {
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM platform.outbox WHERE type = '{type}'"));
        }

        foreach (var action in new[] { "Deal.CefRegistrationCreated", "Deal.DebtorNotified", "Deal.FundingReported", "Deal.FundingConfirmed", "Deal.OutcomeRecorded", "Deal.Closed" })
        {
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{dealId}' AND action = '{action}'"));
        }
    }

    [Fact]
    public async Task A_different_amount_or_a_denied_receipt_opens_a_dispute_that_holds_the_deal()
    {
        var (seller, factor, dealId, _) = await SignedDealAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();
        var wrongAccount = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "1", payeeBankAccountId = Guid.NewGuid() }));
        var future = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today.AddDays(10), paymentReference = "1", payeeBankAccountId = account }));

        var reported = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1100000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-1", payeeBankAccountId = account }));
        var held = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-confirmation", new { received = true }));

        Assert.Equal("account_not_usable", wrongAccount);
        Assert.Equal("validation_failed", future);
        Assert.False(reported.GetProperty("amountMatches").GetBoolean());
        Assert.Equal("hold_active", held);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM deals.disputes WHERE deal_id = '{dealId}' AND kind = 'FundingDiscrepancy' AND status = 'Open'"));

        var (otherSeller, otherFactor, otherDeal, _) = await SignedDealAsync();
        var otherAccount = (await ApiClient.ReadAsync(await Send(otherFactor, HttpMethod.Get, $"/api/v1/deals/{otherDeal}/funding-accounts")))[0].GetProperty("id").GetGuid();
        using (var ok = await Send(otherFactor, HttpMethod.Post, $"/api/v1/deals/{otherDeal}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-2", payeeBankAccountId = otherAccount }))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var noReason = await ApiClient.ErrorCodeAsync(await Send(otherSeller, HttpMethod.Post, $"/api/v1/deals/{otherDeal}/funding-confirmation", new { received = false }));
        var denied = await ApiClient.ReadAsync(await Send(otherSeller, HttpMethod.Post, $"/api/v1/deals/{otherDeal}/funding-confirmation", new { received = false, reason = "Uplata nije stigla" }));
        var heldAgain = await ApiClient.ErrorCodeAsync(await Send(otherSeller, HttpMethod.Post, $"/api/v1/deals/{otherDeal}/funding-confirmation", new { received = true }));

        Assert.Equal("validation_failed", noReason);
        Assert.Equal("FundingReported", denied.GetProperty("state").GetString());
        Assert.Equal("hold_active", heldAgain);
    }

    [Fact]
    public async Task Staff_terminate_a_signed_deal_with_evidence_and_the_receivable_returns_to_verified()
    {
        var (seller, factor, dealId, receivable) = await SignedDealAsync();
        using var operations = await StaffAsync("Operations");
        await operations.StepUpAsync();
        var evidence = await UploadContractAsync(seller, "termination-evidence");
        var sellerConfirmation = await UploadContractAsync(seller, "seller-confirmation");
        var factorConfirmation = await UploadContractAsync(factor, "factor-confirmation");

        using var byParty = await seller.Client.PostAsync($"/api/v1/admin/deals/{dealId}/terminate",
            new { reason = "Sporazumni raskid", evidenceDocumentIds = new[] { evidence }, sellerConfirmationDocumentId = sellerConfirmation, factorConfirmationDocumentId = factorConfirmation });
        var swapped = await ApiClient.ErrorCodeAsync(await operations.PostAsync($"/api/v1/admin/deals/{dealId}/terminate",
            new { reason = "Sporazumni raskid", evidenceDocumentIds = new[] { evidence }, sellerConfirmationDocumentId = factorConfirmation, factorConfirmationDocumentId = sellerConfirmation }));
        var terminated = await ApiClient.ReadAsync(await operations.PostAsync($"/api/v1/admin/deals/{dealId}/terminate",
            new { reason = "Sporazumni raskid", evidenceDocumentIds = new[] { evidence }, sellerConfirmationDocumentId = sellerConfirmation, factorConfirmationDocumentId = factorConfirmation }));
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();

        Assert.Equal(HttpStatusCode.NotFound, byParty.StatusCode);
        Assert.Equal("evidence_missing", swapped);
        Assert.Equal("Terminated", terminated.GetProperty("state").GetString());
        Assert.Equal("Verified", state);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Deals.DealTerminated'"));
    }

    [Fact]
    public async Task Once_the_cef_is_operational_registration_is_pending_and_can_block_funding()
    {
        await _database.ExecuteAsync(
            "INSERT INTO platform.business_parameters (param_key, version, value, effective_from, created_by) VALUES " +
            "('cef.operationalFrom', 1, '\"2026-01-01T00:00:00Z\"', '2026-01-01', NEWID()), ('cef.blockFundingUntilRegistered', 1, 'true', '2026-01-01', NEWID())");
        var (seller, factor, dealId, _) = await SignedDealAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();
        var funding = new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-3", payeeBankAccountId = account };

        var blocked = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report", funding));
        using var registered = await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/cef-registration",
            new { reference = "CEF-2026-1", evidenceDocumentId = await UploadContractAsync(seller, "cef") });
        using var reported = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report", funding);

        Assert.Equal("cef_not_registered", blocked);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM deals.cef_registrations WHERE deal_id = '{dealId}' AND status = 'Registered' AND reference = 'CEF-2026-1'"));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>A Signed deal; both parties have a recent step-up.</summary>
    private async Task<(Party Seller, Party Factor, Guid DealId, Guid Receivable)> SignedDealAsync()
    {
        var (seller, factor, dealId, receivable, agreed) = await AgreedDealAsync();
        foreach (var party in new[] { seller, factor })
        {
            using var signed = await Send(party, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
                new { documentId = await UploadSignedAsync(party, Signature(RepresentativeOf(party.Pib), agreed)) });
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        }

        return (seller, factor, dealId, receivable);
    }
}
