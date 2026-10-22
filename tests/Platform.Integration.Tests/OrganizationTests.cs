using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 005: Draft claims, members and the Signatory flag, submit, and the organization context.</summary>
public sealed class OrganizationTests(SqlServerContainer server) : IAsyncLifetime
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

    [Fact]
    public async Task Two_users_hold_draft_claims_for_the_same_pib_without_seeing_each_other()
    {
        var pib = NewPib();
        using var alice = _app.NewClient();
        await alice.SignUpAsync();
        using var bob = _app.NewClient();
        await bob.SignUpAsync();

        var aliceOrg = await CreateSellerAsync(alice, pib);
        var bobOrg = await CreateSellerAsync(bob, pib);
        var aliceOrgs = await ApiClient.ReadAsync(await alice.GetAsync("/api/v1/me/organizations"));
        using var bobReadsAlice = await bob.GetAsync($"/api/v1/organizations/{aliceOrg}");

        Assert.NotEqual(aliceOrg, bobOrg);
        Assert.Equal([aliceOrg], aliceOrgs.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, bobReadsAlice.StatusCode);
        Assert.Equal(1, await AuditCountAsync(aliceOrg, "Organization.ClaimCreated"));
    }

    [Fact]
    public async Task An_invalid_pib_is_rejected()
    {
        using var client = _app.NewClient();
        await client.SignUpAsync();

        using var response = await client.PostAsync("/api/v1/organizations", new { pib = "123456789" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_organization_twice_with_one_key_creates_it_once()
    {
        using var client = _app.NewClient();
        await client.SignUpAsync();
        var key = Guid.NewGuid().ToString();
        var pib = NewPib();

        var first = await ApiClient.ReadAsync(await client.SendAsync(HttpMethod.Post, "/api/v1/organizations", new { pib }, key));
        var second = await ApiClient.ReadAsync(await client.SendAsync(HttpMethod.Post, "/api/v1/organizations", new { pib }, key));

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM orgs.organizations WHERE pib = '{pib}'"));
    }

    [Fact]
    public async Task A_seller_organization_cannot_invite_beyond_its_member_limit()
    {
        using var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = await CreateSellerAsync(owner, NewPib());
        await owner.StepUpAsync();

        var code = await ApiClient.ErrorCodeAsync(await owner.PostAsync($"/api/v1/organizations/{org}/members", new { email = "colleague@example.rs" }));

        Assert.Equal("member_limit_reached", code);
    }

    [Fact]
    public async Task Inviting_a_member_requires_step_up()
    {
        using var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = await CreateFactorAsync(owner);

        var code = await ApiClient.ErrorCodeAsync(await owner.PostAsync($"/api/v1/organizations/{org}/members", new { email = "colleague@example.rs" }));

        Assert.Equal(StepUp.ErrorCode, code);
    }

    [Fact]
    public async Task Factor_members_and_the_signatory_flag_work_end_to_end()
    {
        using var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = await CreateFactorAsync(owner);
        using var colleague = _app.NewClient();
        var colleagueEmail = $"colleague-{Guid.NewGuid():N}@example.rs";
        await colleague.SignUpAsync(colleagueEmail);

        await owner.StepUpAsync();
        using (var invite = await owner.PostAsync($"/api/v1/organizations/{org}/members", new { email = colleagueEmail }))
        {
            Assert.Equal(HttpStatusCode.Accepted, invite.StatusCode);
        }

        using (var wrongUser = await owner.PostAsync("/api/v1/invitations/accept", new { token = _database.Emails.LinkValue(colleagueEmail, "token") }))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongUser.StatusCode);
        }

        using (var accept = await colleague.PostAsync("/api/v1/invitations/accept", new { token = _database.Emails.LinkValue(colleagueEmail, "token") }))
        {
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        }

        var members = (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}/members"))).EnumerateArray().ToList();
        Assert.Equal(2, members.Count);
        var colleagueMember = members[1];
        var memberId = colleagueMember.GetProperty("id").GetGuid();

        Assert.Equal("signatory_requires_representative", await ApiClient.ErrorCodeAsync(
            await owner.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{org}/members/{memberId}", new { isSignatory = true })));

        var representative = await AddRepresentativeAsync(owner, org);
        var updated = await ApiClient.ReadAsync(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{org}/members/{memberId}",
            new { isSignatory = true, legalRepresentativeId = representative }, ifMatch: colleagueMember.GetProperty("version").GetInt32()));
        Assert.True(updated.GetProperty("isSignatory").GetBoolean());

        var colleagueOrgs = await ApiClient.ReadAsync(await colleague.GetAsync("/api/v1/me/organizations"));
        Assert.True(colleagueOrgs.EnumerateArray().Single().GetProperty("isSignatory").GetBoolean());

        using (var remove = await owner.DeleteAsync($"/api/v1/organizations/{org}/members/{memberId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        }

        var ownerMember = members[0].GetProperty("id").GetGuid();
        Assert.Equal("last_owner", await ApiClient.ErrorCodeAsync(await owner.DeleteAsync($"/api/v1/organizations/{org}/members/{ownerMember}")));
        using var removedReads = await colleague.GetAsync($"/api/v1/organizations/{org}");
        Assert.Equal(HttpStatusCode.NotFound, removedReads.StatusCode);

        foreach (var action in new[] { "Member.Invited", "Member.Added", "Member.SignatoryChanged", "Member.Removed" })
        {
            Assert.Equal(1, await AuditCountAsync(org, action));
        }
    }

    [Fact]
    public async Task Two_simultaneous_signatory_edits_give_one_412()
    {
        using var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = await CreateFactorAsync(owner);
        var representative = await AddRepresentativeAsync(owner, org);
        var member = (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}/members"))).EnumerateArray().Single();
        var path = $"/api/v1/organizations/{org}/members/{member.GetProperty("id").GetGuid()}";
        var version = member.GetProperty("version").GetInt32();
        await owner.StepUpAsync();

        var responses = await Task.WhenAll(
            owner.SendAsync(HttpMethod.Patch, path, new { isSignatory = true, legalRepresentativeId = representative }, ifMatch: version),
            owner.SendAsync(HttpMethod.Patch, path, new { isSignatory = false, legalRepresentativeId = representative }, ifMatch: version));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.PreconditionFailed],
            responses.Select(r => r.StatusCode).Order());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task An_organization_is_submitted_only_with_the_minimum_data()
    {
        using var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = await CreateSellerAsync(owner, NewPib());

        Assert.Equal("organization_incomplete", await ApiClient.ErrorCodeAsync(await owner.PostAsync($"/api/v1/organizations/{org}/submit")));

        var details = await ApiClient.ReadAsync(await owner.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{org}",
            new { legalName = "Primer d.o.o.", mb = NewMb(), legalForm = "d.o.o.", registeredAddress = "Bulevar 1, Beograd" }));
        Assert.Equal("Primer d.o.o.", details.GetProperty("legalName").GetString());
        await AddRepresentativeAsync(owner, org);
        var terms = await ApiClient.ReadAsync(await owner.GetAsync("/api/v1/terms/current"));
        using (var accept = await owner.PostAsync($"/api/v1/organizations/{org}/terms-acceptances", new { termsVersion = terms.GetProperty("version").GetString() }))
        {
            Assert.Equal(HttpStatusCode.Created, accept.StatusCode);
        }

        var submitted = await ApiClient.ReadAsync(await owner.PostAsync($"/api/v1/organizations/{org}/submit"));
        using var editAfterSubmit = await owner.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{org}", new { legalName = "Drugo ime" });

        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, editAfterSubmit.StatusCode);
        Assert.Equal(1, await AuditCountAsync(org, "Organization.Submitted"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.outbox WHERE type = 'Organizations.OrganizationSubmitted' AND payload LIKE '%{org}%'"));
    }

    [Fact]
    public async Task A_user_of_another_organization_cannot_read_or_change_it()
    {
        using var owner = _app.NewClient();
        await owner.SignUpAsync();
        var org = await CreateFactorAsync(owner);
        var representative = await AddRepresentativeAsync(owner, org);
        var member = (await ApiClient.ReadAsync(await owner.GetAsync($"/api/v1/organizations/{org}/members"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        using var outsider = _app.NewClient();
        await outsider.SignUpAsync();
        await CreateFactorAsync(outsider);
        await outsider.StepUpAsync();
        var basePath = $"/api/v1/organizations/{org}";

        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, basePath, null),
            (HttpMethod.Patch, basePath, new { legalName = "x" }),
            (HttpMethod.Post, $"{basePath}/submit", null),
            (HttpMethod.Get, $"{basePath}/members", null),
            (HttpMethod.Post, $"{basePath}/members", new { email = "x@example.rs" }),
            (HttpMethod.Patch, $"{basePath}/members/{member}", new { isSignatory = false }),
            (HttpMethod.Delete, $"{basePath}/members/{member}", null),
            (HttpMethod.Get, $"{basePath}/legal-representatives", null),
            (HttpMethod.Post, $"{basePath}/legal-representatives", new { name = "x", role = "y" }),
            (HttpMethod.Put, $"{basePath}/legal-representatives/{representative}", new { name = "x", role = "y" }),
            (HttpMethod.Delete, $"{basePath}/legal-representatives/{representative}", null),
            (HttpMethod.Get, $"{basePath}/terms-acceptances", null),
            (HttpMethod.Post, $"{basePath}/terms-acceptances", new { termsVersion = "x" }),
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = method == HttpMethod.Get ? await outsider.GetAsync(path) : await outsider.SendAsync(method, path, body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path} returned {(int)response.StatusCode}.");
        }

    }

    [Fact]
    public void Every_organization_route_has_the_organization_context_and_commands_need_an_idempotency_key()
    {
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("/organizations/{", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            Assert.True(endpoint.Metadata.GetMetadata<OrganizationScopedMetadata>() is not null, $"{endpoint.DisplayName} has no organization context.");
            var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!methods.Contains("GET"))
            {
                Assert.True(endpoint.Metadata.GetMetadata<RequiresIdempotencyMetadata>() is not null, $"{endpoint.DisplayName} has no Idempotency-Key requirement.");
            }
        }
    }

    private static async Task<Guid> CreateSellerAsync(ApiClient client, string pib) =>
        (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib }))).GetProperty("id").GetGuid();

    private static async Task<Guid> CreateFactorAsync(ApiClient client) =>
        (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/dev/factor-organizations", new { pib = NewPib(), legalName = "Faktor a.d." }))).GetProperty("id").GetGuid();

    private static async Task<Guid> AddRepresentativeAsync(ApiClient client, Guid org) =>
        (await ApiClient.ReadAsync(await client.PostAsync($"/api/v1/organizations/{org}/legal-representatives", new { name = "Petar Petrović", role = "Direktor" })))
            .GetProperty("id").GetGuid();

    private Task<int> AuditCountAsync(Guid org, string action) =>
        _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{org}' AND action = '{action}'");

    /// <summary>A random PIB with a valid check digit.</summary>
    private static string NewPib() => WithCheckDigit(8, value => Pib.TryParse(value, out _));

    /// <summary>A random matični broj with a valid check digit.</summary>
    private static string NewMb() => WithCheckDigit(7, value => MaticniBroj.TryParse(value, out _));

    private static string WithCheckDigit(int length, Func<string, bool> valid)
    {
        while (true)
        {
            var prefix = string.Concat(Enumerable.Range(0, length).Select(i => (char)('0' + Random.Shared.Next(i == 0 ? 1 : 0, 10))));
            for (var digit = 0; digit < 10; digit++)
            {
                if (valid(prefix + digit))
                {
                    return prefix + digit;
                }
            }
        }
    }
}
