using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.Identity.Passwords;
using Modules.Identity.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Identity;

/// <summary>Composition entry point of the Identity module; the hosts call it to register the module's services.</summary>
public static class IdentityModule
{
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AuthDbContext>(AuthDbContext.SchemaName, migrationOrder: 2);
        services.AddOptions<AuthOptions>().BindConfiguration("Auth");
        services.AddOptions<BreachCheckOptions>().BindConfiguration("Auth:BreachCheck");
        services.AddHttpClient<IBreachedPasswordChecker, HibpBreachedPasswordChecker>(client =>
        {
            client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            client.Timeout = TimeSpan.FromSeconds(3);
        });

        services.AddIdentityCore<AuthUser>(options =>
            {
                // security.md §1: length and the breached-password check matter, composition rules do not.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<BreachedPasswordValidator>();
        services.AddDataProtection();

        services.AddAuthentication(AuthSchemes.Session)
            .AddCookie(AuthSchemes.Session, options =>
            {
                Harden(options, "__Host-cesio");
                options.ExpireTimeSpan = SessionPolicy.Absolute;
                options.Events.OnValidatePrincipal = SessionStore.ValidateAsync;
            })
            .AddCookie(AuthSchemes.Pending, options =>
            {
                Harden(options, "__Host-cesio-pending");
                options.ExpireTimeSpan = SessionPolicy.Pending;
            });

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(AuthSchemes.Session)
                .RequireAuthenticatedUser()
                .RequireClaim(CesioClaims.AuthenticationMethod, CesioClaims.Mfa)
                .Build())
            .AddPolicy(AuthEndpoints.PendingPolicy, policy => policy.AddAuthenticationSchemes(AuthSchemes.Pending).RequireAuthenticatedUser());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeader;
            options.Cookie.Name = "__Host-cesio-af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.RateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        services.AddScoped<SessionStore>();
        services.AddScoped<AuthService>();
        return services;
    }

    /// <summary>Authentication, the anti-forgery check on unsafe methods, authorization and rate limiting, in that order.</summary>
    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.Use(ValidateAntiforgeryAsync);
        app.UseAuthorization();
        app.UseRateLimiter();
        return app;
    }

    private static async Task ValidateAntiforgeryAsync(HttpContext http, RequestDelegate next)
    {
        var method = http.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)
            || !http.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
        {
            await next(http);
            return;
        }

        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem(
                    detail: $"The {AntiforgeryHeader} header is missing or invalid.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "csrf_failed" })
                .ExecuteAsync(http);
            return;
        }

        await next(http);
    }

    private static void Harden(CookieAuthenticationOptions options, string name)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
