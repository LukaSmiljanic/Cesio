using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Shared.Infrastructure.Requests;

/// <summary>Who acts and from where (audit.md §2 <c>actor_type</c>).</summary>
public enum ActorType
{
    User,
    PlatformStaff,
    System,
    Integration,
}

/// <summary>
/// The actor and request data that audit events, the access log and idempotency need. The authenticated user and
/// active organization are filled in by task 004 (authentication); until then they are null for HTTP requests.
/// </summary>
public interface IRequestContext
{
    ActorType ActorType { get; }

    Guid? UserId { get; }

    Guid? OrganizationId { get; }

    string CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? DeviceId { get; }

    string? IdempotencyKey { get; }

    DateTimeOffset? MfaStepUpAt { get; }
}

/// <summary>The request context of background jobs and the outbox dispatcher.</summary>
public sealed class SystemRequestContext : IRequestContext
{
    public ActorType ActorType => ActorType.System;

    public Guid? UserId => null;

    public Guid? OrganizationId => null;

    public string CorrelationId { get; } = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? DeviceId => null;

    public string? IdempotencyKey => null;

    public DateTimeOffset? MfaStepUpAt => null;
}

/// <summary>
/// The request context of an HTTP request. The user comes from the authentication cookie (task 004); the active
/// organization comes with memberships (task 005).
/// </summary>
public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>User agents are stored in audit and access log rows; longer values are cut.</summary>
    public const int MaxUserAgentLength = 512;

    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("No HTTP request is active.");

    public ActorType ActorType => ActorType.User;

    public Guid? UserId => Guid.TryParse(Http.User.FindFirst(CesioClaims.UserId)?.Value, out var id) ? id : null;

    public Guid? OrganizationId => null;

    public string CorrelationId => Activity.Current?.TraceId.ToString() ?? Http.TraceIdentifier;

    public string? IpAddress => Http.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Http.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent
        ? userAgent[..Math.Min(userAgent.Length, MaxUserAgentLength)]
        : null;

    public string? DeviceId => null;

    public string? IdempotencyKey => Http.Request.Headers[IdempotencyKeyHeader].ToString() is { Length: > 0 } key ? key : null;

    public DateTimeOffset? MfaStepUpAt => Http.Items[CesioClaims.MfaStepUpAtItem] as DateTimeOffset?;
}
