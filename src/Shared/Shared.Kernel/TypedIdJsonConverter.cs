using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Kernel;

/// <summary>Serializes every <see cref="ITypedId{TSelf}"/> as its GUID string; empty or malformed GUIDs are rejected.</summary>
public sealed class TypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsValueType && typeToConvert.GetInterfaces().Any(
            type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ITypedId<>) && type.GenericTypeArguments[0] == typeToConvert);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(TypedIdJsonConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class TypedIdJsonConverter<T> : JsonConverter<T>
        where T : struct, ITypedId<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var value) && value != Guid.Empty)
            {
                return T.From(value);
            }

            throw new JsonException($"{typeof(T).Name} must be a non-empty GUID string.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
