using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Requests;

public static class StepUp
{
    /// <summary>How recent a step-up must be for protected actions (security.md §1).</summary>
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(5);

    public const string ErrorCode = "step_up_required";

    /// <summary>
    /// Returns 401 <c>step_up_required</c> unless the session re-verified MFA within <see cref="Validity"/>
    /// (task 004; the session's step-up time is set by the Identity module's session validation).
    /// </summary>
    public static TBuilder RequireStepUp<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            return http.Items[CesioClaims.MfaStepUpAtItem] is DateTimeOffset stepUpAt && now - stepUpAt <= Validity
                ? await next(context)
                : Results.Problem(
                    detail: "Confirm your authenticator code to continue.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCode });
        });
}
