using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Integrations.Sef;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Compliance;
using Modules.Identity;
using Modules.Organizations;
using Modules.Organizations.Contracts;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 007a: SEF claim, pristupnica, official-email confirmations and MFA reset.</summary>
public sealed class SellerClaimTests(SqlServerContainer server, AzuriteContainer storage) : IAsyncLifetime
{
    private const string BuyerPib = "106006802";

    private readonly TestDatabase _database = new(server);
    private ApiApp _app = null!;

    private FakeSefClient Sef => _app.Services.GetRequiredService<FakeSefClient>();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database, new Dictionary<string, string?>
        {
            ["ConnectionStrings:Documents"] = storage.ConnectionString,
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
    public async Task Two_drafts_racing_for_one_pib_have_exactly_one_winner_and_an_alert()
    {
        var pib = TestIds.NewPib();
        var first = await DraftAsync(pib);
        var second = await DraftAsync(pib);

        var responses = await Task.WhenAll(ConnectAsync(first, pib), ConnectAsync(second, pib));
        var codes = new List<string?>();
        foreach (var response in responses)
        {
            codes.Add(response.IsSuccessStatusCode ? "ok" : await ApiClient.ErrorCodeAsync(response));
        }

        Assert.Equal(["ok", "pib_already_claimed"], codes.Order());
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM orgs.organizations WHERE pib = '{pib}' AND claimed_at IS NOT NULL"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Compliance.Alert'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM orgs.organizations WHERE pib = '{pib}' AND onboarding_status = 'ClaimClosed'"));
    }

    [Fact]
    public async Task A_claim_closes_other_drafts_and_a_later_valid_key_is_refused_with_an_alert()
    {
        var pib = TestIds.NewPib();
        var owner = await DraftAsync(pib);
        var other = await DraftAsync(pib);
        var typedOnly = await DraftAsync(TestIds.NewPib());

        using (var won = await ConnectAsync(owner, pib))
        {
            Assert.Equal(HttpStatusCode.OK, won.StatusCode);
        }

        var otherStatus = await StatusAsync(other);
        var refused = await ApiClient.ErrorCodeAsync(await ConnectAsync(other, pib));

        Assert.Equal("ClaimClosed", otherStatus);
        Assert.Equal("pib_already_claimed", refused);
        Assert.Equal("Draft", await StatusAsync(typedOnly));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Compliance.Alert' AND entity_id = '{other.Org}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Organization.ClaimClosed'"));
        Assert.Contains("već registrovana", _database.Emails.Sent.Last(m => m.To == other.Email).Body, StringComparison.Ordinal);
        Assert.DoesNotContain(owner.Email, _database.Emails.Sent.Last(m => m.To == other.Email).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unclaimed_drafts_expire_after_the_claim_window()
    {
        var old = await DraftAsync(TestIds.NewPib());
        var fresh = await DraftAsync(TestIds.NewPib());
        var claimedPib = TestIds.NewPib();
        var claimed = await DraftAsync(claimedPib);
        using (var won = await ConnectAsync(claimed, claimedPib))
        {
            Assert.Equal(HttpStatusCode.OK, won.StatusCode);
        }

        await _database.ExecuteAsync($"UPDATE orgs.organizations SET created_at = DATEADD(day, -7, SYSDATETIMEOFFSET()) WHERE id IN ('{old.Org}', '{claimed.Org}')");
        await _database.ExecuteAsync($"UPDATE orgs.organizations SET created_at = DATEADD(hour, -167, SYSDATETIMEOFFSET()) WHERE id = '{fresh.Org}'");

        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var closed = await scope.ServiceProvider.GetRequiredService<DraftExpiry>().ExpireAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, closed);
        Assert.Equal("ClaimClosed", await StatusAsync(old));
        Assert.Equal("Draft", await StatusAsync(fresh));
        Assert.Equal("Draft", await StatusAsync(claimed));
    }

    [Fact]
    public async Task A_scanned_pristupnica_is_accepted_only_after_the_email_confirmation_and_then_makes_the_account_signatory()
    {
        var seller = await ClaimedSellerAsync();
        var pristupnica = await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/pristupnica"));
        var again = await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/pristupnica"));
        var signed = await UploadAsync(seller, "Pristupnica");
        var uploaded = await ApiClient.ReadAsync(await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/pristupnica", new { documentId = signed, method = "Scanned" }));
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        var id = pristupnica.GetProperty("id").GetGuid();
        var representative = await RepresentativeAsync(seller);
        object Accept() => new { decision = "Accepted", signerName = "Ana Anić", matchedRepresentativeId = representative, notes = "Potpis odgovara APR-u" };

        var tooEarly = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/pristupnice/{id}/decision", Accept()));
        var notSignatory = (await ApiClient.ReadAsync(await seller.Client.GetAsync("/api/v1/me/organizations"))).EnumerateArray().Single().GetProperty("isSignatory").GetBoolean();
        var recorded = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/organizations/{seller.Org}/official-email", new { email = seller.OfficialEmail }));
        var token = TokenFrom(seller.OfficialEmail);
        var wrongPurpose = await ApiClient.ErrorCodeAsync(await seller.Client.GetAsync($"/api/v1/confirmations/{token}?purpose=MfaReset"));
        using var anonymous = _app.NewClient();
        var confirmed = await ApiClient.ReadAsync(await anonymous.GetAsync($"/api/v1/confirmations/{token}?purpose=Pristupnica"));
        using var reused = await anonymous.GetAsync($"/api/v1/confirmations/{token}?purpose=Pristupnica");
        var accepted = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/pristupnice/{id}/decision", Accept()));
        var signatory = (await ApiClient.ReadAsync(await seller.Client.GetAsync("/api/v1/me/organizations"))).EnumerateArray().Single().GetProperty("isSignatory").GetBoolean();
        using var download = await officer.GetAsync($"/api/v1/admin/pristupnice/{id}/content?which=signed");

        Assert.Equal(pristupnica.GetProperty("generatedDocumentId").GetGuid(), again.GetProperty("generatedDocumentId").GetGuid());
        Assert.Equal(64, pristupnica.GetProperty("generatedSha256").GetString()!.Length);
        Assert.Equal("Uploaded", uploaded.GetProperty("status").GetString());
        Assert.True(uploaded.GetProperty("officialEmailMissing").GetBoolean());
        Assert.Equal("email_confirmation_missing", tooEarly);
        Assert.False(notSignatory);
        Assert.True(recorded.GetProperty("confirmationSent").GetBoolean());
        Assert.Equal("validation_failed", wrongPurpose);
        Assert.True(confirmed.GetProperty("confirmed").GetBoolean());
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.True(signatory);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        foreach (var action in new[] { "Pristupnica.Generated", "Pristupnica.Uploaded", "Organization.OfficialEmailRecorded", "Confirmation.Sent", "Confirmation.Used", "Pristupnica.EmailConfirmed", "Pristupnica.Accepted", "Member.SignatoryChanged" })
        {
            Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE action = '{action}'"));
        }

        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM orgs.confirmation_tokens WHERE used_at IS NOT NULL AND LEN(token_hash) = 64 AND token_hash <> '{token}'"));
    }

    [Fact]
    public async Task An_expired_or_unknown_confirmation_is_gone()
    {
        var seller = await ClaimedSellerAsync();
        await RecordOfficialEmailAsync(seller);
        await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/pristupnica"));
        var signed = await UploadAsync(seller, "Pristupnica");
        await ApiClient.ReadAsync(await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/pristupnica", new { documentId = signed, method = "Scanned" }));
        var token = TokenFrom(seller.OfficialEmail);
        await _database.ExecuteAsync("UPDATE orgs.confirmation_tokens SET expires_at = DATEADD(second, -1, SYSDATETIMEOFFSET())");
        using var anonymous = _app.NewClient();

        using var expired = await anonymous.GetAsync($"/api/v1/confirmations/{token}?purpose=Pristupnica");
        using var unknown = await anonymous.GetAsync($"/api/v1/confirmations/not-a-token?purpose=Pristupnica");

        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, unknown.StatusCode);
        Assert.Equal("Uploaded", await _database.ScalarAsync<string>($"SELECT status FROM orgs.pristupnice WHERE org_id = '{seller.Org}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Confirmation.Expired'"));
    }

    [Fact]
    public async Task A_seller_is_approved_only_with_a_claim_and_an_accepted_pristupnica_and_the_official_email_is_told()
    {
        var seller = await ClaimedSellerAsync();
        var representative = await RepresentativeAsync(seller);
        await SubmitAsync(seller);
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        var caseId = (await ApiClient.ReadAsync(await officer.GetAsync("/api/v1/admin/kyb-cases"))).EnumerateArray()
            .Single(c => c.GetProperty("organizationId").GetGuid() == seller.Org).GetProperty("id").GetGuid();
        using (var rated = await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/risk", new { rating = "Low", reason = "Domaća firma" }))
        {
            Assert.Equal(HttpStatusCode.OK, rated.StatusCode);
        }

        object Approve() => new { decision = "Approved", reason = "Sve provereno", representativesVerified = true };
        var blocked = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", Approve()));
        var id = (await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/pristupnica"))).GetProperty("id").GetGuid();
        var signed = await UploadAsync(seller, "Pristupnica");
        await ApiClient.ReadAsync(await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/pristupnica", new { documentId = signed, method = "Qes" }));
        await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/organizations/{seller.Org}/official-email", new { email = seller.OfficialEmail }));
        await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/pristupnice/{id}/decision",
            new { decision = "Accepted", signerName = "Ana Anić", matchedRepresentativeId = representative, notes = "QES proveren ručno" }));
        var approved = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", Approve()));
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var notice = ActivatorUtilities.CreateInstance<ApprovalNotice>(scope.ServiceProvider);
            await notice.HandleAsync(new OrganizationApproved(seller.Org, OrganizationType.Seller), Guid.NewGuid(), TestContext.Current.CancellationToken);
        }

        Assert.Equal("onboarding_incomplete", blocked);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Contains(_database.Emails.Sent, m => m.To == seller.OfficialEmail && m.Subject.Contains("registrovana", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_sef_key_change_waits_for_the_official_email()
    {
        var seller = await ClaimedSellerAsync();
        var newKey = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(newKey, FakeSefClient.Ubl(seller.Pib, BuyerPib, $"N-{Guid.NewGuid():N}"[..20], 10m, 2m));

        var missing = await ApiClient.ErrorCodeAsync(await seller.Client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{seller.Org}/sef-connection", new { apiKey = newKey }));
        await RecordOfficialEmailAsync(seller);
        using var pending = await seller.Client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{seller.Org}/sef-connection", new { apiKey = newKey });
        var rotatedBefore = await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{seller.Org}' AND action = 'Organization.SefKeyRotated'");
        using var anonymous = _app.NewClient();
        var confirmed = await ApiClient.ReadAsync(await anonymous.GetAsync($"/api/v1/confirmations/{TokenFrom(seller.OfficialEmail)}?purpose=SefKeyChange"));
        await using var scope = _app.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<Shared.Infrastructure.Secrets.ISecretStore>()
            .GetAsync(SefConnectionService.SecretName(seller.Org), TestContext.Current.CancellationToken);

        Assert.Equal("official_email_missing", missing);
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        Assert.Equal(0, rotatedBefore);
        Assert.True(confirmed.GetProperty("confirmed").GetBoolean());
        Assert.Equal(newKey, stored);
    }

    [Fact]
    public async Task An_mfa_reset_needs_the_official_email_and_two_officers_and_then_blocks_step_up()
    {
        var seller = await ClaimedSellerAsync();
        await RecordOfficialEmailAsync(seller);
        using var locked = _app.NewClient();
        using (var login = await locked.PostAsync("/api/v1/auth/login", new { email = seller.Email, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var requested = await ApiClient.ReadAsync(await locked.PostAsync("/api/v1/auth/mfa-reset-requests", new { organizationId = seller.Org }));
        var id = requested.GetProperty("id").GetGuid();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        using var second = await StaffAsync("ComplianceOfficer");
        await second.StepUpAsync();
        using var operations = await StaffAsync("Operations");
        await operations.StepUpAsync();

        var notApproved = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/mfa-resets/{id}/execute"));
        using var anonymous = _app.NewClient();
        await ApiClient.ReadAsync(await anonymous.GetAsync($"/api/v1/confirmations/{TokenFrom(seller.OfficialEmail)}?purpose=MfaReset"));
        using var support = await operations.PostAsync($"/api/v1/admin/mfa-resets/{id}/execute");
        var first = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/mfa-resets/{id}/execute"));
        var sameOfficer = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/mfa-resets/{id}/execute"));
        var executed = await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/mfa-resets/{id}/execute"));
        using var oldSession = await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}");

        using var relogged = _app.NewClient();
        using (var login = await relogged.PostAsync("/api/v1/auth/login", new { email = seller.Email, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var start = await ApiClient.ReadAsync(await relogged.PostAsync("/api/v1/auth/mfa/enrol/start"));
        var key = start.GetProperty("sharedKey").GetString()!;
        await ApiClient.ReadAsync(await relogged.PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code = Totp.Code(key) }));
        using (var stepUp = await relogged.PostAsync("/api/v1/auth/step-up", new { code = Totp.Code(key, 1) }))
        {
            Assert.True(stepUp.IsSuccessStatusCode || stepUp.StatusCode == HttpStatusCode.Unauthorized);
        }

        var blocked = await ApiClient.ErrorCodeAsync(await relogged.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{seller.Org}/sef-connection", new { apiKey = "x" }));

        Assert.Equal("Email", requested.GetProperty("approvalKind").GetString());
        Assert.Equal("conflict_state", notApproved);
        Assert.Equal(HttpStatusCode.NotFound, support.StatusCode);
        Assert.Equal("FirstExecution", first.GetProperty("status").GetString());
        Assert.Equal("four_eyes_required", sameOfficer);
        Assert.Equal("Executed", executed.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal("step_up_required", blocked);
        Assert.Contains(_database.Emails.Sent, m => m.To == seller.OfficialEmail && m.Subject.Contains("resetovana", StringComparison.Ordinal));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'User.MfaReset'"));
    }

    [Fact]
    public async Task A_factor_owner_approves_another_owners_mfa_reset()
    {
        var (factorOwner, factorOrg) = await FactorAsync();
        using var colleague = _app.NewClient();
        var (colleagueEmail, _) = await colleague.SignUpAsync();
        await factorOwner.StepUpAsync();
        await ApiClient.ReadAsync(await factorOwner.PostAsync($"/api/v1/organizations/{factorOrg}/members", new { email = colleagueEmail }));
        await ApiClient.ReadAsync(await colleague.PostAsync("/api/v1/invitations/accept", new { token = _database.Emails.LinkValue(colleagueEmail, "token") }));
        using var locked = _app.NewClient();
        using (var login = await locked.PostAsync("/api/v1/auth/login", new { email = colleagueEmail, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var requested = await ApiClient.ReadAsync(await locked.PostAsync("/api/v1/auth/mfa-reset-requests", new { organizationId = factorOrg }));
        var id = requested.GetProperty("id").GetGuid();
        var waiting = await ApiClient.ReadAsync(await factorOwner.GetAsync($"/api/v1/organizations/{factorOrg}/mfa-reset-requests"));
        var self = await ApiClient.ErrorCodeAsync(await colleague.PostAsync($"/api/v1/organizations/{factorOrg}/mfa-reset-requests/{id}/approve"));
        var approved = await ApiClient.ReadAsync(await factorOwner.PostAsync($"/api/v1/organizations/{factorOrg}/mfa-reset-requests/{id}/approve"));
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        var executed = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/mfa-resets/{id}/execute"));

        Assert.Equal("Owner", requested.GetProperty("approvalKind").GetString());
        Assert.Single(waiting.GetProperty("items").EnumerateArray());
        Assert.NotNull(self);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("Executed", executed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Other_organizations_and_non_officers_cannot_reach_the_new_endpoints()
    {
        var seller = await ClaimedSellerAsync();
        var id = (await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/pristupnica"))).GetProperty("id").GetGuid();
        var outsider = await DraftAsync(TestIds.NewPib());
        await outsider.Client.StepUpAsync();
        var outsiderDocument = await UploadAsync(outsider, "Pristupnica");

        using var read = await outsider.Client.GetAsync($"/api/v1/organizations/{seller.Org}/pristupnica");
        using var upload = await outsider.Client.PostAsync($"/api/v1/organizations/{seller.Org}/pristupnica", new { documentId = outsiderDocument, method = "Qes" });
        using var resets = await outsider.Client.GetAsync($"/api/v1/organizations/{seller.Org}/mfa-reset-requests");
        using var approve = await outsider.Client.PostAsync($"/api/v1/organizations/{seller.Org}/mfa-reset-requests/{Guid.NewGuid()}/approve");
        using var queue = await outsider.Client.GetAsync("/api/v1/admin/pristupnice");
        using var decide = await outsider.Client.PostAsync($"/api/v1/admin/pristupnice/{id}/decision", new { decision = "Accepted" });
        using var email = await outsider.Client.PostAsync($"/api/v1/admin/organizations/{seller.Org}/official-email", new { email = "x@example.rs" });
        using var execute = await outsider.Client.PostAsync($"/api/v1/admin/mfa-resets/{Guid.NewGuid()}/execute");
        using var foreignReset = await outsider.Client.PostAsync("/api/v1/auth/mfa-reset-requests", new { organizationId = seller.Org });
        var ownUpload = await ApiClient.ErrorCodeAsync(await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/pristupnica", new { documentId = outsiderDocument, method = "Qes" }));

        Assert.All([read, upload, resets, approve, queue, decide, email, execute], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.NotEqual(HttpStatusCode.Accepted, foreignReset.StatusCode);
        Assert.Equal("wrong_document", ownUpload);
    }

    // ----- helpers -----

    private sealed record Seller(ApiClient Client, Guid Org, string Pib, string Email, string OfficialEmail);

    private async Task<Seller> DraftAsync(string pib)
    {
        var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib }))).GetProperty("id").GetGuid();
        return new Seller(client, org, pib, email, $"apr-{Guid.NewGuid():N}@firma.rs");
    }

    private async Task<HttpResponseMessage> ConnectAsync(Seller seller, string pib)
    {
        var key = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(key, FakeSefClient.Ubl(pib, BuyerPib, $"P-{Guid.NewGuid():N}"[..20], 10m, 2m));
        await seller.Client.StepUpAsync();
        return await seller.Client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{seller.Org}/sef-connection", new { apiKey = key });
    }

    private async Task<Seller> ClaimedSellerAsync()
    {
        var seller = await DraftAsync(TestIds.NewPib());
        using (var patched = await seller.Client.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{seller.Org}",
            new { legalName = "Primer d.o.o.", mb = TestIds.NewMb(), legalForm = "d.o.o.", registeredAddress = "Bulevar 1, Beograd" }, ifMatch: 0))
        {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        }

        using (var representative = await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/legal-representatives", new { name = "Ana Anić", role = "Direktor" }))
        {
            Assert.Equal(HttpStatusCode.Created, representative.StatusCode);
        }

        using (var won = await ConnectAsync(seller, seller.Pib))
        {
            Assert.Equal(HttpStatusCode.OK, won.StatusCode);
        }

        return seller;
    }

    private static async Task<Guid> RepresentativeAsync(Seller seller) =>
        (await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/legal-representatives"))).EnumerateArray().First().GetProperty("id").GetGuid();

    private async Task RecordOfficialEmailAsync(Seller seller)
    {
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/organizations/{seller.Org}/official-email", new { email = seller.OfficialEmail }));
    }

    private async Task SubmitAsync(Seller seller)
    {
        var terms = await ApiClient.ReadAsync(await seller.Client.GetAsync("/api/v1/terms/current"));
        using (var accepted = await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/terms-acceptances", new { termsVersion = terms.GetProperty("version").GetString() }))
        {
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using (var submitted = await seller.Client.PostAsync($"/api/v1/organizations/{seller.Org}/submit"))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(
            ct => scope.ServiceProvider.GetRequiredService<KybService>().HandleAsync(new OrganizationSubmitted(seller.Org, OrganizationType.Seller), Guid.NewGuid(), ct),
            TestContext.Current.CancellationToken);
    }

    private async Task<(ApiClient Owner, Guid Org)> FactorAsync()
    {
        var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await owner.PostAsync("/api/v1/dev/factor-organizations", new { pib = TestIds.NewPib(), legalName = "Faktor a.d." }))).GetProperty("id").GetGuid();
        return (owner, org);
    }

    private static async Task<Guid> UploadAsync(Seller seller, string purpose)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes($"%PDF-1.7\npotpisana pristupnica {Guid.NewGuid()}\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "pristupnica.pdf");
        form.Add(new StringContent(purpose), "purpose");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await seller.Client.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add(OrganizationScopedMetadata.Header, seller.Org.ToString());
        return (await ApiClient.ReadAsync(await seller.Client.Http.SendAsync(request, TestContext.Current.CancellationToken))).GetProperty("id").GetGuid();
    }

    private string TokenFrom(string officialEmail)
    {
        var body = _database.Emails.Sent.Last(m => m.To == officialEmail).Body;
        var match = System.Text.RegularExpressions.Regex.Match(body, @"/confirmations/([A-Za-z0-9_-]+)\?");
        Assert.True(match.Success, body);
        return match.Groups[1].Value;
    }

    private async Task<string?> StatusAsync(Seller seller) =>
        await _database.ScalarAsync<string>($"SELECT onboarding_status FROM orgs.organizations WHERE id = '{seller.Org}'");

    private async Task<ApiClient> StaffAsync(string role)
    {
        var staff = _app.NewClient();
        await staff.SignUpAsync();
        using var grant = await staff.PostAsync("/api/v1/dev/me/platform-roles", new { role });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        return staff;
    }
}
