using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Documents.Contracts;
using Modules.Documents.Persistence;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Documents;

/// <summary>Composition entry point of the Documents module (task 006).</summary>
public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<DocumentsDbContext>(DocumentsDbContext.SchemaName, migrationOrder: 4);
        services.AddSingleton<IDocumentStore, BlobDocumentStore>();
        services.AddSingleton<IMalwareScanner, FakeMalwareScanner>();
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
            .DisableAntiforgery();
        documents.MapGet("/{id:guid}", (Guid id, DocumentService service, CancellationToken ct) => service.GetAsync(id, ct));
        documents.MapGet("/{id:guid}/content", (Guid id, HttpResponse response, DocumentService service, CancellationToken ct) =>
            service.DownloadAsync(id, response, ct));
        return app;
    }
}
