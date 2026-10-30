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

/// <summary>Task 012: listings, the visibility rule of the marketplace, withdrawal and operations suspension (offers: task 013).</summary>
public sealed partial class MarketplaceTests(SqlServerContainer server, TestStorage storage) : IAsyncLifetime
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
    private ApiClient? _officer;

    private FakeSefClient Sef => _app.Services.GetRequiredService<FakeSefClient>();

    private static DateTimeOffset InTenDays => DateTimeOffset.UtcNow.AddDays(10);

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database, new Dictionary<string, string?>
        {
            ["Storage:Root"] = storage.Root,
            ["Documents:Scanner"] = "Fake",
            ["Sef:Client"] = "Fake",
            ["Signatures:Validator"] = "Fake",
        });
    }

    public async ValueTask DisposeAsync()
    {
        _officer?.Dispose();
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_seller_lists_a_verified_receivable_and_an_authorized_factor_sees_it_without_the_reserve_price()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var factor = await FactorAsync();

        using var opened = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings",
            new { closesAt = InTenDays, audience = new { type = "AllVerifiedFactors" }, reservePrice = new { amount = "1100000.00", currency = "RSD" } });
        var listing = await ApiClient.ReadAsync(opened);
        var id = listing.GetProperty("id").GetGuid();
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();
        using var feed = await Send(factor, HttpMethod.Get, "/api/v1/marketplace/listings");
        var feedBody = await feed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var detail = await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings/{id}");
        var detailBody = await detail.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var detailJson = JsonDocument.Parse(detailBody).RootElement;

        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        Assert.Equal("1100000.00", listing.GetProperty("reservePrice").GetProperty("amount").GetString());
        Assert.Equal("Listed", state);
        Assert.Contains(id.ToString(), feedBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.DoesNotContain("reserve", detailBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1100000", feedBody + detailBody, StringComparison.Ordinal);
        Assert.Equal(BuyerPib, detailJson.GetProperty("debtor").GetProperty("pib").GetString());
        Assert.Equal("0.15", detailJson.GetProperty("platformFee").GetProperty("share").GetString());
        Assert.Equal(2, detailJson.GetProperty("documents").GetArrayLength());
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = 'Listing.Opened'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{receivable}' AND action = 'Receivable.Listed'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.access_log WHERE entity_id = '{id}' AND action = 'Listing.Viewed'"));
    }

    [Fact]
    public async Task A_factor_never_sees_a_listing_outside_its_audience_over_random_audiences()
    {
        var seller = await SellerAsync();
        var factors = new List<Party>();
        for (var i = 0; i < 4; i++)
        {
            factors.Add(await FactorAsync());
        }

        var random = new Random(12_011);
        var expected = factors.ToDictionary(f => f.Org, _ => new HashSet<Guid>());
        for (var i = 0; i < 5; i++)
        {
            var receivable = await VerifiedAsync(seller);
            var selected = random.Next(2) == 0;
            var included = selected ? factors.Where(_ => random.Next(2) == 0).Select(f => f.Org).DefaultIfEmpty(factors[0].Org).ToList() : [];
            var excluded = factors.Select(f => f.Org).Where(org => !included.Contains(org) && random.Next(3) == 0).ToList();
            using var opened = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", new
            {
                closesAt = InTenDays,
                audience = new { type = selected ? "Selected" : "AllVerifiedFactors", factorOrgIds = selected ? included : null },
                excludedFactorOrgIds = excluded,
            });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var id = (await ApiClient.ReadAsync(opened)).GetProperty("id").GetGuid();
            foreach (var factor in factors.Where(f => (!selected || included.Contains(f.Org)) && !excluded.Contains(f.Org)))
            {
                expected[factor.Org].Add(id);
            }
        }

        foreach (var factor in factors)
        {
            var seen = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/marketplace/listings?limit=100"))).GetProperty("items")
                .EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToHashSet();
            Assert.Equal(expected[factor.Org].Order(), seen.Order());
            foreach (var hidden in expected.Values.SelectMany(v => v).Except(expected[factor.Org]))
            {
                using var detail = await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings/{hidden}");
                Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Unapproved_factors_sellers_and_related_parties_do_not_see_listings()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var id = await OpenAsync(seller, receivable);
        var unapproved = await FactorAsync(approve: false);
        var relatedToDebtor = await FactorAsync(pib: BuyerPib);

        using var unapprovedFeed = await Send(unapproved, HttpMethod.Get, "/api/v1/marketplace/listings");
        using var unapprovedDetail = await Send(unapproved, HttpMethod.Get, $"/api/v1/marketplace/listings/{id}");
        using var sellerFeed = await Send(seller, HttpMethod.Get, "/api/v1/marketplace/listings");
        using var relatedDetail = await Send(relatedToDebtor, HttpMethod.Get, $"/api/v1/marketplace/listings/{id}");
        var relatedFeed = await ApiClient.ReadAsync(await Send(relatedToDebtor, HttpMethod.Get, "/api/v1/marketplace/listings"));

        Assert.Equal(HttpStatusCode.Forbidden, unapprovedFeed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unapprovedDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sellerFeed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, relatedDetail.StatusCode);
        Assert.Equal(0, relatedFeed.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Two_parallel_listings_of_one_receivable_give_one_201_and_one_409()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var body = new { closesAt = InTenDays, audience = new { type = "AllVerifiedFactors" } };

        var responses = await Task.WhenAll(
            Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", body),
            Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", body));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM marketplace.listings WHERE receivable_id = '{receivable}'"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_stale_sef_status_is_re_polled_and_listing_fails_closed()
    {
        var seller = await SellerAsync();
        var cancelled = await VerifiedAsync(seller);
        var unreachable = await VerifiedAsync(seller);
        await _database.ExecuteAsync("UPDATE receivables.invoices SET sef_status_checked_at = DATEADD(day, -2, SYSDATETIMEOFFSET())");
        Sef.SetStatus(seller.Key, await SefIdAsync(cancelled), "Cancelled");
        const string Observed = "SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Invoice.SefStatusObserved'";
        var historyBefore = await CountAsync("SELECT COUNT(*) FROM receivables.sef_status_history");
        var observedBefore = await CountAsync(Observed);

        var notApproved = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{cancelled}/listings", new { closesAt = InTenDays }));
        Sef.FailNextCalls = 1;
        var unavailable = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{unreachable}/listings", new { closesAt = InTenDays }));

        Assert.Equal("sef_not_approved", notApproved);
        Assert.Equal("upstream_unavailable", unavailable);
        Assert.Equal(historyBefore + 1, await CountAsync("SELECT COUNT(*) FROM receivables.sef_status_history"));
        Assert.Equal(observedBefore + 1, await CountAsync(Observed));
    }

    [Fact]
    public async Task Listing_validation_rejects_bad_dates_prices_states_and_foreign_receivables()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var draft = await DraftAsync(seller);
        var other = await SellerAsync();
        var maturity = DateTimeOffset.UtcNow.AddDays(60);

        var tooLate = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", new { closesAt = maturity.AddDays(-10) }));
        var past = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", new { closesAt = DateTimeOffset.UtcNow.AddHours(-1) }));
        var reserve = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings",
            new { closesAt = InTenDays, reservePrice = new { amount = "1200000.00", currency = "RSD" } }));
        var emptySelection = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings",
            new { closesAt = InTenDays, audience = new { type = "Selected", factorOrgIds = Array.Empty<Guid>() } }));
        var notFactor = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings",
            new { closesAt = InTenDays, audience = new { type = "Selected", factorOrgIds = new[] { other.Org } } }));
        var notVerified = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{draft}/listings", new { closesAt = InTenDays }));
        using var foreign = await Send(other, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", new { closesAt = InTenDays });

        Assert.Equal("maturity_too_close", tooLate);
        Assert.Equal("validation_failed", past);
        Assert.Equal("validation_failed", reserve);
        Assert.Equal("validation_failed", emptySelection);
        Assert.Equal("validation_failed", notFactor);
        Assert.Equal("receivable_not_verified", notVerified);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Withdrawal_returns_the_receivable_to_verified_once_and_only_for_its_seller()
    {
        var seller = await SellerAsync();
        var receivable = await VerifiedAsync(seller);
        var id = await OpenAsync(seller, receivable);
        var other = await SellerAsync();

        using var foreignWithdraw = await Send(other, HttpMethod.Post, $"/api/v1/listings/{id}/withdraw");
        using var foreignGet = await Send(other, HttpMethod.Get, $"/api/v1/listings/{id}");
        var withdrawn = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, $"/api/v1/listings/{id}/withdraw"));
        var again = await ApiClient.ErrorCodeAsync(await Send(seller, HttpMethod.Post, $"/api/v1/listings/{id}/withdraw"));
        var state = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/receivables/{receivable}"))).GetProperty("state").GetString();
        var relisted = await OpenAsync(seller, receivable);
        var mine = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, "/api/v1/listings"))).EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(HttpStatusCode.NotFound, foreignWithdraw.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal("Withdrawn", withdrawn.GetProperty("state").GetString());
        Assert.Equal("conflict_state", again);
        Assert.Equal("Verified", state);
        Assert.Equal([relisted, id], mine);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Marketplace.ListingWithdrawn'"));
    }

    [Fact]
    public async Task Operations_suspend_and_resume_a_listing_with_a_reason_and_others_cannot()
    {
        var seller = await SellerAsync();
        var id = await OpenAsync(seller, await VerifiedAsync(seller));
        var factor = await FactorAsync();
        using var operations = await StaffAsync("Operations");

        using var byFactor = await factor.Client.PostAsync($"/api/v1/admin/listings/{id}/suspend", new { reason = "Check" });
        var noReason = await ApiClient.ErrorCodeAsync(await operations.PostAsync($"/api/v1/admin/listings/{id}/suspend", new { reason = " " }));
        using var suspended = await operations.PostAsync($"/api/v1/admin/listings/{id}/suspend", new { reason = "Seller dispute reported" });
        using var hidden = await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings/{id}");
        using var resumed = await operations.PostAsync($"/api/v1/admin/listings/{id}/resume", new { reason = "Cleared" });
        using var visible = await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings/{id}");

        Assert.Equal(HttpStatusCode.NotFound, byFactor.StatusCode);
        Assert.Equal("validation_failed", noReason);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = 'Listing.Suspended'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = 'Listing.Resumed'"));
    }

    [Fact]
    public async Task The_feed_filters_and_pages_and_listing_documents_are_readable_only_by_factors_that_see_the_listing()
    {
        var seller = await SellerAsync();
        var small = await OpenAsync(seller, await VerifiedAsync(seller, net: 1_000_000m, vat: 200_000m));
        var large = await OpenAsync(seller, await VerifiedAsync(seller, net: 5_000_000m, vat: 1_000_000m));
        var factor = await FactorAsync();
        var excluded = await FactorAsync();
        var hiddenFrom = await VerifiedAsync(seller);
        using (var opened = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{hiddenFrom}/listings", new { closesAt = InTenDays, excludedFactorOrgIds = new[] { excluded.Org } }))
        {
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        }

        var first = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/marketplace/listings?limit=1&sort=amount"));
        var second = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings?limit=1&sort=amount&cursor={Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!)}"));
        var filtered = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/marketplace/listings?minAmount=2000000"));
        var badSort = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Get, "/api/v1/marketplace/listings?sort=price"));
        var detail = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/marketplace/listings/{small}"));
        var documentId = detail.GetProperty("documents")[0].GetProperty("id").GetGuid();
        var evidenceId = detail.GetProperty("documents")[1].GetProperty("id").GetGuid();
        using var allowed = await Send(factor, HttpMethod.Get, $"/api/v1/documents/{documentId}/content");
        var all = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/marketplace/listings?limit=100"));
        using var outsider = await Send(await SellerAsync(), HttpMethod.Get, $"/api/v1/documents/{evidenceId}/content");

        Assert.Equal(small, first.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.NotEqual(small, second.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal([large], filtered.GetProperty("items").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()));
        Assert.Equal("validation_failed", badSort);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(3, all.GetProperty("items").GetArrayLength());
        Assert.Equal(2, (await ApiClient.ReadAsync(await Send(excluded, HttpMethod.Get, "/api/v1/marketplace/listings"))).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.True(await CountAsync($"SELECT COUNT(*) FROM audit.access_log WHERE entity_id = '{documentId}' AND action = 'Document.Downloaded'") >= 1);
    }

    /// <summary>Every test organization has one legal representative with this name (the QES signer of task 016).</summary>
    private static string RepresentativeOf(string pib) => $"Zastupnik {pib}";

    private sealed record Party(ApiClient Client, Guid Org, string Pib, string Key, string Email = "");

    private async Task<Party> SellerAsync()
    {
        var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        var pib = TestIds.NewPib();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib }))).GetProperty("id").GetGuid();
        using (var representative = await client.PostAsync($"/api/v1/organizations/{org}/legal-representatives", new { name = RepresentativeOf(pib), role = "Direktor" }))
        {
            Assert.Equal(HttpStatusCode.Created, representative.StatusCode);
        }

        using (var approved = await client.PostAsync($"/api/v1/dev/organizations/{org}/approve"))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await _database.ExecuteAsync(TestIds.VerifiedAccountSql(org));
        var key = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(key, FakeSefClient.Ubl(pib, BuyerPib, $"PROOF-{Guid.NewGuid():N}"[..20], 10m, 2m));
        await client.StepUpAsync();
        using (var connect = await client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/sef-connection", new { apiKey = key }))
        {
            Assert.Equal(HttpStatusCode.OK, connect.StatusCode);
        }

        return new Party(client, org, pib, key, email);
    }

    private async Task<Party> FactorAsync(bool approve = true, string? pib = null)
    {
        var client = _app.NewClient();
        await client.SignUpAsync();
        pib ??= TestIds.NewPib();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/dev/factor-organizations", new { pib, legalName = "Faktor a.d." }))).GetProperty("id").GetGuid();
        using (var representative = await client.PostAsync($"/api/v1/organizations/{org}/legal-representatives", new { name = RepresentativeOf(pib), role = "Direktor" }))
        {
            Assert.Equal(HttpStatusCode.Created, representative.StatusCode);
        }

        if (approve)
        {
            using var approved = await client.PostAsync($"/api/v1/dev/organizations/{org}/approve");
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

            // A Verified authorization (task 008), set directly: FactorAuthorizationTests covers the two-officer flow.
            await _database.ExecuteAsync($"INSERT INTO compliance.factor_authorizations (id, org_id, kind, issuing_authority, status, verified_at, reverify_by, submitted_by, created_at, version) VALUES (NEWID(), '{org}', 'Bank', 'NationalBankOfSerbia', 'Verified', SYSDATETIMEOFFSET(), DATEADD(day, 30, CAST(GETDATE() AS date)), NEWID(), SYSDATETIMEOFFSET(), 0)");
        }

        return new Party(client, org, pib, string.Empty);
    }

    private async Task<ApiClient> StaffAsync(string role)
    {
        var staff = _app.NewClient();
        await staff.SignUpAsync();
        using var grant = await staff.PostAsync("/api/v1/dev/me/platform-roles", new { role });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        return staff;
    }

    private async Task<Guid> DraftAsync(Party seller, decimal net = 1_000_000m, decimal vat = 200_000m)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var sefId = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, $"F-{Guid.NewGuid():N}"[..16], net, vat, issueDate: today, dueDate: today.AddDays(60)), "Approved");
        var result = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Post, "/api/v1/receivables/import-from-sef", new { sefInvoiceIds = new[] { $"{sefId}" } })))
            .GetProperty("results")[0];
        return result.GetProperty("receivableId").GetGuid();
    }

    /// <summary>Imported, declared, documented, submitted and verified by compliance (screening always sends it to the queue).</summary>
    private async Task<Guid> VerifiedAsync(Party seller, decimal net = 1_000_000m, decimal vat = 200_000m)
    {
        var id = await DraftAsync(seller, net, vat);
        using (var declared = await Send(seller, HttpMethod.Put, $"/api/v1/receivables/{id}/declaration", new { answers = AllYes }))
        {
            Assert.Equal(HttpStatusCode.OK, declared.StatusCode);
        }

        using (var attached = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{id}/documents", new { documentId = await UploadAsync(seller) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, attached.StatusCode);
        }

        using (var submitted = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{id}/submit"))
        {
            Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        }

        if (_officer is null)
        {
            _officer = await StaffAsync("ComplianceOfficer");
            await _officer.StepUpAsync();
        }

        using var decided = await _officer.PostAsync($"/api/v1/admin/receivables/{id}/decision", new { decision = "Verified" });
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        return id;
    }

    private static async Task<Guid> OpenAsync(Party seller, Guid receivable)
    {
        using var opened = await Send(seller, HttpMethod.Post, $"/api/v1/receivables/{receivable}/listings", new { closesAt = InTenDays });
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        return (await ApiClient.ReadAsync(opened)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> UploadAsync(Party seller)
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

    private static async Task<HttpResponseMessage> Send(Party party, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        AddHeaders(request, party, method == HttpMethod.Get ? null : await party.Client.AntiforgeryTokenAsync());
        return await party.Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static void AddHeaders(HttpRequestMessage request, Party party, string? antiforgery)
    {
        request.Headers.Add(OrganizationScopedMetadata.Header, party.Org.ToString());
        if (antiforgery is not null)
        {
            request.Headers.Add(IdentityModule.AntiforgeryHeader, antiforgery);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }
    }

    private Task<long> SefIdAsync(Guid receivable) =>
        _database.ScalarAsync<long>($"SELECT CAST(i.sef_invoice_id AS bigint) FROM receivables.invoices i JOIN receivables.receivables r ON r.invoice_id = i.id WHERE r.id = '{receivable}'");

    private Task<int> CountAsync(string sql) => _database.ScalarAsync<int>(sql);
}
