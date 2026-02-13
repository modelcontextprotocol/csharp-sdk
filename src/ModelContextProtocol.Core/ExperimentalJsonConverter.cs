using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol;

/// <summary>
/// A JSON converter that handles serialization of experimental MCP types through <c>object?</c> backing fields.
/// </summary>
/// <typeparam name="T">The experimental type to serialize/deserialize.</typeparam>
/// <remarks>
/// <para>
/// This converter is used on <c>object?</c> backing fields that shadow public experimental properties
/// marked with <see cref="ExperimentalAttribute"/>. By declaring the backing field
/// as <c>object?</c>, the System.Text.Json source generator does not walk the experimental type graph.
/// </para>
/// <para>
/// Serialization delegates to <see cref="McpJsonUtilities.DefaultOptions"/>, which already contains source-generated
/// contracts for all experimental types.
/// </para>
/// <para>
/// This type is not intended to be used directly. It supports the MCP infrastructure and is subject to change.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExperimentalJsonConverter<T> : JsonConverter<object?> where T : class
{
    private static JsonTypeInfo<T> TypeInfo => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    /// <inheritdoc/>
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return JsonSerializer.Deserialize(ref reader, TypeInfo);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value is not T typed)
        {
            throw new JsonException($"Expected value of type '{typeof(T).Name}' but got '{value.GetType().Name}'.");
        }

        JsonSerializer.Serialize(writer, typed, TypeInfo);
    }
}
