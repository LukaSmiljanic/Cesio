using System.Net.Mime;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;

namespace Shared.Infrastructure.Idempotency;

/// <summary>Marks an endpoint as a command that requires an <c>Idempotency-Key</c> (CLAUDE.md, database.md §4).</summary>
public sealed class RequiresIdempotencyMetadata;

public static class IdempotencySetup
{
    /// <summary>Retention of idempotency rows (database.md §4); a technical replay cache, pending LQ-47.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequiresIdempotencyMetadata());

    /// <summary>
    /// Runs every endpoint marked with <see cref="RequireIdempotency{TBuilder}"/> in one unit-of-work transaction that
    /// also stores the idempotency key and the response. Add it after authentication.
    /// </summary>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) => app.UseMiddleware<IdempotencyMiddleware>();

}

internal sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private const int SqlUniqueViolation = 2627;
    private const int SqlUniqueIndexViolation = 2601;

    public async Task InvokeAsync(HttpContext http, UnitOfWork unitOfWork, PlatformDbContext platform, IRequestContext request, TimeProvider timeProvider)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<RequiresIdempotencyMetadata>() is null)
        {
            await next(http);
            return;
        }

        var key = request.IdempotencyKey;
        if (key is null || key.Length > IdempotencyRecord.MaxKeyLength)
        {
            await Problem(http, StatusCodes.Status400BadRequest, "validation_failed",
                $"The {HttpRequestContext.IdempotencyKeyHeader} header is required (at most {IdempotencyRecord.MaxKeyLength} characters).");
            return;
        }

        var orgId = request.OrganizationId
            ?? throw new InvalidOperationException("Idempotent endpoints need an authenticated organization context (task 004).");
        var userId = request.UserId
            ?? throw new InvalidOperationException("Idempotent endpoints need an authenticated user (task 004).");
        var endpoint = EndpointName(http);
        var requestHash = await HashBodyAsync(http.Request);
        var now = timeProvider.GetUtcNow();
        var record = new IdempotencyRecord
        {
            IdemKey = key,
            OrgId = orgId,
            UserId = userId,
            Endpoint = endpoint,
            RequestSha256 = requestHash,
            CreatedAt = now,
            ExpiresAt = now + IdempotencySetup.Retention,
        };

        await unitOfWork.BeginAsync(http.RequestAborted);
        try
        {
            // A concurrent duplicate blocks on the primary key until the first request commits or rolls back.
            platform.IdempotencyRecords.Add(record);
            await platform.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: SqlUniqueViolation or SqlUniqueIndexViolation })
        {
            await unitOfWork.RollbackAsync();
            platform.ChangeTracker.Clear();
            await ReplayAsync(http, platform, orgId, endpoint, key, requestHash);
            return;
        }

        await ExecuteAndStoreAsync(http, unitOfWork, platform, record);
    }

    private async Task ExecuteAndStoreAsync(HttpContext http, UnitOfWork unitOfWork, PlatformDbContext platform, IdempotencyRecord record)
    {
        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await next(http);

            if (http.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                await unitOfWork.RollbackAsync();
            }
            else
            {
                record.ResponseStatus = http.Response.StatusCode;
                record.ResponseBody = IsJson(http.Response.ContentType) && buffer.Length > 0
                    ? System.Text.Encoding.UTF8.GetString(buffer.ToArray())
                    : null;
                await platform.SaveChangesAsync(http.RequestAborted);
                await unitOfWork.CommitAsync(http.RequestAborted);
            }
        }
        catch
        {
            await unitOfWork.RollbackAsync();
            throw;
        }
        finally
        {
            http.Response.Body = originalBody;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, http.RequestAborted);
    }

    private static async Task ReplayAsync(HttpContext http, PlatformDbContext platform, Guid orgId, string endpoint, string key, string requestHash)
    {
        var stored = await platform.IdempotencyRecords.AsNoTracking()
            .SingleAsync(r => r.OrgId == orgId && r.Endpoint == endpoint && r.IdemKey == key, http.RequestAborted);
        if (stored.RequestSha256 != requestHash)
        {
            await Problem(http, StatusCodes.Status422UnprocessableEntity, "idempotency_key_reused",
                "This idempotency key was already used with a different request body.");
            return;
        }

        http.Response.StatusCode = stored.ResponseStatus!.Value;
        if (stored.ResponseBody is not null)
        {
            http.Response.ContentType = stored.ResponseStatus >= StatusCodes.Status400BadRequest
                ? "application/problem+json"
                : MediaTypeNames.Application.Json;
            await http.Response.WriteAsync(stored.ResponseBody, http.RequestAborted);
        }
    }

    private static string EndpointName(HttpContext http)
    {
        var route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value;
        return $"{http.Request.Method} {route}";
    }

    private static async Task<string> HashBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        var hash = await SHA256.HashDataAsync(request.Body, request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsJson(string? contentType) =>
        contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static Task Problem(HttpContext http, int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode })
            .ExecuteAsync(http);
}

/// <summary>Deletes expired idempotency rows (retention 30 days, database.md §4).</summary>
internal sealed class IdempotencyCleanupJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<IdempotencyCleanupJob> logger)
    : SingletonJob(scopeFactory, configuration, logger)
{
    private const int BatchSize = 1000;

    protected override string LockName => "idempotency-cleanup";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var platform = services.GetRequiredService<PlatformDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        int deleted;
        do
        {
            deleted = await platform.IdempotencyRecords.Where(r => r.ExpiresAt < now).Take(BatchSize).ExecuteDeleteAsync(cancellationToken);
        }
        while (deleted == BatchSize);
    }
}
