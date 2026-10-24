using Shared.Kernel;

namespace Modules.Compliance.Contracts;

/// <summary>A Verified factor authorization was revoked (task 008); INV-A2 consequences follow (tasks 013, 014, 020).</summary>
public sealed record FactorAuthorizationRevoked(Guid FactorOrgId, Guid AuthorizationId) : IIntegrationEvent
{
    public static string EventType => "Compliance.FactorAuthorizationRevoked";
}

/// <summary>A Verified factor authorization passed its re-verification date (task 008); INV-A2 consequences follow.</summary>
public sealed record FactorAuthorizationExpired(Guid FactorOrgId, Guid AuthorizationId) : IIntegrationEvent
{
    public static string EventType => "Compliance.FactorAuthorizationExpired";
}
