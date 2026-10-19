using System.Text.Json;

namespace Shared.Kernel;

public static class KernelJsonConverters
{
    /// <summary>Adds the converters for <see cref="Money"/> and the typed IDs.</summary>
    public static JsonSerializerOptions AddKernelConverters(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new TypedIdJsonConverterFactory());
        return options;
    }
}
