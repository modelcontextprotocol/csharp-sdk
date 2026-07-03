using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Encodes and decodes parameter values for use in MCP HTTP headers according to the
/// HTTP Standardization SEP.
/// </summary>
/// <remarks>
/// <para>
/// This encoder handles conversion of parameter values to HTTP header-safe strings,
/// including Base64 encoding for values that cannot be safely transmitted as plain text.
/// </para>
/// <para>
/// Per SEP-2243 only primitive parameter types are supported: <c>string</c>, <c>integer</c>, and
/// <c>boolean</c>. The JSON Schema <c>number</c> type is not permitted, and integer values must be
/// within the JavaScript safe integer range (−2^53+1 to 2^53−1).
/// </para>
/// <para>
/// Encoding rules:
/// <list type="bullet">
/// <item><description>Plain ASCII values (0x20-0x7E): sent as-is</description></item>
/// <item><description>Values with leading/trailing whitespace: Base64 encoded with <c>=?base64?{value}?=</c> wrapper</description></item>
/// <item><description>Non-ASCII characters: Base64 encoded</description></item>
/// <item><description>Control characters: Base64 encoded</description></item>
/// <item><description>Plain ASCII values that themselves match the <c>=?base64?...?=</c> sentinel pattern: Base64 encoded to avoid ambiguity</description></item>
/// </list>
/// </para>
/// </remarks>
public static class McpHeaderEncoder
{
    private const string Base64Prefix = "=?base64?";
    private const string Base64Suffix = "?=";

    // Strict UTF-8 decoder that throws on invalid byte sequences rather than silently substituting
    // U+FFFD replacement characters, so a malformed Base64-wrapped header value is rejected.
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes a string parameter value for use in an HTTP header.
    /// </summary>
    /// <param name="value">The string value to encode.</param>
    /// <returns>
    /// The encoded header value, or <see langword="null"/> if <paramref name="value"/> is <see langword="null"/>.
    /// </returns>
    public static string? EncodeValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (RequiresBase64Encoding(value))
        {
            return EncodeAsBase64(value);
        }

        return value;
    }

    /// <summary>
    /// Encodes a boolean parameter value for use in an HTTP header.
    /// </summary>
    /// <param name="value">The boolean value to encode.</param>
    /// <returns>The encoded header value (<c>"true"</c> or <c>"false"</c>).</returns>
    public static string EncodeValue(bool value) => value ? "true" : "false";

    /// <summary>
    /// Encodes an integer parameter value for use in an HTTP header.
    /// </summary>
    /// <param name="value">The integer value to encode.</param>
    /// <returns>The decimal string representation of the value.</returns>
    public static string EncodeValue(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Encodes a parameter value for use in an HTTP header.
    /// </summary>
    /// <param name="value">The value to encode. Supported types are <c>string</c>, integer, and <c>boolean</c>.</param>
    /// <returns>
    /// The encoded header value, or <see langword="null"/> if the value is <see langword="null"/>
    /// or is not a supported type (string, integer, or boolean).
    /// </returns>
    public static string? EncodeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        // Route to typed overloads for known types
        if (value is string s)
        {
            return EncodeValue(s);
        }

        if (value is bool b)
        {
            return EncodeValue(b);
        }

        var stringValue = ConvertToString(value);
        if (stringValue is null)
        {
            return null;
        }

        return stringValue;
    }

    /// <summary>
    /// Decodes a header value that may be Base64-encoded according to SEP rules.
    /// </summary>
    /// <param name="headerValue">The header value to decode.</param>
    /// <returns>
    /// The decoded string value, or <see langword="null"/> if decoding fails.
    /// If the value is not Base64-encoded, returns the original value.
    /// </returns>
    public static string? DecodeValue(string? headerValue)
    {
        if (headerValue is null || headerValue.Length == 0)
        {
            return headerValue;
        }

        // Check for Base64 wrapper. The spec requires the sentinel markers to be
        // case-sensitive and exactly lowercase per SEP-2243.
        if (headerValue.StartsWith(Base64Prefix, StringComparison.Ordinal) &&
            headerValue.EndsWith(Base64Suffix, StringComparison.Ordinal))
        {
            var base64Content = headerValue.Substring(
                Base64Prefix.Length,
                headerValue.Length - Base64Prefix.Length - Base64Suffix.Length);

            try
            {
                var bytes = Convert.FromBase64String(base64Content);
                return s_strictUtf8.GetString(bytes);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        return headerValue;
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> value to an encoded header value string.
    /// </summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>The encoded header value, or <see langword="null"/> if the element is not a supported primitive type.</returns>
    public static string? ConvertToHeaderValue(JsonElement element)
    {
        object? value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        return EncodeValue(value);
    }

    /// <summary>
    /// Converts a <see cref="JsonNode"/> value to an encoded header value string.
    /// </summary>
    /// <param name="node">The JSON node to convert.</param>
    /// <returns>The encoded header value, or <see langword="null"/> if the node is not a <see cref="JsonValue"/> or is not a supported primitive type.</returns>
    public static string? ConvertToHeaderValue(JsonNode node)
    {
        if (node is not JsonValue jsonValue)
        {
            return null;
        }

        object? value = jsonValue.GetValueKind() switch
        {
            JsonValueKind.String => jsonValue.GetValue<string>(),
            JsonValueKind.Number => jsonValue.ToJsonString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        return EncodeValue(value);
    }

    private static string? ConvertToString(object value)
    {
        return value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            byte n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sbyte n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            short n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ulong n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static bool RequiresBase64Encoding(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // Check for leading/trailing whitespace (space or tab)
        if (value[0] is ' ' or '\t' || value[^1] is ' ' or '\t')
        {
            return true;
        }

        // Avoid sentinel collision: if the value matches the base64 wrapper pattern,
        // it must be encoded to prevent ambiguity during decoding.
        if (value.StartsWith(Base64Prefix, StringComparison.Ordinal) &&
            value.EndsWith(Base64Suffix, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (char c in value)
        {
            // Valid HTTP header field value characters per SEP: visible ASCII (0x21-0x7E) and space (0x20).
            // All control characters (0x00-0x1F, 0x7F), including tab, must be Base64-encoded.
            if (c < 0x20 || c > 0x7E)
            {
                return true;
            }
        }

        return false;
    }

    private static string EncodeAsBase64(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var base64 = Convert.ToBase64String(bytes);
        return $"{Base64Prefix}{base64}{Base64Suffix}";
    }
}
