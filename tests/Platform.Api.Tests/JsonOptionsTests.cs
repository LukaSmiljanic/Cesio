using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Kernel;

namespace Platform.Api.Tests;

public sealed class JsonOptionsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Http_json_options_serialize_money_with_a_string_amount()
    {
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        var json = JsonSerializer.Serialize(Money.Parse("97000.00", Currency.Rsd), options);

        Assert.Equal("""{"amount":"97000.00","currency":"RSD"}""", json);
    }
}
