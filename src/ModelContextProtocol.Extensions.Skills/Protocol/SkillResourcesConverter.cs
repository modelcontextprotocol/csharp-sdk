using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Serializes <see cref="SkillResources"/> as either a JSON array of <see cref="SkillResource"/> or the
/// literal string <c>"dynamic"</c>, and rejects every other shape.
/// </summary>
internal sealed class SkillResourcesConverter : JsonConverter<SkillResources>
{
    /// <summary>
    /// Gets a value indicating that this converter is invoked for <c>null</c> tokens.
    /// </summary>
    /// <remarks>
    /// Without this, System.Text.Json assigns <see langword="null"/> directly for a reference type and never
    /// calls <see cref="Read"/>, so <c>"resources": null</c> would be silently accepted. The specification
    /// requires a manifest on every entry and admits only an array or the <c>"dynamic"</c> string, so a null
    /// must be rejected like any other invalid value.
    /// </remarks>
    public override bool HandleNull => true;

    public override SkillResources Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                throw new JsonException(
                    $"Invalid skill manifest: expected an array or the string '{SkillsProtocol.DynamicResourcesSentinel}' but found null.");

            case JsonTokenType.String:
                string? sentinel = reader.GetString();
                if (!string.Equals(sentinel, SkillsProtocol.DynamicResourcesSentinel, StringComparison.Ordinal))
                {
                    throw new JsonException(
                        $"Invalid skill manifest: expected the string '{SkillsProtocol.DynamicResourcesSentinel}' but found '{sentinel}'.");
                }

                return SkillResources.Dynamic;

            case JsonTokenType.StartArray:
                var resources = new List<SkillResource>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return SkillResources.FromResources(resources);
                    }

                    var resource = JsonSerializer.Deserialize(ref reader, McpSkillsJsonContext.Default.SkillResource) ??
                        throw new JsonException("Invalid skill manifest: a resource entry was null.");
                    resources.Add(resource);
                }

                throw new JsonException("Invalid skill manifest: unterminated array.");

            default:
                throw new JsonException(
                    $"Invalid skill manifest: expected an array or the string '{SkillsProtocol.DynamicResourcesSentinel}' but found {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, SkillResources value, JsonSerializerOptions options)
    {
        if (value.IsDynamic)
        {
            writer.WriteStringValue(SkillsProtocol.DynamicResourcesSentinel);
            return;
        }

        writer.WriteStartArray();
        foreach (var resource in value.Resources!)
        {
            JsonSerializer.Serialize(writer, resource, McpSkillsJsonContext.Default.SkillResource);
        }

        writer.WriteEndArray();
    }
}
