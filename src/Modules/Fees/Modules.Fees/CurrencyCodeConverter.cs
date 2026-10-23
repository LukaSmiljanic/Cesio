using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Kernel;

namespace Modules.Fees;

/// <summary>A currency as its ISO code ("RSD") in a fee schedule definition.</summary>
internal sealed class CurrencyCodeConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Currency.TryFromCode(reader.GetString(), out var currency) ? currency : throw new JsonException("Unknown currency code.");

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Code);
    }
}
