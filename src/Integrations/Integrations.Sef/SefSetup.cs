using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Integrations.Sef;

public static class SefSetup
{
    /// <summary>The DEMO environment; production sets <c>Sef:BaseUrl</c> to https://efaktura.mfin.gov.rs/ (integrations.md §1).</summary>
    public const string DemoBaseUrl = "https://demoefaktura.mfin.gov.rs/";

    /// <summary>The fake must never answer for a real environment: local development only (not staging).</summary>
    private static readonly string[] FakeEnvironments = ["Development", "local", "dev"];

    /// <summary>The HTTP client with retries, or the in-memory fake when <c>Sef:Client = Fake</c> (development only).</summary>
    public static IServiceCollection AddSef(this IServiceCollection services)
    {
        services.AddTransient<SefRetryHandler>();
        services.AddHttpClient<HttpSefClient>((provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IConfiguration>()["Sef:BaseUrl"] ?? DemoBaseUrl);
                // Each attempt has its own 10 s timeout in the retry handler; this bounds all attempts together.
                client.Timeout = TimeSpan.FromSeconds(60);
                client.MaxResponseContentBufferSize = 25 * 1024 * 1024;
            })
            .AddHttpMessageHandler<SefRetryHandler>();
        services.AddSingleton<FakeSefClient>();
        services.AddScoped<ISefClient>(provider =>
        {
            if (provider.GetRequiredService<IConfiguration>()["Sef:Client"] != "Fake")
            {
                return provider.GetRequiredService<HttpSefClient>();
            }

            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return FakeEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? provider.GetRequiredService<FakeSefClient>()
                : throw new InvalidOperationException($"The fake SEF client must not run in the '{environment}' environment.");
        });
        return services;
    }
}
