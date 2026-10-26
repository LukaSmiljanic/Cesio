using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Platform.Api.Tests;

public sealed class ApiSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Liveness_returns_ok()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_unavailable_without_a_database()
    {
        // An unreachable server, so a developer's user secrets with a running local database do not change the result.
        using var client = factory.WithWebHostBuilder(host => host.UseSetting("ConnectionStrings:Cesio", "Server=127.0.0.1,1;Database=none;Connect Timeout=1;TrustServerCertificate=True"))
            .CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
