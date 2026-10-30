using System.Net;
using Integrations.Sef;
using Microsoft.Extensions.DependencyInjection;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 010: SEF connection and invoice import against the fake SEF client.</summary>
public sealed class SefImportTests(SqlServerContainer server, TestStorage storage) : IAsyncLifetime
{
    private const string BuyerPib = "106006802";

    private static readonly string[] OneId = ["1"];

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
    public async Task An_invoice_is_imported_as_an_invoice_and_a_draft_receivable()
    {
        var seller = await ConnectedSellerAsync();
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "F-2026-1", 1_000_000m, 200_000m), "Approved");

        var result = await ImportOneAsync(seller, id);
        var receivables = await ApiClient.ReadAsync(await GetAsync(seller, "/api/v1/receivables"));
        var receivable = receivables.EnumerateArray().Single();

        Assert.Equal("Imported", result.GetProperty("status").GetString());
        Assert.Equal(result.GetProperty("receivableId").GetGuid(), receivable.GetProperty("id").GetGuid());
        Assert.Equal("Draft", receivable.GetProperty("state").GetString());
        Assert.Equal("1200000.00", receivable.GetProperty("faceValue").GetProperty("amount").GetString());
        Assert.Equal("RSD", receivable.GetProperty("faceValue").GetProperty("currency").GetString());
        Assert.Equal(BuyerPib, receivable.GetProperty("debtor").GetProperty("pib").GetString());
        Assert.Equal("Approved", receivable.GetProperty("invoice").GetProperty("sefStatus").GetString());
        Assert.Equal(64, await _database.ScalarAsync<int>("SELECT TOP 1 LEN(fingerprint) FROM receivables.receivables"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM receivables.sef_status_history"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM documents.stored_documents WHERE purpose = 'InvoiceUbl'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Invoice.Imported'"));
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'Receivable.Created'"));
    }

    [Fact]
    public async Task Invoices_the_organization_did_not_issue_or_cannot_use_are_reported_per_item()
    {
        var seller = await ConnectedSellerAsync();
        var otherIssuer = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(TestIds.NewPib(), BuyerPib, "X-1", 100m, 20m));
        var euro = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "X-2", 100m, 20m, currency: "EUR"));
        var badTotals = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "X-3", 100m, 20m, gross: 150m));
        var malformed = Sef.AddInvoice(seller.Key, "<Invoice><broken"u8.ToArray());
        var noDebtorPib = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, "123456789", "X-4", 100m, 20m));
        var threeDecimals = Sef.AddInvoice(seller.Key, System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(FakeSefClient.Ubl(seller.Pib, BuyerPib, "X-5", 100m, 20m)).Replace(">100.00<", ">100.005<", StringComparison.Ordinal)));
        var ids = new[] { $"{otherIssuer}", $"{euro}", $"{badTotals}", $"{malformed}", "999999999", $"{noDebtorPib}", $"{threeDecimals}" };

        var results = (await ApiClient.ReadAsync(await ImportAsync(seller, new { sefInvoiceIds = ids })))
            .GetProperty("results").EnumerateArray().Select(r => (r.GetProperty("status").GetString(), r.TryGetProperty("reason", out var reason) ? reason.GetString() : null)).ToList();

        Assert.Equal(
            [("NotIssuer", "supplier_pib_differs"), ("Unsupported", "currency_not_rsd"), ("Unsupported", "totals_inconsistent"), ("Unsupported", "malformed_ubl"),
             ("NotFound", null), ("Unsupported", "debtor_without_pib"), ("Unsupported", "malformed_ubl")],
            results);
        Assert.Equal(0, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM receivables.invoices"));
    }

    [Fact]
    public async Task Importing_the_same_invoice_again_returns_the_existing_receivable()
    {
        var seller = await ConnectedSellerAsync();
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "D-1", 500m, 100m));

        var first = await ImportOneAsync(seller, id);
        var second = await ImportOneAsync(seller, id);

        Assert.Equal("AlreadyImported", second.GetProperty("status").GetString());
        Assert.Equal(first.GetProperty("receivableId").GetGuid(), second.GetProperty("receivableId").GetGuid());
    }

    [Fact]
    public async Task Two_parallel_imports_of_one_invoice_create_one_invoice()
    {
        var seller = await ConnectedSellerAsync();
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "P-1", 500m, 100m));

        var responses = await Task.WhenAll(ImportAsync(seller, new { sefInvoiceIds = new[] { $"{id}" } }), ImportAsync(seller, new { sefInvoiceIds = new[] { $"{id}" } }));
        var statuses = new List<string?>();
        foreach (var response in responses)
        {
            statuses.Add((await ApiClient.ReadAsync(response)).GetProperty("results")[0].GetProperty("status").GetString());
        }

        Assert.Equal(["AlreadyImported", "Imported"], statuses.Order());
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM receivables.invoices WHERE sef_invoice_id = '{id}'"));
    }

    [Fact]
    public async Task A_rejected_key_invalidates_the_connection()
    {
        var seller = await ConnectedSellerAsync();
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "R-1", 500m, 100m));
        Sef.RejectedKeys.Add(seller.Key);

        var code = await ApiClient.ErrorCodeAsync(await ImportAsync(seller, new { sefInvoiceIds = new[] { $"{id}" } }));
        var connection = await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/sef-connection"));

        Assert.Equal("sef_connection_invalid", code);
        Assert.Equal("Invalid", connection.GetProperty("status").GetString());
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Organizations.SefConnectionInvalidated'"));
    }

    [Fact]
    public async Task A_key_of_another_company_or_without_invoices_is_not_connected()
    {
        var (client, org, pib) = await SellerAsync();
        var otherCompanyKey = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(otherCompanyKey, FakeSefClient.Ubl(TestIds.NewPib(), BuyerPib, "O-1", 500m, 100m));
        var emptyKey = $"k-{Guid.NewGuid():N}";
        await client.StepUpAsync();

        var mismatch = await ApiClient.ErrorCodeAsync(await client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/sef-connection", new { apiKey = otherCompanyKey }));
        var empty = await ApiClient.ErrorCodeAsync(await client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/sef-connection", new { apiKey = emptyKey }));

        Assert.Equal("sef_pib_mismatch", mismatch);
        Assert.Equal("sef_no_invoices", empty);
        Assert.Equal(0, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM platform.outbox WHERE type = 'Organizations.SefConnectionValidated'"));
    }

    [Fact]
    public async Task A_failed_key_rotation_keeps_the_working_connection()
    {
        var seller = await ConnectedSellerAsync();
        var otherCompanyKey = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(otherCompanyKey, FakeSefClient.Ubl(TestIds.NewPib(), BuyerPib, "O-2", 500m, 100m));

        var code = await ApiClient.ErrorCodeAsync(await seller.Client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{seller.Org}/sef-connection", new { apiKey = otherCompanyKey }));
        var connection = await ApiClient.ReadAsync(await seller.Client.GetAsync($"/api/v1/organizations/{seller.Org}/sef-connection"));
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "O-3", 500m, 100m));

        Assert.Equal("sef_pib_mismatch", code);
        Assert.Equal("Connected", connection.GetProperty("status").GetString());
        Assert.Equal("Imported", (await ImportOneAsync(seller, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_organization_cannot_read_or_set_the_sef_connection()
    {
        var owner = await ConnectedSellerAsync();
        var (outsider, _, _) = await SellerAsync();
        await outsider.StepUpAsync();

        using var read = await outsider.GetAsync($"/api/v1/organizations/{owner.Org}/sef-connection");
        using var write = await outsider.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{owner.Org}/sef-connection", new { apiKey = $"demo-{owner.Pib}" });
        var connection = await ApiClient.ReadAsync(await owner.Client.GetAsync($"/api/v1/organizations/{owner.Org}/sef-connection"));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal("Connected", connection.GetProperty("status").GetString());
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{owner.Org}' AND action LIKE 'Organization.SefKey%'"));
    }

    [Fact]
    public async Task Import_without_a_connection_is_refused()
    {
        var (client, org, _) = await SellerAsync();

        var code = await ApiClient.ErrorCodeAsync(await ImportAsync(new Seller(client, org, "", ""), new { sefInvoiceIds = OneId }));

        Assert.Equal("sef_connection_invalid", code);
    }

    [Fact]
    public async Task Another_seller_cannot_read_the_receivable()
    {
        var seller = await ConnectedSellerAsync();
        var id = Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "C-1", 500m, 100m));
        var receivableId = (await ImportOneAsync(seller, id)).GetProperty("receivableId").GetGuid();
        var other = await ConnectedSellerAsync();

        using var read = await GetAsync(other, $"/api/v1/receivables/{receivableId}");
        var list = await ApiClient.ReadAsync(await GetAsync(other, "/api/v1/receivables"));
        var reimport = await ImportOneAsync(other, id);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Empty(list.EnumerateArray());
        Assert.Equal("NotFound", reimport.GetProperty("status").GetString());
        Assert.False(reimport.TryGetProperty("receivableId", out var leaked) && leaked.ValueKind != System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task A_date_range_imports_every_invoice_issued_in_it()
    {
        var seller = await ConnectedSellerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "Q-1", 500m, 100m, issueDate: today.AddDays(-3)));
        Sef.AddInvoice(seller.Key, FakeSefClient.Ubl(seller.Pib, BuyerPib, "Q-2", 700m, 140m, issueDate: today.AddDays(-2)));

        var results = (await ApiClient.ReadAsync(await ImportAsync(seller, new { issueDateFrom = today.AddDays(-5), issueDateTo = today })))
            .GetProperty("results").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ToList();

        // The connection proof invoice of ConnectedSellerAsync is in the range too.
        Assert.Equal(3, results.Count);
        Assert.All(results, s => Assert.Equal("Imported", s));
    }

    private sealed record Seller(ApiClient Client, Guid Org, string Pib, string Key);

    private async Task<(ApiClient Client, Guid Org, string Pib)> SellerAsync()
    {
        var client = _app.NewClient();
        await client.SignUpAsync();
        var pib = TestIds.NewPib();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib }))).GetProperty("id").GetGuid();
        return (client, org, pib);
    }

    /// <summary>A seller whose SEF key is connected; the key already has one invoice, which proves the PIB.</summary>
    private async Task<Seller> ConnectedSellerAsync()
    {
        var (client, org, pib) = await SellerAsync();
        var key = $"k-{Guid.NewGuid():N}";
        Sef.AddInvoice(key, FakeSefClient.Ubl(pib, BuyerPib, $"PROOF-{Guid.NewGuid():N}"[..20], 10m, 2m));
        await client.StepUpAsync();
        var connected = await ApiClient.ReadAsync(await client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/sef-connection", new { apiKey = key }));
        Assert.Equal("Connected", connected.GetProperty("status").GetString());
        return new Seller(client, org, pib, key);
    }

    private static async Task<System.Text.Json.JsonElement> ImportOneAsync(Seller seller, long id) =>
        (await ApiClient.ReadAsync(await ImportAsync(seller, new { sefInvoiceIds = new[] { $"{id}" } }))).GetProperty("results")[0];

    private static async Task<HttpResponseMessage> ImportAsync(Seller seller, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/receivables/import-from-sef") { Content = System.Net.Http.Json.JsonContent.Create(body) };
        request.Headers.Add(Modules.Identity.IdentityModule.AntiforgeryHeader, await seller.Client.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add(OrganizationScopedMetadata.Header, seller.Org.ToString());
        return await seller.Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetAsync(Seller seller, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(OrganizationScopedMetadata.Header, seller.Org.ToString());
        return await seller.Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
