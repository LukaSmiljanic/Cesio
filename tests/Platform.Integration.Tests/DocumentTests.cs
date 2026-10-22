using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Modules.Documents.Contracts;
using Modules.Identity;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 006: upload → scan → authorized download, with the fake scanner and Azurite.</summary>
public sealed class DocumentTests(SqlServerContainer server, AzuriteContainer storage) : IAsyncLifetime
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private readonly TestDatabase _database = new(server);
    private readonly GrantingPolicy _policy = new();
    private ApiApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(
            _database,
            new Dictionary<string, string?> { ["ConnectionStrings:Documents"] = storage.ConnectionString },
            services => services.AddSingleton<IDocumentAccessPolicy>(_policy));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task An_uploaded_pdf_is_scanned_hashed_and_downloadable_by_its_organization()
    {
        var (client, org) = await SignedUpWithOrganizationAsync();

        var uploaded = await ApiClient.ReadAsync(await UploadAsync(client, org, Pdf, "../../Ugovor o faktoringu.pdf"));
        var id = uploaded.GetProperty("id").GetGuid();
        using var download = await GetAsync(client, org, $"/api/v1/documents/{id}/content");
        var bytes = await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Clean", uploaded.GetProperty("scanStatus").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Pdf)), uploaded.GetProperty("sha256").GetString());
        Assert.Equal("Ugovor_o_faktoringu.pdf", uploaded.GetProperty("fileName").GetString());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, bytes);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.access_log WHERE entity_id = '{id}' AND action = 'Document.Downloaded'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{id}' AND action = 'Document.Uploaded'"));
    }

    [Fact]
    public async Task A_file_whose_bytes_are_not_an_allowed_type_is_rejected()
    {
        var (client, org) = await SignedUpWithOrganizationAsync();

        using var response = await UploadAsync(client, org, Encoding.ASCII.GetBytes("MZ this is an executable"), "invoice.pdf");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task An_infected_file_is_stored_as_infected_and_never_served()
    {
        var (client, org) = await SignedUpWithOrganizationAsync();
        var eicar = Encoding.ASCII.GetBytes("%PDF-1.4\nX5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*\n");

        var uploaded = await ApiClient.ReadAsync(await UploadAsync(client, org, eicar, "scan.pdf"));
        using var download = await GetAsync(client, org, $"/api/v1/documents/{uploaded.GetProperty("id").GetGuid()}/content");

        Assert.Equal("Infected", uploaded.GetProperty("scanStatus").GetString());
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
    }

    [Fact]
    public async Task Another_organization_gets_404_unless_a_module_policy_grants_access()
    {
        var (owner, ownerOrg) = await SignedUpWithOrganizationAsync();
        var kyb = (await ApiClient.ReadAsync(await UploadAsync(owner, ownerOrg, Pdf, "kyb.pdf", "KybEvidence"))).GetProperty("id").GetGuid();
        var listing = (await ApiClient.ReadAsync(await UploadAsync(owner, ownerOrg, Pdf, "listing.pdf", "ListingAttachment"))).GetProperty("id").GetGuid();
        var (outsider, outsiderOrg) = await SignedUpWithOrganizationAsync();
        _policy.Granted = outsiderOrg;

        using var metadata = await GetAsync(outsider, outsiderOrg, $"/api/v1/documents/{kyb}");
        using var content = await GetAsync(outsider, outsiderOrg, $"/api/v1/documents/{kyb}/content");
        using var granted = await GetAsync(outsider, outsiderOrg, $"/api/v1/documents/{listing}/content");

        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
    }

    [Fact]
    public async Task Documents_need_an_organization_of_the_user()
    {
        var (client, org) = await SignedUpWithOrganizationAsync();
        var id = (await ApiClient.ReadAsync(await UploadAsync(client, org, Pdf, "a.pdf"))).GetProperty("id").GetGuid();
        var (_, otherOrg) = await SignedUpWithOrganizationAsync();

        using var withoutHeader = await client.GetAsync($"/api/v1/documents/{id}");
        using var foreignHeader = await GetAsync(client, otherOrg, $"/api/v1/documents/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignHeader.StatusCode);
    }

    [Fact]
    public async Task A_file_over_20_mb_is_rejected()
    {
        var (client, org) = await SignedUpWithOrganizationAsync();
        var large = new byte[(20 * 1024 * 1024) + 1];
        Pdf.CopyTo(large, 0);

        using var response = await UploadAsync(client, org, large, "large.pdf");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private async Task<(ApiClient Client, Guid Organization)> SignedUpWithOrganizationAsync()
    {
        var client = _app.NewClient();
        await client.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib = TestIds.NewPib() }))).GetProperty("id").GetGuid();
        return (client, org);
    }

    private static async Task<HttpResponseMessage> UploadAsync(ApiClient client, Guid org, byte[] bytes, string fileName, string purpose = "KybEvidence")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(purpose), "purpose");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await client.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add(OrganizationScopedMetadata.Header, org.ToString());
        return await client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetAsync(ApiClient client, Guid org, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(OrganizationScopedMetadata.Header, org.ToString());
        return await client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A module policy that lets one organization read listing attachments.</summary>
    private sealed class GrantingPolicy : IDocumentAccessPolicy
    {
        public Guid? Granted { get; set; }

        public bool AppliesTo(DocumentPurpose purpose) => purpose == DocumentPurpose.ListingAttachment;

        public Task<bool> CanReadAsync(DocumentInfo document, Guid userId, Guid? organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(organizationId is not null && organizationId == Granted);
    }
}
