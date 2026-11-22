using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a JSON converter for <see cref="IList{T}"/> that handles both array and single object representations.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SingleItemOrListConverter<T> : JsonConverter<IList<T>>
    where T : class
{
    /// <inheritdoc />
    public override IList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<T> list = [];
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (JsonSerializer.Deserialize(ref reader, options.GetTypeInfo(typeof(T))) is T item)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            return JsonSerializer.Deserialize(ref reader, options.GetTypeInfo(typeof(T))) is T item ? [item] : [];
        }

        throw new JsonException($"Unexpected token type: {reader.TokenType}. Expected StartArray or StartObject.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IList<T> value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;

            // Serialize items as T (not object) so that a converter declared on T, such as
            // ContentBlock.Converter, is used; serializing via object resolves the derived
            // type's default contract, which base64-encodes the already-encoded image/audio data again.
            case { Count: 1 }:
                JsonSerializer.Serialize(writer, value[0], options.GetTypeInfo(typeof(T)));
                return;

            default:
                writer.WriteStartArray();
                foreach (var item in value)
                {
                    JsonSerializer.Serialize(writer, item, options.GetTypeInfo(typeof(T)));
                }
                writer.WriteEndArray();
                return;
        }
    }
}
