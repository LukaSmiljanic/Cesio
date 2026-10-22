using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>A fixed authenticated actor until task 004 adds authentication.</summary>
public sealed class TestRequestContext : IRequestContext
{
    public ActorType ActorType => ActorType.User;

    public Guid? UserId { get; set; } = Guid.NewGuid();

    public Guid? OrganizationId { get; set; } = Guid.NewGuid();

    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "integration-tests";

    public string? DeviceId => null;

    public string? IdempotencyKey { get; set; }

    public DateTimeOffset? MfaStepUpAt => null;
}
