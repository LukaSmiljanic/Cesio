using Modules.Audit.Contracts;
using Modules.Identity.Contracts;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

/// <summary><c>GET /admin/jobs</c> and <c>POST /admin/jobs/{name}/run</c> 🔐 (task 020): Operations see the scheduled jobs and trigger one.</summary>
internal static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/jobs").RequireAuthorization().AddEndpointFilter(RequireOperations);
        admin.MapGet("", async (JobRunner runner, CancellationToken ct) => Results.Ok(await runner.StatusAsync(ct)));
        admin.MapPost("/{name}/run", RunAsync).RequireIdempotency().RequireStepUp();
        return app;
    }

    /// <summary>
    /// The job runs in its own scope, as in the worker, so its per-item transactions stay short; the trigger and its
    /// outcome are audited afterwards in the request's transaction (before would hold the audit chain lock the job needs).
    /// </summary>
    private static async Task<IResult> RunAsync(string name, IRequestContext request, UnitOfWork unitOfWork, IAuditWriter audit, JobRunner runner, CancellationToken ct)
    {
        if (!runner.Definitions.Any(d => d.Name == name))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
        }

        var run = await runner.RunAsync(name, ct, request.UserId);
        await unitOfWork.ExecuteInTransactionAsync(
            c => audit.WriteAsync(new AuditEntry("Job.RunTriggered", "Job", Guid.Empty, NewState: run.Status,
                Changes: new Dictionary<string, string?> { ["job"] = name, ["items"] = run.Items.ToString(System.Globalization.CultureInfo.InvariantCulture) }), c), ct);
        return Results.Ok(run);
    }

    /// <summary>Operations and PlatformAdmin only; anyone else gets 404 (security.md §3).</summary>
    private static async ValueTask<object?> RequireOperations(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && (await roles.HasRoleAsync(userId, PlatformRoles.Operations, http.RequestAborted) || await roles.HasRoleAsync(userId, PlatformRoles.PlatformAdmin, http.RequestAborted))
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
