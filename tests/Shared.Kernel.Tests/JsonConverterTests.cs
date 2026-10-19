using System.Text.Json;

namespace Shared.Kernel.Tests;

public sealed class JsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddKernelConverters();

    private sealed record Payload(Money Price, DealId Deal);

    [Theory]
    [InlineData("97000.00")]
    [InlineData("97000")]
    [InlineData("0.50")]
    [InlineData("-1.5")]
    public void Money_round_trip_preserves_scale(string amount)
    {
        var money = Money.Parse(amount, Currency.Rsd);

        var json = JsonSerializer.Serialize(money, Options);
        var back = JsonSerializer.Deserialize<Money>(json, Options)!;

        Assert.Equal($$"""{"amount":"{{amount}}","currency":"RSD"}""", json);
        Assert.Equal(amount, back.FormatAmount());
        Assert.Equal(money, back);
    }

    [Fact]
    public void Money_reads_properties_in_any_order() =>
        Assert.Equal(
            Money.Parse("1.00", Currency.Eur),
            JsonSerializer.Deserialize<Money>("""{"currency":"EUR","amount":"1.00"}""", Options));

    [Theory]
    [InlineData("""{"amount":97000.00,"currency":"RSD"}""")]
    [InlineData("""{"amount":"97000.00","currency":1}""")]
    [InlineData("""{"amount":null,"currency":"RSD"}""")]
    [InlineData("""{"amount":"9.7e4","currency":"RSD"}""")]
    [InlineData("""{"amount":"1.001","currency":"RSD"}""")]
    [InlineData("""{"amount":"1.00","currency":"USD"}""")]
    [InlineData("""{"amount":"1.00"}""")]
    [InlineData("""{"currency":"RSD"}""")]
    [InlineData("""{"amount":"1","amount":"2","currency":"RSD"}""")]
    [InlineData("""{"amount":"1","currency":"RSD","currency":"EUR"}""")]
    [InlineData("""{"amount":"1","currency":"RSD","note":"x"}""")]
    [InlineData("""{}""")]
    [InlineData("""97000""")]
    [InlineData("""["97000","RSD"]""")]
    public void Money_rejects_numbers_and_malformed_objects(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json, Options));

    [Fact]
    public void Typed_ids_serialize_as_guid_strings()
    {
        var payload = new Payload(Money.Parse("10.00", Currency.Rsd), DealId.New());

        var json = JsonSerializer.Serialize(payload, Options);
        var back = JsonSerializer.Deserialize<Payload>(json, Options)!;

        Assert.Contains($"\"Deal\":\"{payload.Deal.Value}\"", json, StringComparison.Ordinal);
        Assert.Equal(payload, back);
    }

    [Theory]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void Typed_ids_reject_empty_or_malformed_values(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DealId>(json, Options));

    [Fact]
    public void Typed_id_factory_converts_only_typed_ids()
    {
        var factory = new TypedIdJsonConverterFactory();

        Assert.True(factory.CanConvert(typeof(DealId)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.False(factory.CanConvert(typeof(int)));
        Assert.False(factory.CanConvert(typeof(Guid)));
        Assert.False(factory.CanConvert(typeof(MisdeclaredId)));
        Assert.Throws<ArgumentNullException>(() => factory.CanConvert(null!));
    }

    [Fact]
    public void Converters_reject_null_arguments()
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        Assert.Throws<ArgumentNullException>(() => new MoneyJsonConverter().Write(null!, Money.Zero(Currency.Rsd), Options));
        Assert.Throws<ArgumentNullException>(() => new MoneyJsonConverter().Write(writer, null!, Options));
        Assert.Throws<ArgumentNullException>(() => ((JsonSerializerOptions)null!).AddKernelConverters());
    }

    /// <summary>Implements ITypedId for another type, so it is not itself a typed ID.</summary>
    private readonly struct MisdeclaredId : ITypedId<DealId>
    {
        public Guid Value => Guid.Empty;

        public static DealId New() => DealId.New();

        public static DealId From(Guid value) => DealId.From(value);
    }
}
