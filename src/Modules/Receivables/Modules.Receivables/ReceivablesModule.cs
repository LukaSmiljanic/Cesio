using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Receivables;

/// <summary>Composition entry point of the Receivables module; the hosts call it to register the module's services.</summary>
public static class ReceivablesModule
{
    public static IServiceCollection AddReceivablesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ReceivablesDbContext>(ReceivablesDbContext.SchemaName, migrationOrder: 5);
        services.AddScoped<ImportService>();
        return services;
    }

    /// <summary><c>POST /receivables/import-from-sef</c>, <c>GET /receivables</c>, <c>GET /receivables/{id}</c> (task 010).</summary>
    public static IEndpointRouteBuilder MapReceivableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/receivables").RequireAuthorization().RequireOrganization();
        group.MapPost("/import-from-sef", (ImportFromSefRequest body, ImportService service, CancellationToken ct) => service.ImportAsync(body, ct))
            .RequireIdempotency();
        group.MapGet("", (ImportService service, CancellationToken ct) => service.ListAsync(ct));
        group.MapGet("/{id:guid}", (Guid id, ImportService service, CancellationToken ct) => service.GetAsync(id, ct));
        return app;
    }
}
