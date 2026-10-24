using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Compliance;
using Modules.Identity;
using Modules.Organizations.Contracts;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 007: KYB cases, beneficial owners, screening, risk, four-eyes decisions and holds.</summary>
public sealed class KybTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);
    private ApiApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData("Petrović Marko", "Марко Петровић")]
    [InlineData("ĐORĐEVIĆ Jelena", "jelena djordjevic")]
    [InlineData("Neka Firma d.o.o.", "NEKA FIRMA DOO")]
    public void Names_match_across_scripts_diacritics_and_case(string a, string b) =>
        Assert.True(NameMatcher.Similarity(NameMatcher.Normalize(a), NameMatcher.Normalize(b)) >= 0.92m);

    [Fact]
    public void Different_names_do_not_match() =>
        Assert.True(NameMatcher.Similarity(NameMatcher.Normalize("Petar Petrović"), NameMatcher.Normalize("Jovana Nikolić")) < 0.8m);

    [Fact]
    public async Task A_submitted_organization_is_screened_reviewed_and_approved_end_to_end()
    {
        var (owner, org) = await SubmittedSellerAsync("Primer d.o.o.");
        using var declared = await owner.PostAsync($"/api/v1/organizations/{org}/kyb/beneficial-owners",
            new { owners = new[] { new { name = "Ana Anić", dateOfBirth = "1980-05-01", nationality = "RS", ownershipPercent = 100m, pepDeclared = false } } });
        using var consent = await owner.PostAsync($"/api/v1/organizations/{org}/kyb/sharing-consent", new { consent = true });
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();

        var queue = (await ApiClient.ReadAsync(await officer.GetAsync("/api/v1/admin/kyb-cases"))).EnumerateArray().ToList();
        var caseId = queue.Single(c => c.GetProperty("organizationId").GetGuid() == org).GetProperty("id").GetGuid();
        var rescreened = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/screening"));
        var incomplete = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Approved", reason = "ok", representativesVerified = true }));
        using var rated = await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/risk", new { rating = "Low", reason = "Starija firma, domaći vlasnik" });
        var approved = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Approved", reason = "Sve provereno", representativesVerified = true }));
        var status = (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}"))).GetProperty("status").GetString();
        var kyb = await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}/kyb"));

        Assert.Equal(HttpStatusCode.OK, declared.StatusCode);
        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
        Assert.Contains(rescreened.GetProperty("screening").EnumerateArray(), r => r.GetProperty("subjectKind").GetString() == "BeneficialOwner");
        Assert.All(rescreened.GetProperty("screening").EnumerateArray(), r => Assert.Equal("NoLists", r.GetProperty("result").GetString()));
        Assert.Equal("kyb_incomplete", incomplete);
        Assert.Equal(HttpStatusCode.OK, rated.StatusCode);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("Approved", status);
        Assert.Equal("Approved", kyb.GetProperty("status").GetString());
        Assert.True(kyb.GetProperty("sharingConsent").GetBoolean());
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Organizations.OrganizationApproved'"));
    }

    [Fact]
    public async Task A_potential_match_blocks_approval_and_high_risk_needs_two_officers()
    {
        using var officer = await StaffAsync("ComplianceOfficer");
        using var second = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        await second.StepUpAsync();
        await ImportListAsync(officer, "UN", "Марко Петровић\nSome Other Person\n");
        var again = await ImportListAsync(officer, "UN", "Марко Петровић\nSome Other Person\n");
        var (owner, org) = await SubmittedSellerAsync("Primer d.o.o.");
        using (var declared = await owner.PostAsync($"/api/v1/organizations/{org}/kyb/beneficial-owners",
            new { owners = new[] { new { name = "Petrović Marko", nationality = "RS", ownershipPercent = 60m, pepDeclared = false } } }))
        {
            Assert.Equal(HttpStatusCode.OK, declared.StatusCode);
        }

        var caseId = await CaseIdAsync(officer, org);
        var detail = await ApiClient.ReadAsync(await officer.GetAsync($"/api/v1/admin/kyb-cases/{caseId}"));
        var hit = detail.GetProperty("screening").EnumerateArray().Single(r => r.GetProperty("result").GetString() == "PotentialMatch");
        using (var rated = await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/risk", new { rating = "High", reason = "PEP i složena struktura" }))
        {
            Assert.Equal(HttpStatusCode.OK, rated.StatusCode);
        }

        var blocked = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Approved", reason = "x", representativesVerified = true }));
        using (var resolved = await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/screening/{hit.GetProperty("id").GetGuid()}/resolution", new { result = "Clear", note = "Drugi datum rođenja" }))
        {
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        }

        var first = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Approved", reason = "Prvi", representativesVerified = true }));
        var sameOfficer = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Approved", reason = "Opet", representativesVerified = true }));
        var stillPending = (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}"))).GetProperty("status").GetString();
        var final = await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Approved", reason = "Drugi", representativesVerified = true }));

        Assert.False(again.GetProperty("imported").GetBoolean());
        Assert.Equal("screening_unresolved", blocked);
        Assert.Equal("PendingSecondApproval", first.GetProperty("status").GetString());
        Assert.Equal("four_eyes_required", sameOfficer);
        Assert.Equal("UnderReview", stillPending);
        Assert.Equal("Approved", final.GetProperty("status").GetString());
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'ScreeningList.Imported'"));
    }

    [Fact]
    public async Task A_confirmed_match_forces_a_rejection_and_needs_info_returns_the_organization_to_draft()
    {
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        await ImportListAsync(officer, "EU", "Zabranjena Firma d.o.o.\n");
        var (owner, org) = await SubmittedSellerAsync("Zabranjena Firma d.o.o.");
        var caseId = await CaseIdAsync(officer, org);
        var hit = (await ApiClient.ReadAsync(await officer.GetAsync($"/api/v1/admin/kyb-cases/{caseId}"))).GetProperty("screening").EnumerateArray()
            .Single(r => r.GetProperty("result").GetString() == "PotentialMatch").GetProperty("id").GetGuid();
        using (var confirmed = await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/screening/{hit}/resolution", new { result = "ConfirmedMatch", note = "Isti MB" }))
        {
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        }

        var needsInfo = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "NeedsInfo", reason = "x" }));
        var rejected = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{caseId}/decision", new { decision = "Rejected", reason = "Potvrđeno poklapanje; eskalirano (LQ-33)" }));

        var (otherOwner, otherOrg) = await SubmittedSellerAsync("Druga Firma d.o.o.");
        var otherCase = await CaseIdAsync(officer, otherOrg);
        var info = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/kyb-cases/{otherCase}/decision", new { decision = "NeedsInfo", reason = "Izjava o vlasnicima se ne slaže sa registrom" }));
        var draft = (await ApiClient.ReadAsync(await otherOwner.GetAsync($"/api/v1/organizations/{otherOrg}"))).GetProperty("status").GetString();

        Assert.Equal("confirmed_match", needsInfo);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Rejected", (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}"))).GetProperty("status").GetString());
        Assert.Equal("NeedsInfo", info.GetProperty("status").GetString());
        Assert.Equal("Draft", draft);
    }

    [Fact]
    public async Task Holds_block_and_a_fraud_hold_needs_two_officers_to_lift()
    {
        using var officer = await StaffAsync("ComplianceOfficer");
        using var second = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        await second.StepUpAsync();
        var subject = Guid.NewGuid();

        var hold = await ApiClient.ReadAsync(await officer.PostAsync("/api/v1/admin/holds", new { subjectType = "Organization", subjectId = subject, kind = "FraudInvestigation", reason = "Sumnja na prevaru" }));
        var holdId = hold.GetProperty("id").GetGuid();
        var active = await HasHoldAsync(subject);
        var requested = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/holds/{holdId}/lift"));
        var self = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/holds/{holdId}/lift"));
        var stillActive = await HasHoldAsync(subject);
        var lifted = await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/holds/{holdId}/lift"));

        Assert.True(active);
        Assert.True(requested.GetProperty("liftRequested").GetBoolean());
        Assert.Equal("four_eyes_required", self);
        Assert.True(stillActive);
        Assert.NotEqual(JsonValueKind.Null, lifted.GetProperty("liftedAt").ValueKind);
        Assert.False(await HasHoldAsync(subject));
    }

    [Fact]
    public async Task Kyb_data_is_for_the_organization_and_compliance_only_and_staff_cannot_join_organizations()
    {
        var (_, org) = await SubmittedSellerAsync("Primer d.o.o.");
        using var outsider = _app.NewClient();
        await outsider.SignUpAsync();
        using var officer = await StaffAsync("ComplianceOfficer");

        using var foreign = await outsider.GetAsync($"/api/v1/organizations/{org}/kyb");
        using var foreignDeclare = await outsider.PostAsync($"/api/v1/organizations/{org}/kyb/beneficial-owners", new { owners = new[] { new { name = "X", ownershipPercent = 50m } } });
        using var queue = await outsider.GetAsync("/api/v1/admin/kyb-cases");
        using var holds = await outsider.PostAsync("/api/v1/admin/holds", new { subjectType = "Organization", subjectId = org, kind = "ComplianceHold", reason = "x" });
        var staffOrg = await ApiClient.ErrorCodeAsync(await officer.PostAsync("/api/v1/organizations", new { pib = TestIds.NewPib() }));

        Assert.All([foreign, foreignDeclare, queue, holds], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("staff_account", staffOrg);
    }

    private async Task<(ApiClient Owner, Guid Org)> SubmittedSellerAsync(string legalName)
    {
        var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await owner.PostAsync("/api/v1/organizations", new { pib = TestIds.NewPib() }))).GetProperty("id").GetGuid();
        using (var patched = await owner.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{org}",
            new { legalName, mb = TestIds.NewMb(), legalForm = "d.o.o.", registeredAddress = "Bulevar 1, Beograd" }, ifMatch: 0))
        {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        }

        using (var representative = await owner.PostAsync($"/api/v1/organizations/{org}/legal-representatives", new { name = "Ana Anić", role = "Direktor" }))
        {
            Assert.Equal(HttpStatusCode.Created, representative.StatusCode);
        }

        var terms = await ApiClient.ReadAsync(await owner.GetAsync("/api/v1/terms/current"));
        using (var accepted = await owner.PostAsync($"/api/v1/organizations/{org}/terms-acceptances", new { termsVersion = terms.GetProperty("version").GetString() }))
        {
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using (var submitted = await owner.PostAsync($"/api/v1/organizations/{org}/submit"))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        await DeliverSubmittedAsync(org);
        return (owner, org);
    }

    /// <summary>Delivers OrganizationSubmitted to the KYB consumer, as the worker's outbox dispatcher does.</summary>
    private async Task DeliverSubmittedAsync(Guid org)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var consumer = scope.ServiceProvider.GetRequiredService<KybService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(
            ct => consumer.HandleAsync(new OrganizationSubmitted(org, OrganizationType.Seller), Guid.NewGuid(), ct), TestContext.Current.CancellationToken);
    }

    private async Task<bool> HasHoldAsync(Guid subject)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Modules.Compliance.Contracts.IHoldQuery>()
            .HasActiveHoldAsync(Modules.Compliance.Contracts.HoldSubject.Organization, subject, TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> CaseIdAsync(ApiClient officer, Guid org) =>
        (await ApiClient.ReadAsync(await officer.GetAsync("/api/v1/admin/kyb-cases"))).EnumerateArray()
            .Single(c => c.GetProperty("organizationId").GetGuid() == org).GetProperty("id").GetGuid();

    private static async Task<JsonElement> ImportListAsync(ApiClient officer, string source, string csv)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "lista.csv");
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/screening-lists/{source}") { Content = form };
        upload.Headers.Add(IdentityModule.AntiforgeryHeader, await officer.AntiforgeryTokenAsync());
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await ApiClient.ReadAsync(await officer.Http.SendAsync(upload, TestContext.Current.CancellationToken));
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
