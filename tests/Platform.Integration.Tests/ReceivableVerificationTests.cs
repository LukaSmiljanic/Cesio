using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Integrations.Sef;
using Microsoft.Extensions.DependencyInjection;
using Modules.Identity;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 011: declarations, documents, submission, the check pipeline and the compliance review queue.</summary>
public sealed class ReceivableVerificationTests(SqlServerContainer server, TestStorage storage) : IAsyncLifetime
{
    private const string BuyerPib = "106006802";

    private static readonly Dictionary<string, bool> AllYes = new()
    {
        ["NoPriorAssignment"] = true,
        ["NoAssignmentProhibition"] = true,
        ["NoSetOffOrDispute"] = true,
        ["GoodsOrServicesDelivered"] = true,
    };

    private readonly TestDatabase _database = new(server);
    private ApiApp _app = null!;

    private FakeSefClient Sef => _app.Services.GetRequiredService<FakeSefClient>();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database, new Dictionary<string, string?>
        {
            ["Storage:Root"] = storage.Root,
            ["Documents:Scanner"] = "Fake",
            ["Sef:Client"] = "Fake",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_submitted_receivable_goes_through_every_check_to_the_queue_and_is_verified_by_compliance()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller);

        var submitted = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"));
        var checks = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable.Id}/checks"))).EnumerateArray().ToList();
        using var officer = await ComplianceOfficerAsync();
        var queue = await ApiClient.ReadAsync(await officer.GetAsync("/api/v1/admin/receivables/review-queue"));
        await officer.StepUpAsync();
        var decided = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Verified", comment = "Documents checked." }));

        Assert.Equal("PendingVerification", submitted.GetProperty("state").GetString());
        Assert.True(submitted.GetProperty("reviewRequired").GetBoolean());
        Assert.Equal(13, checks.Count);
        Assert.DoesNotContain(checks, c => c.GetProperty("result").GetString() == "Fail");
        Assert.Contains(checks, c => c.GetProperty("code").GetString() == "DEBTOR_SCREENING" && c.GetProperty("result").GetString() == "Warn");
        Assert.Contains(queue.EnumerateArray(), q => q.GetProperty("id").GetGuid() == receivable.Id);
        Assert.Equal("Verified", decided.GetProperty("state").GetString());
        foreach (var action in new[] { "Receivable.DeclarationSaved", "Receivable.DocumentAttached", "Receivable.Submitted", "Receivable.SentToManualReview", "Receivable.Verified" })
        {
            Assert.True(await AuditCountAsync(receivable.Id, action) >= 1, $"Missing {action}.");
        }

        // Two runs: at submission and again at the manual decision.
        Assert.Equal(26, await AuditCountAsync(receivable.Id, "Receivable.CheckRecorded"));    }

    [Fact]
    public async Task A_failing_check_rejects_the_receivable_with_its_code()
    {
        var seller = await ApprovedSellerAsync(approve: false);
        var receivable = await ReadyDraftAsync(seller);

        var submitted = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"));

        Assert.Equal("Rejected", submitted.GetProperty("state").GetString());
        Assert.Contains("SELLER_APPROVED", submitted.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_or_storno_invoice_in_sef_is_rejected()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller);
        Sef.SetStatus(seller.Key, receivable.SefId, "Storno");

        var submitted = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"));

        Assert.Equal("Rejected", submitted.GetProperty("state").GetString());
        Assert.Contains("SEF_FRESH_APPROVED", submitted.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_amount_below_the_minimum_is_rejected()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller, net: 1_000m, vat: 200m);

        var submitted = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"));

        Assert.Contains("AMOUNT_LIMITS", submitted.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submission_needs_a_full_declaration_and_a_clean_document()
    {
        var seller = await ApprovedSellerAsync();
        var noDeclaration = await ImportedDraftAsync(seller);
        var withNo = await ImportedDraftAsync(seller);
        var noDocument = await ImportedDraftAsync(seller);
        await DeclareAsync(seller, withNo.Id, new Dictionary<string, bool>(AllYes) { ["NoSetOffOrDispute"] = false });
        await AttachAsync(seller, withNo.Id);
        await DeclareAsync(seller, noDocument.Id, AllYes);

        var missingDeclaration = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{noDeclaration.Id}/submit"));
        var refused = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{withNo.Id}/submit"));
        var missingDocument = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{noDocument.Id}/submit"));

        Assert.Equal("declaration_incomplete", missingDeclaration);
        Assert.Equal("declaration_incomplete", refused);
        Assert.Equal("documents_missing", missingDocument);
    }

    [Fact]
    public async Task Submitting_twice_is_a_conflict_and_only_drafts_or_verified_can_be_cancelled()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller);
        var draft = await ImportedDraftAsync(seller);
        using (var first = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"))
        {
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        }

        using var second = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit");
        using var cancelPending = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/cancel");
        var cancelled = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{draft.Id}/cancel"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelPending.StatusCode);
        Assert.Equal("Cancelled", cancelled.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Compliance_cannot_verify_when_sef_no_longer_shows_the_approval()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller);
        using (var submit = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"))
        {
            Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        }

        Sef.SetStatus(seller.Key, receivable.SefId, "Cancelled");
        using var officer = await ComplianceOfficerAsync();
        await officer.StepUpAsync();

        var code = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Verified" }));

        Assert.Equal("sef_not_approved", code);
    }

    [Fact]
    public async Task A_fraud_rejection_blocks_re_import_while_another_rejection_allows_a_resubmission()
    {
        var seller = await ApprovedSellerAsync();
        var fraud = await ReadyDraftAsync(seller);
        var mistake = await ReadyDraftAsync(seller);
        using var officer = await ComplianceOfficerAsync();
        await officer.StepUpAsync();
        // Any spelling of Fraud blocks the re-import.
        foreach (var (receivable, reason) in new[] { (fraud, "fraud"), (mistake, "WrongDocuments") })
        {
            using (var submit = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"))
            {
                Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
            }

            using var decision = await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Rejected", reasonCode = reason });
            Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        }

        var blocked = await ImportAsync(seller, fraud.SefId);
        var resubmitted = await ImportAsync(seller, mistake.SefId);

        Assert.Equal("fraud_rejected", blocked.GetProperty("reason").GetString());
        Assert.Equal("Imported", resubmitted.GetProperty("status").GetString());
        Assert.NotEqual(mistake.Id, resubmitted.GetProperty("receivableId").GetGuid());
    }

    [Fact]
    public async Task The_review_queue_is_only_for_compliance_staff_and_another_seller_cannot_act_on_a_receivable()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller);
        var other = await ApprovedSellerAsync();

        var othersDraft = await ImportedDraftAsync(other);
        var othersDocument = await UploadAsync(other);

        using var queue = await other.Client.GetAsync("/api/v1/admin/receivables/review-queue");
        using var detail = await other.Client.GetAsync($"/api/v1/admin/receivables/{receivable.Id}");
        using var decision = await other.Client.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Verified" });
        using var submit = await Send(other, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit");
        using var declare = await Send(other, HttpMethod.Put, $"/api/v1/receivables/{receivable.Id}/declaration", new { answers = AllYes });
        using var cancel = await Send(other, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/cancel");
        using var checks = await Send(other, HttpMethod.Get, $"/api/v1/receivables/{receivable.Id}/checks");
        using var get = await Send(other, HttpMethod.Get, $"/api/v1/receivables/{receivable.Id}");
        using var attachToForeign = await Send(other, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/documents", new { documentId = othersDocument });
        using var attachForeignDocument = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/documents", new { documentId = othersDocument });
        using var foreignDraft = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{othersDraft.Id}/submit");

        Assert.All(
            [queue, detail, decision, submit, declare, cancel, checks, get, attachToForeign, attachForeignDocument, foreignDraft],
            r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    [Fact]
    public async Task A_decision_needs_step_up_and_a_reason_code_of_letters()
    {
        var seller = await ApprovedSellerAsync();
        var receivable = await ReadyDraftAsync(seller);
        using (var submit = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable.Id}/submit"))
        {
            Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        }

        using var officer = await ComplianceOfficerAsync();
        var withoutStepUp = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Rejected", reasonCode = "Fraud" }));
        await officer.StepUpAsync();
        var badCode = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Rejected", reasonCode = "Fraud!" }));
        var tooLong = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Rejected", reasonCode = "Other", comment = new string('x', 1001) }));
        using var rejected = await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Rejected", reasonCode = "Other" });
        var again = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/receivables/{receivable.Id}/decision", new { decision = "Verified" }));

        Assert.Equal("step_up_required", withoutStepUp);
        Assert.Equal("validation_failed", badCode);
        Assert.Equal("validation_failed", tooLong);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("conflict_state", again);
    }

    [Fact]
    public async Task A_duplicate_fingerprint_of_another_seller_is_shown_to_compliance_but_not_to_the_seller()
    {
        var first = await ApprovedSellerAsync();
        var second = await ApprovedSellerAsync();
        var original = await ReadyDraftAsync(first);
        var copy = await ReadyDraftAsync(second);
        foreach (var (seller, draft) in new[] { (first, original), (second, copy) })
        {
            using var submit = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{draft.Id}/submit");
            Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        }

        var sellerView = (await ApiClient.ReadAsync(await Send(second, HttpMethod.Get, $"/api/v1/receivables/{copy.Id}/checks"))).EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == "DUPLICATE_FINGERPRINT");
        using var officer = await ComplianceOfficerAsync();
        var adminView = (await ApiClient.ReadAsync(await officer.GetAsync($"/api/v1/admin/receivables/{copy.Id}"))).GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == "DUPLICATE_FINGERPRINT");

        Assert.Equal("Review", sellerView.GetProperty("result").GetString());
        Assert.Equal("Warn", adminView.GetProperty("result").GetString());
    }

    private sealed record Seller(ApiClient Client, Guid Org, string Pib, string Key);

    private sealed record Draft(Guid Id, long SefId);

    private async Task<Seller> ApprovedSellerAsync(bool approve = true)
    {
        var client = _app.NewClient();
        await client.SignUpAsync();
        var pib = TestIds.NewPib();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib }))).GetProperty("id").GetGuid();
        if (approve)
        {
            using var approved = await client.PostAsync($"/api/v1/dev/organizations/{org}/approve");
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        var key = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(key, FakeSefClient.Ubl(pib, BuyerPib, $"PROOF-{Guid.NewGuid():N}"[..20], 10m, 2m));
        await client.StepUpAsync();
        using (var connect = await client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/sef-connection", new { apiKey = key }))
        {
            Assert.Equal(HttpStatusCode.OK, connect.StatusCode);
        }

        return new Seller(client, org, pib, key);
    }

    private async Task<ApiClient> ComplianceOfficerAsync()
    {
        var officer = _app.NewClient();
        await officer.SignUpAsync();
        using var grant = await officer.PostAsync("/api/v1/dev/me/platform-roles", new { role = "ComplianceOfficer" });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        return officer;
    }

    private async Task<Draft> ImportedDraftAsync(Seller seller, decimal net = 1_000_000m, decimal vat = 200_000m)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, $"F-{Guid.NewGuid():N}"[..16], net, vat, issueDate: today, dueDate: today.AddDays(60)), "Approved");
        var result = await ImportAsync(seller, id);
        return new Draft(result.GetProperty("receivableId").GetGuid(), id);
    }

    /// <summary>A Draft with the full declaration and a clean supporting document.</summary>
    private async Task<Draft> ReadyDraftAsync(Seller seller, decimal net = 1_000_000m, decimal vat = 200_000m)
    {
        var draft = await ImportedDraftAsync(seller, net, vat);
        await DeclareAsync(seller, draft.Id, AllYes);
        await AttachAsync(seller, draft.Id);
        return draft;
    }

    private static async Task DeclareAsync(Seller seller, Guid id, Dictionary<string, bool> answers)
    {
        using var response = await Send(seller, HttpMethod.Put, $"/api/v1/receivables/{id}/declaration", new { answers });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AttachAsync(Seller seller, Guid id)
    {
        using var attach = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{id}/documents", new { documentId = await UploadAsync(seller) });
        Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);
    }

    private static async Task<Guid> UploadAsync(Seller seller)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nugovor\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "ugovor.pdf");
        form.Add(new StringContent("ReceivableEvidence"), "purpose");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        AddHeaders(upload, seller, await seller.Client.AntiforgeryTokenAsync());
        return (await ApiClient.ReadAsync(await seller.Client.Http.SendAsync(upload, TestContext.Current.CancellationToken))).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ImportAsync(Seller seller, long sefId) =>
        (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, "/api/v1/receivables/import-from-sef", new { sefInvoiceIds = new[] { $"{sefId}" } })))
            .GetProperty("results")[0];

    private static async Task<HttpResponseMessage> Send(Seller seller, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        AddHeaders(request, seller, method == HttpMethod.Get ? null : await seller.Client.AntiforgeryTokenAsync());
        return await seller.Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static void AddHeaders(HttpRequestMessage request, Seller seller, string? antiforgery)
    {
        request.Headers.Add(OrganizationScopedMetadata.Header, seller.Org.ToString());
        if (antiforgery is not null)
        {
            request.Headers.Add(IdentityModule.AntiforgeryHeader, antiforgery);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }
    }

    private Task<int> AuditCountAsync(Guid id, string action) =>
        _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = '{action}'");
}
