using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Compliance;
using Modules.Compliance.Contracts;
using Modules.Identity;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 008: factor authorization submitted by the factor and verified by two compliance officers.</summary>
public sealed class FactorAuthorizationTests(SqlServerContainer server, AzuriteContainer storage) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);
    private ApiApp _app = null!;

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
    public async Task Staff_invite_a_factor_and_two_officers_verify_its_authorization()
    {
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        using var second = await StaffAsync("ComplianceOfficer");
        await second.StepUpAsync();
        var (factor, org) = await InvitedFactorAsync(officer);
        var evidence = await UploadAsync(factor, org);

        var submitted = await ApiClient.ReadAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new
        {
            kind = "FactoringCompany",
            issuingAuthority = "MinistryOfFinance",
            decisionNumber = "Fa-123",
            decisionDate = "2024-05-10",
            evidenceDocumentId = evidence,
            officialSourceUrl = "https://pretraga2.apr.gov.rs/FactoringList",
        }));
        var id = submitted.GetProperty("id").GetGuid();
        var before = await AuthorizedAsync(org);
        var snapshot = await SnapshotAsync(officer, id);
        var first = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/factor-authorizations/{id}/approve",
            new { sourceSnapshotDocumentId = snapshot, verificationMethod = "OfficialRegisterLookup", notes = "Evidentirano odobrenje" }));
        var sameOfficer = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/factor-authorizations/{id}/approve", new { }));
        var pending = await AuthorizedAsync(org);
        var verified = await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/factor-authorizations/{id}/approve", new { }));
        var after = await AuthorizedAsync(org);

        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.False(before);
        Assert.Equal("PendingSecondApproval", first.GetProperty("status").GetString());
        Assert.Equal("four_eyes_required", sameOfficer);
        Assert.False(pending);
        Assert.Equal("Verified", verified.GetProperty("status").GetString());
        Assert.Equal(BusinessDayCalendar.Today(TimeProvider.System).AddDays(90), DateOnly.Parse(verified.GetProperty("reverifyBy").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(after);
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = 'FactorAuthorization.Verified' AND evidence_document_ids IS NOT NULL"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = 'FactorAuthorization.FirstApproval'"));
    }

    [Fact]
    public async Task Kind_authority_and_decision_fields_must_be_consistent()
    {
        var (factor, org) = await DevFactorAsync();
        var evidence = await UploadAsync(factor, org);

        using var bank = await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" });
        var wrongAuthority = await ApiClient.ErrorCodeAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "MinistryOfFinance" }));
        var bankWithDecision = await ApiClient.ErrorCodeAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations",
            new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia", decisionNumber = "1", decisionDate = "2024-01-01" }));
        var companyWithout = await ApiClient.ErrorCodeAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations",
            new { kind = "FactoringCompany", issuingAuthority = "SecuritiesCommission", evidenceDocumentId = evidence }));
        var companyWrongAuthority = await ApiClient.ErrorCodeAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations",
            new { kind = "FactoringCompany", issuingAuthority = "NationalBankOfSerbia", decisionNumber = "Fa-1", decisionDate = "2024-01-01", evidenceDocumentId = evidence }));

        Assert.Equal(HttpStatusCode.Created, bank.StatusCode);
        Assert.Equal("authority_inconsistent", wrongAuthority);
        Assert.Equal("decision_not_applicable", bankWithDecision);
        Assert.Equal("decision_required", companyWithout);
        Assert.Equal("authority_inconsistent", companyWrongAuthority);
    }

    [Fact]
    public async Task A_bank_is_verified_for_30_days_and_the_reverification_date_is_the_last_authorized_day()
    {
        var (org, id) = await VerifiedBankAsync();
        var reverifyBy = await _database.ScalarAsync<DateTime>($"SELECT reverify_by FROM compliance.factor_authorizations WHERE id = '{id}'");
        var today = BusinessDayCalendar.Today(TimeProvider.System);

        await _database.ExecuteAsync($"UPDATE compliance.factor_authorizations SET reverify_by = '{today:yyyy-MM-dd}' WHERE id = '{id}'");
        var onTheDay = await AuthorizedAsync(org);
        await _database.ExecuteAsync($"UPDATE compliance.factor_authorizations SET reverify_by = '{today.AddDays(-1):yyyy-MM-dd}' WHERE id = '{id}'");
        var dayAfter = await AuthorizedAsync(org);
        int expired;
        await using (var scope = Scope())
        {
            expired = await scope.ServiceProvider.GetRequiredService<FactorAuthorizationService>().ExpireDueAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(today.AddDays(30), DateOnly.FromDateTime(reverifyBy));
        Assert.True(onTheDay);
        Assert.False(dayAfter);
        Assert.Equal(1, expired);
        Assert.Equal("Expired", await _database.ScalarAsync<string>($"SELECT status FROM compliance.factor_authorizations WHERE id = '{id}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Compliance.FactorAuthorizationExpired'"));
    }

    [Fact]
    public async Task A_revocation_ends_the_authorization_at_once_and_is_published()
    {
        var (org, id) = await VerifiedBankAsync();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();

        var noReason = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/factor-authorizations/{id}/revoke", new { }));
        var revoked = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/factor-authorizations/{id}/revoke", new { reason = "Uklonjena sa spiska NBS" }));
        var again = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/factor-authorizations/{id}/revoke", new { reason = "ponovo" }));

        Assert.Equal("validation_failed", noReason);
        Assert.Equal("Revoked", revoked.GetProperty("status").GetString());
        Assert.Equal("conflict_state", again);
        Assert.False(await AuthorizedAsync(org));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Compliance.FactorAuthorizationRevoked'"));
    }

    [Fact]
    public async Task A_new_verification_supersedes_the_old_one_and_a_rejection_needs_a_reason()
    {
        var (org, firstId) = await VerifiedBankAsync();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        using var other = await StaffAsync("ComplianceOfficer");
        await other.StepUpAsync();
        var factor = _owners[org];
        var renewed = (await ApiClient.ReadAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" }))).GetProperty("id").GetGuid();
        var rejectedOne = (await ApiClient.ReadAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" }))).GetProperty("id").GetGuid();
        await ApproveTwiceAsync(officer, other, renewed);

        var rejected = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/factor-authorizations/{rejectedOne}/reject", new { reason = "Duplikat" }));
        var mine = (await ApiClient.ReadAsync(await factor.GetAsync($"/api/v1/organizations/{org}/factor-authorizations"))).EnumerateArray().ToList();

        Assert.Equal("Expired", await _database.ScalarAsync<string>($"SELECT status FROM compliance.factor_authorizations WHERE id = '{firstId}'"));
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Single(mine, a => a.GetProperty("status").GetString() == "Verified");
        Assert.True(await AuthorizedAsync(org));
    }

    [Fact]
    public async Task Other_organizations_sellers_and_non_officers_cannot_reach_the_endpoints()
    {
        var (factor, org) = await DevFactorAsync();
        var id = (await ApiClient.ReadAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" }))).GetProperty("id").GetGuid();
        var (outsider, outsiderOrg) = await DevFactorAsync();
        await outsider.StepUpAsync();
        var seller = _app.NewClient();
        await seller.SignUpAsync();
        var sellerOrg = (await ApiClient.ReadAsync(await seller.PostAsync("/api/v1/organizations", new { pib = TestIds.NewPib() }))).GetProperty("id").GetGuid();

        using var read = await outsider.GetAsync($"/api/v1/organizations/{org}/factor-authorizations");
        using var write = await outsider.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" });
        using var queue = await outsider.GetAsync("/api/v1/admin/factor-authorizations");
        using var approve = await outsider.PostAsync($"/api/v1/admin/factor-authorizations/{id}/approve", new { });
        using var reject = await outsider.PostAsync($"/api/v1/admin/factor-authorizations/{id}/reject", new { reason = "x" });
        using var revoke = await outsider.PostAsync($"/api/v1/admin/factor-authorizations/{id}/revoke", new { reason = "x" });
        using var create = await outsider.PostAsync("/api/v1/admin/factor-organizations", new { pib = TestIds.NewPib(), legalName = "X", firstUserEmail = "x@example.rs" });
        var sellerSubmit = await ApiClient.ErrorCodeAsync(await seller.PostAsync($"/api/v1/organizations/{sellerOrg}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" }));
        var ownList = (await ApiClient.ReadAsync(await outsider.GetAsync($"/api/v1/organizations/{outsiderOrg}/factor-authorizations"))).EnumerateArray().Count();

        Assert.All([read, write, queue, approve, reject, revoke, create], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("conflict_state", sellerSubmit);
        Assert.Equal(0, ownList);
    }

    // ----- helpers -----

    private readonly Dictionary<Guid, ApiClient> _owners = [];

    private async Task<(ApiClient Client, Guid Org)> InvitedFactorAsync(ApiClient officer)
    {
        var factor = _app.NewClient();
        var (email, _) = await factor.SignUpAsync();
        var created = await ApiClient.ReadAsync(await officer.PostAsync("/api/v1/admin/factor-organizations", new { pib = TestIds.NewPib(), legalName = "Faktor d.o.o.", firstUserEmail = email }));
        var org = created.GetProperty("id").GetGuid();
        await ApiClient.ReadAsync(await factor.PostAsync("/api/v1/invitations/accept", new { token = _database.Emails.LinkValue(email, "token") }));
        using (var approved = await factor.PostAsync($"/api/v1/dev/organizations/{org}/approve"))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        return (factor, org);
    }

    private async Task<(ApiClient Client, Guid Org)> DevFactorAsync()
    {
        var factor = _app.NewClient();
        await factor.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await factor.PostAsync("/api/v1/dev/factor-organizations", new { pib = TestIds.NewPib(), legalName = "Faktor a.d." }))).GetProperty("id").GetGuid();
        using (var approved = await factor.PostAsync($"/api/v1/dev/organizations/{org}/approve"))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        _owners[org] = factor;
        return (factor, org);
    }

    private async Task<(Guid Org, Guid Id)> VerifiedBankAsync()
    {
        var (factor, org) = await DevFactorAsync();
        var id = (await ApiClient.ReadAsync(await factor.PostAsync($"/api/v1/organizations/{org}/factor-authorizations", new { kind = "Bank", issuingAuthority = "NationalBankOfSerbia" }))).GetProperty("id").GetGuid();
        using var first = await StaffAsync("ComplianceOfficer");
        await first.StepUpAsync();
        using var second = await StaffAsync("ComplianceOfficer");
        await second.StepUpAsync();
        await ApproveTwiceAsync(first, second, id);
        return (org, id);
    }

    private static async Task ApproveTwiceAsync(ApiClient first, ApiClient second, Guid id)
    {
        var snapshot = await SnapshotAsync(first, id);
        await ApiClient.ReadAsync(await first.PostAsync($"/api/v1/admin/factor-authorizations/{id}/approve",
            new { sourceSnapshotDocumentId = snapshot, verificationMethod = "OfficialRegisterLookup", notes = "Na spisku NBS" }));
        var verified = await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/factor-authorizations/{id}/approve", new { }));
        Assert.Equal("Verified", verified.GetProperty("status").GetString());
    }

    private static async Task<Guid> SnapshotAsync(ApiClient officer, Guid id)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes($"%PDF-1.7\nsnimak registra {Guid.NewGuid()}\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "registar.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/factor-authorizations/{id}/snapshot") { Content = form };
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await officer.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return (await ApiClient.ReadAsync(await officer.Http.SendAsync(request, TestContext.Current.CancellationToken))).GetProperty("documentId").GetGuid();
    }

    private static async Task<Guid> UploadAsync(ApiClient client, Guid org)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes($"%PDF-1.7\nresenje {Guid.NewGuid()}\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "resenje.pdf");
        form.Add(new StringContent("KybEvidence"), "purpose");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await client.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add(OrganizationScopedMetadata.Header, org.ToString());
        return (await ApiClient.ReadAsync(await client.Http.SendAsync(request, TestContext.Current.CancellationToken))).GetProperty("id").GetGuid();
    }

    private AsyncServiceScope Scope()
    {
        var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return scope;
    }

    private async Task<bool> AuthorizedAsync(Guid org)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IAuthorizationQuery>().IsFactorAuthorizedAsync(org, TestContext.Current.CancellationToken);
    }

    private async Task<ApiClient> StaffAsync(string role)
    {
        var staff = _app.NewClient();
        await staff.SignUpAsync();
        using var grant = await staff.PostAsync("/api/v1/dev/me/platform-roles", new { role });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        return staff;
    }
}
