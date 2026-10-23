using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Deals;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 016: signature validation, Signed and Assigned (the fake validator stands in for PAdES, ADR-011).</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task Valid_signatures_of_both_parties_representatives_sign_the_deal_and_assign_the_receivable()
    {
        var (seller, factor, dealId, receivable, agreed) = await AgreedDealAsync();

        var first = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
            new { documentId = await UploadSignedAsync(seller, Signature(RepresentativeOf(seller.Pib), agreed)) }));
        var second = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
            new { documentId = await UploadSignedAsync(factor, Signature(RepresentativeOf(seller.Pib), agreed), Signature(RepresentativeOf(factor.Pib), agreed)) }));
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();

        Assert.Equal("AwaitingSignatures", first.GetProperty("state").GetString());
        Assert.Equal("Valid", first.GetProperty("signatures")[0].GetProperty("result").GetString());
        Assert.Equal("Seller", first.GetProperty("signatures")[0].GetProperty("party").GetString());
        Assert.Equal("Signed", second.GetProperty("state").GetString());
        Assert.Equal("Assigned", state);
        Assert.Equal(3, await CountAsync($"SELECT COUNT(*) FROM deals.signatures WHERE deal_id = '{dealId}'"));
        Assert.Equal(3, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{dealId}' AND action = 'Deal.SignatureValidated'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{dealId}' AND action = 'Deal.Signed'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{receivable}' AND action = 'Receivable.Assigned'"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Deals.DealSigned'"));
    }

    [Fact]
    public async Task Invalid_signatures_are_stored_as_evidence_and_never_sign_the_deal()
    {
        var (seller, _, dealId, _, agreed) = await AgreedDealAsync();
        var name = RepresentativeOf(seller.Pib);
        var cases = new (string Expected, string Line)[]
        {
            ("signature_invalid", Signature(name, agreed, integrity: false)),
            ("certificate_not_qualified", Signature(name, agreed, qualified: false)),
            ("certificate_untrusted", Signature(name, agreed, chainValid: false)),
            ("certificate_revoked", Signature(name, agreed, revoked: true)),
            ("document_mismatch", Signature(name, new string('a', 64))),
            ("signer_not_authorized", Signature("Neko Treći", agreed)),
        };

        foreach (var (expected, line) in cases)
        {
            var code = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
                new { documentId = await UploadSignedAsync(seller, line) }));
            Assert.Equal(expected, code);
        }

        var unsigned = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
            new { documentId = await UploadSignedAsync(seller) }));
        var unavailable = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
            new { documentId = await UploadSignedAsync(seller, FakeSignatureValidator.UnavailableMarker, Signature(name, agreed)) }));
        var deal = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}"))).GetProperty("deal");

        Assert.Equal("signature_invalid", unsigned);
        Assert.Equal("upstream_unavailable", unavailable);
        Assert.Equal("AwaitingSignatures", deal.GetProperty("state").GetString());
        Assert.Equal(cases.Length, await CountAsync($"SELECT COUNT(*) FROM deals.signatures WHERE deal_id = '{dealId}'"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => _database.ExecuteAsync("DELETE FROM deals.signatures"));
    }

    [Fact]
    public async Task Signing_fails_closed_when_sef_changed_and_after_the_deadline()
    {
        var (seller, factor, dealId, receivable, agreed) = await AgreedDealAsync();
        using (var sellerSigns = await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract",
            new { documentId = await UploadSignedAsync(seller, Signature(RepresentativeOf(seller.Pib), agreed)) }))
        {
            Assert.Equal(HttpStatusCode.OK, sellerSigns.StatusCode);
        }

        await _database.ExecuteAsync($"UPDATE receivables.invoices SET sef_status_checked_at = DATEADD(hour, -2, SYSDATETIMEOFFSET()) WHERE id = (SELECT invoice_id FROM receivables.receivables WHERE id = '{receivable}')");
        Sef.SetStatus(seller.Key, await SefIdAsync(receivable), "Storno");
        var factorDocument = await UploadSignedAsync(factor, Signature(RepresentativeOf(factor.Pib), agreed));
        var sefChanged = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/signed-contract", new { documentId = factorDocument }));

        var userId = await _database.ScalarAsync<Guid>($"SELECT user_id FROM orgs.memberships WHERE org_id = '{factor.Org}'");
        await using var scope = _app.Services.CreateAsyncScope();
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CesioClaims.UserId, userId.ToString())], "test")),
        };
        http.Items[OrganizationScope.OrganizationIdItem] = factor.Org;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        var later = ActivatorUtilities.CreateInstance<DealStepService>(scope.ServiceProvider, new ShiftedTime(TimeSpan.FromDays(10)));
        var afterDeadline = await later.UploadSignedContractAsync(dealId, new ContractRequest(factorDocument), TestContext.Current.CancellationToken);
        await afterDeadline.ExecuteAsync(http);

        Assert.Equal("receivable_ineligible", sefChanged);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM deals.deals WHERE id = '{dealId}' AND state = 'Signed'"));
    }

    [Fact]
    public async Task Platform_admins_maintain_the_trust_store_and_others_cannot_see_it()
    {
        using var admin = await StaffAsync("PlatformAdmin");
        var other = await FactorAsync();
        using var key = ECDsa.Create();
        var certificate = Convert.ToBase64String(new CertificateRequest("CN=Test QTSP Root", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(5)).Export(X509ContentType.Cert));
        await admin.StepUpAsync();

        using var hidden = await other.Client.GetAsync("/api/v1/admin/trusted-certificates");
        var added = await ApiClient.ReadAsync(await admin.PostAsync("/api/v1/admin/trusted-certificates", new { name = "Test QTSP Root", certificate }));
        var duplicate = await ApiClient.ErrorCodeAsync(await admin.PostAsync("/api/v1/admin/trusted-certificates", new { name = "Again", certificate }));
        var invalid = await ApiClient.ErrorCodeAsync(await admin.PostAsync("/api/v1/admin/trusted-certificates", new { name = "Bad", certificate = "not-a-certificate" }));
        using var deactivated = await admin.PostAsync($"/api/v1/admin/trusted-certificates/{added.GetProperty("id").GetGuid()}/deactivate");
        var list = (await ApiClient.ReadAsync(await admin.GetAsync("/api/v1/admin/trusted-certificates"))).EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("already_exists", duplicate);
        Assert.Equal("validation_failed", invalid);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        Assert.False(Assert.Single(list).GetProperty("active").GetBoolean());
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'TrustStore.CertificateAdded'"));
    }

    /// <summary>A deal in AwaitingSignatures with the agreed contract hash; both parties have a recent step-up.</summary>
    private async Task<(Party Seller, Party Factor, Guid DealId, Guid Receivable, string Agreed)> AgreedDealAsync()
    {
        var (seller, factor, dealId, receivable) = await AcceptedDealAsync();
        using (var approved = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/cdd-decision", new { decision = "Approved" }))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        var contract = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/contract", new { documentId = await UploadContractAsync(factor, "v1") }));
        foreach (var party in new[] { seller, factor })
        {
            using var confirmed = await Send(party, HttpMethod.Post, $"/api/v1/deals/{dealId}/terms-confirmation",
                new { contractDocumentId = contract.GetProperty("contractDocumentId").GetGuid(), confirmed = true });
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        }

        return (seller, factor, dealId, receivable, contract.GetProperty("sha256").GetString()!);
    }

    private static string Signature(string signer, string covers, bool integrity = true, bool qualified = true, bool chainValid = true, bool revoked = false) =>
        FakeSignatureValidator.Marker + JsonSerializer.Serialize(new { signer, covers, integrity, qualified, chainValid, revoked, serial = Guid.NewGuid().ToString("N")[..8], signingTime = DateTimeOffset.UtcNow });

    private static async Task<Guid> UploadSignedAsync(Party party, params string[] lines)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"%PDF-1.7\n{string.Join('\n', lines)}\n%{Guid.NewGuid()}\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "potpisan-ugovor.pdf");
        form.Add(new StringContent("Contract"), "purpose");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        AddHeaders(upload, party, await party.Client.AntiforgeryTokenAsync());
        return (await ApiClient.ReadAsync(await party.Client.Http.SendAsync(upload, TestContext.Current.CancellationToken))).GetProperty("id").GetGuid();
    }
}
