namespace Shared.Infrastructure.Requests;

/// <summary>Claims and per-request items written by authentication (Identity module) and read by <see cref="HttpRequestContext"/>.</summary>
public static class CesioClaims
{
    public const string UserId = "sub";

    public const string SessionId = "sid";

    /// <summary>Authentication method reference; <see cref="Mfa"/> once the second factor was verified.</summary>
    public const string AuthenticationMethod = "amr";

    public const string Mfa = "mfa";

    /// <summary><c>HttpContext.Items</c> key holding the session's last step-up time (<see cref="DateTimeOffset"/>).</summary>
    public const string MfaStepUpAtItem = "cesio.mfa_step_up_at";
}
