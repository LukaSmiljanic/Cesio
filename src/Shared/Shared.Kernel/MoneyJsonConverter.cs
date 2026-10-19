using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Kernel;

/// <summary>
/// Reads and writes <c>{ "amount": "97000.00", "currency": "RSD" }</c> (api.md §1). A JSON number for the amount is
/// rejected, so money never passes through a floating-point representation.
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const string AmountProperty = "amount";
    private const string CurrencyProperty = "currency";

    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be an object with string 'amount' and 'currency'.");
        }

        string? amount = null;
        string? currencyCode = null;
        // The serializer hands a converter a complete object, so every Read() below succeeds.
        for (reader.Read(); reader.TokenType != JsonTokenType.EndObject; reader.Read())
        {
            var property = reader.GetString();
            reader.Read();
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Money '{property}' must be a JSON string.");
            }

            switch (property)
            {
                case AmountProperty when amount is null:
                    amount = reader.GetString();
                    break;
                case CurrencyProperty when currencyCode is null:
                    currencyCode = reader.GetString();
                    break;
                default:
                    throw new JsonException($"Unexpected or duplicate Money property '{property}'.");
            }
        }

        if (!Currency.TryFromCode(currencyCode, out var currency))
        {
            throw new JsonException("Money 'currency' is missing or not supported.");
        }

        return Money.TryParse(amount, currency, out var money)
            ? money
            : throw new JsonException("Money 'amount' is missing or not a valid decimal string for the currency.");
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString(AmountProperty, value.FormatAmount());
        writer.WriteString(CurrencyProperty, value.Currency.Code);
        writer.WriteEndObject();
    }
}
