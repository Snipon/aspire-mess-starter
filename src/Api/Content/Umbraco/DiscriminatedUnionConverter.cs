using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Content.Umbraco.Delivery;

/// <summary>A oneOf schema whose variant is picked by a discriminator property (generated in DeliveryApiModels.g.cs).</summary>
public interface IDiscriminatedUnion
{
    static abstract string Discriminator { get; }

    /// <summary>The variant type for a discriminator value; an Unknown* variant for values the spec does not know.</summary>
    static abstract Type Resolve(string? value);
}

/// <summary>
/// Deserializes a discriminated union to its variant. Unlike System.Text.Json's [JsonPolymorphic], the discriminator
/// stays readable on the model and unknown values deserialize instead of failing.
/// </summary>
public sealed class DiscriminatedUnionConverter<T> : JsonConverter<T> where T : class, IDiscriminatedUnion
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var value = root.TryGetProperty(T.Discriminator, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        // Variants are derived types, so this does not come back to this converter.
        return (T?)root.Deserialize(T.Resolve(value), options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
