using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Documents.Contracts;
using Modules.Documents.Persistence;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Documents;

/// <summary>Composition entry point of the Documents module (task 006).</summary>
public static class DocumentsModule
{
    /// <summary>Environments where the fake scanner may run (tasks/README, Phase 2 stubs).</summary>
    private static readonly string[] FakeScannerEnvironments = ["Development", "local", "dev", "staging"];

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<DocumentsDbContext>(DocumentsDbContext.SchemaName, migrationOrder: 4);
        services.AddSingleton<IDocumentStore, BlobDocumentStore>();
        services.AddSingleton<IMalwareScanner>(provider =>
        {
            if (provider.GetRequiredService<IConfiguration>()["Documents:Scanner"] != "Fake")
            {
                return new DeferredMalwareScanner();
            }

            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return FakeScannerEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? new FakeMalwareScanner()
                : throw new InvalidOperationException($"The fake malware scanner must not run in the '{environment}' environment.");
        });
        services.AddScoped<DocumentService>();
        services.AddScoped<IDocumentQuery, DocumentQuery>();
        return services;
    }

    /// <summary><c>POST /documents</c>, <c>GET /documents/{id}</c>, <c>GET /documents/{id}/content</c> (api.md Documents).</summary>
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var documents = app.MapGroup("/api/v1/documents").RequireAuthorization().RequireOrganization();
        documents.MapPost("", (HttpRequest request, DocumentService service, CancellationToken ct) => service.UploadAsync(request, ct))
            .RequireIdempotency()
            // 20 MB of file plus the multipart framing; larger bodies are refused before they are read.
            .WithMetadata(new RequestSizeLimitAttribute(DocumentService.MaxSizeBytes + (1024 * 1024)));
        documents.MapGet("/{id:guid}", (Guid id, DocumentService service, CancellationToken ct) => service.GetAsync(id, ct));
        documents.MapGet("/{id:guid}/content", (Guid id, HttpResponse response, DocumentService service, CancellationToken ct) =>
            service.DownloadAsync(id, response, ct));
        return app;
    }
}
