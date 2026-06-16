using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Serializes <see cref="CacheScope"/> caching-scope hints, tolerating unknown or future values on read.
/// </summary>
/// <remarks>
/// <para>
/// SEP-2549 introduces <c>cacheScope</c> as a forward-looking caching hint. If a server sends an
/// unrecognized scope string (for example, a value added in a later revision of the specification) or a
/// non-string token, this converter maps it to <see langword="null"/> rather than throwing. This prevents
/// a single unexpected hint from breaking deserialization of the entire result (for example, the whole
/// tool list). A <see langword="null"/> result is the same as an absent field, which clients treat as
/// <see cref="CacheScope.Public"/>.
/// </para>
/// <para>
/// This converter is applied per-property on the cacheable result types. The <see cref="CacheScope"/>
/// enum itself retains a standard string converter for any standalone serialization.
/// </para>
/// </remarks>
internal sealed class CacheScopeConverter : JsonConverter<CacheScope?>
{
    public override CacheScope? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String)
        {
            string? value = reader.GetString();

            // Match case-insensitively so a non-conforming casing of "private" (a security-relevant hint)
            // is honored rather than falling through to null, which clients would treat as "public" and
            // could cache user-specific data in a shared cache. Genuinely unknown values still map to null.
            if (string.Equals(value, "public", StringComparison.OrdinalIgnoreCase))
            {
                return CacheScope.Public;
            }

            if (string.Equals(value, "private", StringComparison.OrdinalIgnoreCase))
            {
                return CacheScope.Private;
            }

            return null;
        }

        // Any non-string token (number, bool, object, array) is an unrecognized hint. Consume the whole
        // value, including the contents of an object or array, so the reader is left correctly positioned
        // before mapping to null. Skipping is required for container tokens: returning without consuming
        // them would leave the reader mispositioned and break deserialization of the enclosing result.
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, CacheScope? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value switch
        {
            CacheScope.Public => "public",
            CacheScope.Private => "private",
            _ => throw new JsonException($"Unsupported {nameof(CacheScope)} value: {value}."),
        });
    }
}
