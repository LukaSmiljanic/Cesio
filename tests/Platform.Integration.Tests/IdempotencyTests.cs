using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

public sealed class IdempotencyTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);
    private readonly CommandExecutions _executions = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration[$"ConnectionStrings:{PersistenceSetup.ConnectionStringName}"] = _database.ConnectionString;
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<IRequestContext>(provider => new HeaderRequestContext(
            provider.GetRequiredService<IHttpContextAccessor>(), _database.RequestContext));
        builder.Services.AddSingleton(_executions);
        _database.AddServices(builder.Services);

        _app = builder.Build();
        _app.UseIdempotency();
        _app.MapPost("/orders", async (SampleDbContext sample, CommandExecutions executions, CancellationToken ct) =>
        {
            var order = new SampleOrder { Id = Guid.NewGuid() };
            sample.Orders.Add(order);
            await sample.SaveChangesAsync(ct);
            executions.Record();
            return Results.Created($"/orders/{order.Id}", new { id = order.Id });
        }).RequireIdempotency();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_missing_key_is_rejected()
    {
        using var response = await _client.PostAsJsonAsync("/orders", new { name = "a" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_replay_returns_the_stored_response_without_running_again()
    {
        var key = Guid.NewGuid().ToString();
        var before = _executions.Count;

        using var first = await PostAsync(key, "a");
        using var second = await PostAsync(key, "a");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(before + 1, _executions.Count);
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_rejected()
    {
        var key = Guid.NewGuid().ToString();

        using var first = await PostAsync(key, "a");
        using var second = await PostAsync(key, "b");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task Concurrent_duplicates_execute_once()
    {
        var key = Guid.NewGuid().ToString();
        var before = _executions.Count;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(key, "a")));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            response.Dispose();
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Equal(before + 1, _executions.Count);
    }

    private Task<HttpResponseMessage> PostAsync(string key, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(new { name }) };
        request.Headers.Add(HttpRequestContext.IdempotencyKeyHeader, key);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The fixed test actor, with the idempotency key read from the request header.</summary>
    private sealed class HeaderRequestContext(IHttpContextAccessor accessor, TestRequestContext actor) : IRequestContext
    {
        private readonly HttpRequestContext _http = new(accessor);

        public ActorType ActorType => actor.ActorType;

        public Guid? UserId => actor.UserId;

        public Guid? OrganizationId => actor.OrganizationId;

        public string CorrelationId => _http.CorrelationId;

        public string? IpAddress => _http.IpAddress;

        public string? UserAgent => _http.UserAgent;

        public string? DeviceId => null;

        public string? IdempotencyKey => _http.IdempotencyKey;

        public DateTimeOffset? MfaStepUpAt => null;
    }
}

public sealed class CommandExecutions
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Record() => Interlocked.Increment(ref _count);
}
