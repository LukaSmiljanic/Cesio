using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Identity;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>Platform.Api on a test database, with captured emails, no external breach API and no rate limit.</summary>
public sealed class ApiApp(TestDatabase database) : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
    {
        host.UseSetting($"ConnectionStrings:{PersistenceSetup.ConnectionStringName}", database.ConnectionString);
        host.UseSetting("Auth:BreachCheck:UseOnlineApi", "false");
        host.UseSetting("Auth:RateLimitPerMinute", "100000");
        host.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(database.Emails);
        });
    });

    /// <summary>A browser: its own cookie jar, over HTTPS so the Secure cookies are sent.</summary>
    public ApiClient NewClient() =>
        new(_factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }), database.Emails);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>A query value from the last link sent to <paramref name="to"/>.</summary>
    public string LinkValue(string to, string name)
    {
        var body = _sent.Last(m => m.To == to).Body;
        var match = Regex.Match(body, $@"[?&]{name}=([^&\s]+)");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }
}

/// <summary>Calls the API like the SPA does: anti-forgery token before every unsafe request, JSON bodies.</summary>
public sealed class ApiClient(HttpClient http, CapturingEmailSender emails) : IDisposable
{
    public const string Password = "correct horse battery staple 42";

    public string? SharedKey { get; private set; }

    public HttpClient Http => http;

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await AntiforgeryTokenAsync());
        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add(IdentityModule.AntiforgeryHeader, await AntiforgeryTokenAsync());
        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>Register, confirm the email and sign in with the password (pending MFA).</summary>
    public async Task<string> RegisterAndLoginAsync(string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.rs";
        Expect(HttpStatusCode.Accepted, await PostAsync("/api/v1/auth/register", new { email, password = Password }));
        Expect(HttpStatusCode.NoContent, await PostAsync("/api/v1/auth/confirm-email", new
        {
            userId = Guid.Parse(emails.LinkValue(email, "userId")),
            token = emails.LinkValue(email, "token"),
        }));
        Expect(HttpStatusCode.OK, await PostAsync("/api/v1/auth/login", new { email, password = Password }));
        return email;
    }

    /// <summary>A fully signed-in user with TOTP enrolled; returns the email and the recovery codes.</summary>
    public async Task<(string Email, string[] RecoveryCodes)> SignUpAsync()
    {
        var email = await RegisterAndLoginAsync();
        var start = await ReadAsync(await PostAsync("/api/v1/auth/mfa/enrol/start"));
        SharedKey = start.GetProperty("sharedKey").GetString();
        var confirmed = await ReadAsync(await PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code = Totp.Code(SharedKey!) }));
        return (email, confirmed.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray());
    }

    /// <summary>Signs in an existing user on this client (password + TOTP).</summary>
    public async Task SignInAsync(string email, string sharedKey)
    {
        SharedKey = sharedKey;
        Expect(HttpStatusCode.OK, await PostAsync("/api/v1/auth/login", new { email, password = Password }));
        Expect(HttpStatusCode.NoContent, await PostAsync("/api/v1/auth/mfa/verify", new { code = Totp.Code(sharedKey, 1) }));
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
            return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        }
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            return problem.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
        }
    }

    public void Dispose() => http.Dispose();

    private static void Expect(HttpStatusCode expected, HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
        }
    }

    private async Task<string> AntiforgeryTokenAsync()
    {
        var token = await ReadAsync(await GetAsync("/api/v1/auth/antiforgery"));
        return token.GetProperty("token").GetString()!;
    }
}
