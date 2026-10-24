using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Identity;
using Modules.Organizations.Contracts;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 009: payout accounts, their NBS-snapshot verification, the cool-down and the duplicate alert.</summary>
public sealed class BankAccountTests(SqlServerContainer server, AzuriteContainer storage) : IAsyncLifetime
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
    public async Task An_account_is_verified_with_the_nbs_snapshot_and_usable_only_after_the_cool_down()
    {
        var (owner, org) = await SellerAsync();
        var number = TestIds.NewBankAccount();
        var badChecksum = number[..16] + (number[16..] == "00" ? "01" : "00");

        var invalid = await ApiClient.ErrorCodeAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = badChecksum }));
        var added = await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = number }));
        var id = added.GetProperty("id").GetGuid();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        var noEvidence = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/bank-accounts/{id}/verify", new { holderMatchesPib = true }));
        var snapshot = await EvidenceAsync(officer, id);
        var verified = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/bank-accounts/{id}/verify", new { evidenceDocumentId = snapshot, holderMatchesPib = true }));
        var verifiedAt = DateTimeOffset.Parse(verified.GetProperty("usableFrom").GetString()!, System.Globalization.CultureInfo.InvariantCulture).AddHours(-72);

        Assert.Equal("validation_failed", invalid);
        Assert.Equal("Pending", added.GetProperty("status").GetString());
        Assert.Equal("Banca Intesa", added.GetProperty("bankName").GetString());
        Assert.Equal("evidence_missing", noEvidence);
        Assert.Equal("Verified", verified.GetProperty("status").GetString());
        Assert.False(await UsableAsync(org, id, verifiedAt.AddHours(72).AddSeconds(-1)));
        Assert.True(await UsableAsync(org, id, verifiedAt.AddHours(72)));
        Assert.False(await UsableAsync(Guid.NewGuid(), id, verifiedAt.AddDays(10)));
        Assert.Empty(await UsableListAsync(org));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Organizations.BankAccountVerified'"));
        Assert.Equal(0, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND changes LIKE '%{number[3..16]}%'"));
    }

    [Fact]
    public async Task A_further_account_waits_for_the_official_email_and_a_deactivated_one_is_not_usable()
    {
        var (owner, org) = await SellerAsync();
        await _database.ExecuteAsync(TestIds.VerifiedAccountSql(org));
        var first = (await UsableListAsync(org)).Single();

        var missingEmail = await ApiClient.ErrorCodeAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = TestIds.NewBankAccount("265") }));
        var official = $"apr-{Guid.NewGuid():N}@firma.rs";
        await _database.ExecuteAsync($"UPDATE orgs.organizations SET official_email = '{official}' WHERE id = '{org}'");
        var second = await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = TestIds.NewBankAccount("265") }));
        var link = _database.Emails.Sent.Last(m => m.To == official).Body;
        var token = System.Text.RegularExpressions.Regex.Match(link, @"/confirmations/([A-Za-z0-9_-]+)\?").Groups[1].Value;
        using var anonymous = _app.NewClient();
        var confirmed = await ApiClient.ReadAsync(await anonymous.PostAsync($"/api/v1/confirmations/{token}", new { purpose = "PayoutAccountChange" }));
        var deactivated = await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts/{first.Id}/deactivate"));
        var list = (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}/bank-accounts"))).EnumerateArray().ToList();

        Assert.Equal("official_email_missing", missingEmail);
        Assert.Equal("ConfirmationPending", second.GetProperty("status").GetString());
        Assert.Equal("Raiffeisen banka", second.GetProperty("bankName").GetString());
        Assert.True(confirmed.GetProperty("confirmed").GetBoolean());
        Assert.Equal("Pending", list.Single(a => a.GetProperty("id").GetGuid() == second.GetProperty("id").GetGuid()).GetProperty("status").GetString());
        Assert.Equal("Deactivated", deactivated.GetProperty("status").GetString());
        Assert.Empty(await UsableListAsync(org));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Organizations.BankAccountDeactivated'"));
    }

    [Fact]
    public async Task The_same_account_in_two_organizations_is_flagged_alerted_and_blocked_until_cleared()
    {
        var (owner, org) = await SellerAsync();
        var (other, otherOrg) = await SellerAsync();
        var number = TestIds.NewBankAccount();
        var firstId = (await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = number }))).GetProperty("id").GetGuid();
        var duplicate = await ApiClient.ReadAsync(await other.PostAsync($"/api/v1/organizations/{otherOrg}/bank-accounts", new { accountNumber = number }));
        var duplicateId = duplicate.GetProperty("id").GetGuid();
        using var officer = await StaffAsync("ComplianceOfficer");
        await officer.StepUpAsync();
        var snapshot = await EvidenceAsync(officer, firstId);

        var duplicateSnapshot = await EvidenceAsync(officer, duplicateId);
        var blocked = await ApiClient.ErrorCodeAsync(await officer.PostAsync($"/api/v1/admin/bank-accounts/{duplicateId}/verify", new { evidenceDocumentId = duplicateSnapshot, holderMatchesPib = true }));
        var verified = await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/bank-accounts/{firstId}/verify", new { evidenceDocumentId = snapshot, holderMatchesPib = true }));
        await ApiClient.ReadAsync(await officer.PostAsync($"/api/v1/admin/bank-accounts/{duplicateId}/reject", new { reason = "Račun pripada drugoj firmi" }));
        var deactivated = await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts/{firstId}/deactivate"));
        await _database.ExecuteAsync($"UPDATE orgs.organizations SET official_email = 'apr-{Guid.NewGuid():N}@firma.rs' WHERE id = '{org}'");
        var readded = await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = number }));

        Assert.False(duplicate.TryGetProperty("duplicateFlag", out var flag) && flag.ValueKind == System.Text.Json.JsonValueKind.True);
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM orgs.bank_accounts WHERE id = '{duplicateId}' AND duplicate_flag = 1"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM orgs.bank_accounts WHERE id = '{firstId}' AND duplicate_flag = 0"));
        Assert.Equal("duplicate_account", blocked);
        Assert.Equal("Verified", verified.GetProperty("status").GetString());
        Assert.Equal("Deactivated", deactivated.GetProperty("status").GetString());
        Assert.Equal("ConfirmationPending", readded.GetProperty("status").GetString());
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Compliance.Alert' AND reason = 'account_registered_by_another_organization'"));
    }

    [Fact]
    public async Task A_seller_is_not_approved_without_a_verified_payout_account()
    {
        var (_, org) = await SellerAsync();
        await _database.ExecuteAsync($"UPDATE orgs.organizations SET claimed_at = SYSDATETIMEOFFSET() WHERE id = '{org}'");
        await _database.ExecuteAsync($"INSERT INTO orgs.pristupnice (id, org_id, generated_document_id, generated_sha256, status, method, created_at, version) VALUES (NEWID(), '{org}', NEWID(), REPLICATE('a', 64), 'Accepted', 'Qes', SYSDATETIMEOFFSET(), 0)");
        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var query = scope.ServiceProvider.GetRequiredService<IOrganizationQuery>();

        var without = await query.SellerOnboardingCompleteAsync(org, TestContext.Current.CancellationToken);
        await _database.ExecuteAsync(TestIds.VerifiedAccountSql(org));
        var with = await query.SellerOnboardingCompleteAsync(org, TestContext.Current.CancellationToken);

        Assert.False(without);
        Assert.True(with);
    }

    [Fact]
    public async Task Other_organizations_and_non_officers_cannot_reach_the_accounts()
    {
        var (owner, org) = await SellerAsync();
        var id = (await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = TestIds.NewBankAccount() }))).GetProperty("id").GetGuid();
        var (outsider, outsiderOrg) = await SellerAsync();

        using var read = await outsider.GetAsync($"/api/v1/organizations/{org}/bank-accounts");
        using var add = await outsider.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = TestIds.NewBankAccount() });
        using var deactivate = await outsider.PostAsync($"/api/v1/organizations/{org}/bank-accounts/{id}/deactivate");
        using var queue = await outsider.GetAsync("/api/v1/admin/bank-accounts");
        using var verify = await outsider.PostAsync($"/api/v1/admin/bank-accounts/{id}/verify", new { holderMatchesPib = true });
        using var reject = await outsider.PostAsync($"/api/v1/admin/bank-accounts/{id}/reject", new { reason = "x" });
        using var clear = await outsider.PostAsync($"/api/v1/admin/bank-accounts/{id}/clear-duplicate", new { reason = "x" });
        using var codes = await outsider.SendAsync(HttpMethod.Put, "/api/v1/admin/bank-codes/999", new { name = "X" });
        var own = (await ApiClient.ReadAsync(await outsider.GetAsync($"/api/v1/organizations/{outsiderOrg}/bank-accounts"))).EnumerateArray().Count();

        Assert.All([read, add, deactivate, queue, verify, reject, clear, codes], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(0, own);
    }

    // ----- helpers -----

    private async Task<(ApiClient Client, Guid Org)> SellerAsync()
    {
        var client = _app.NewClient();
        await client.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib = TestIds.NewPib() }))).GetProperty("id").GetGuid();
        await client.StepUpAsync();
        return (client, org);
    }

    private async Task<bool> UsableAsync(Guid org, Guid id, DateTimeOffset at)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IBankAccountQuery>().IsUsableForFundingAsync(org, id, at, TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<FundingAccount>> UsableListAsync(Guid org)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IBankAccountQuery>().UsableForFundingAsync(org, TestContext.Current.CancellationToken);
    }

    private AsyncServiceScope Scope()
    {
        var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return scope;
    }

    private static async Task<Guid> EvidenceAsync(ApiClient officer, Guid id)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes($"%PDF-1.7\nNBS registar racuna {Guid.NewGuid()}\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "nbs.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/bank-accounts/{id}/evidence") { Content = form };
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await officer.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return (await ApiClient.ReadAsync(await officer.Http.SendAsync(request, TestContext.Current.CancellationToken))).GetProperty("documentId").GetGuid();
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
