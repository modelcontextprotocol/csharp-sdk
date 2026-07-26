using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
#if NET
using System.Buffers;
#endif
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Extracts parameter values from tool call arguments and adds them as HTTP headers
/// based on <c>x-mcp-header</c> schema extensions.
/// </summary>
internal static class McpHeaderExtractor
{
    private const string XMcpHeaderProperty = "x-mcp-header";

    /// <summary>
    /// Adds custom parameter headers to an HTTP request based on a tool's schema extensions.
    /// </summary>
    /// <param name="headers">The HTTP request headers to add to.</param>
    /// <param name="tool">The tool definition containing the input schema with x-mcp-header annotations.</param>
    /// <param name="arguments">The arguments being passed to the tool call.</param>
    public static void AddParameterHeaders(
        HttpRequestHeaders headers,
        Tool tool,
        JsonElement? arguments)
    {
        if (!arguments.HasValue || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddParameterHeadersFromProperties(headers, properties, arguments.Value);
    }

    /// <summary>
    /// Recursively extracts parameter values from properties at any nesting depth
    /// and adds them as HTTP headers.
    /// </summary>
    private static void AddParameterHeadersFromProperties(
        HttpRequestHeaders headers,
        JsonElement properties,
        JsonElement arguments)
    {
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Recurse into nested object properties
            if (property.Value.TryGetProperty("properties", out var nestedProperties) &&
                nestedProperties.ValueKind == JsonValueKind.Object &&
                arguments.TryGetProperty(property.Name, out var nestedArgs) &&
                nestedArgs.ValueKind == JsonValueKind.Object)
            {
                AddParameterHeadersFromProperties(headers, nestedProperties, nestedArgs);
            }

            if (!property.Value.TryGetProperty(XMcpHeaderProperty, out var headerNameElement))
            {
                continue;
            }

            var headerName = headerNameElement.GetString();
            if (string.IsNullOrEmpty(headerName))
            {
                continue;
            }

            // Look for the corresponding argument value
            if (!arguments.TryGetProperty(property.Name, out var argValue))
            {
                continue;
            }

            // Null values → omit header per SEP
            if (argValue.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var headerValue = ConvertArgumentToHeaderValue(property.Value, property.Name, argValue);
            if (headerValue is not null)
            {
                headers.Add($"{McpHttpHeaders.ParamPrefix}{headerName}", headerValue);
            }
        }
    }

    // The maximum magnitude for an integer that can be represented exactly by an IEEE 754
    // double-precision value (2^53 - 1). Per SEP-2243 integer x-mcp-header values MUST be within
    // the JavaScript safe integer range (-2^53+1 to 2^53-1) so intermediaries can compare them.
    private const long MaxSafeInteger = 9007199254740991L;

    /// <summary>
    /// Converts an argument value to its encoded header representation. When the property schema
    /// declares an <c>integer</c> type, the value is canonicalized to its decimal string form
    /// (e.g. a body value of <c>42.0</c> is emitted as <c>"42"</c>) per SEP-2243.
    /// </summary>
    private static string? ConvertArgumentToHeaderValue(JsonElement propertySchema, string propertyName, JsonElement argValue)
    {
        if (argValue.ValueKind == JsonValueKind.Number && SchemaTypeIsInteger(propertySchema))
        {
            if (!TryGetCanonicalSafeInteger(argValue, out long canonical))
            {
                throw new McpException(
                    $"The value '{argValue.GetRawText()}' for parameter '{propertyName}' annotated with x-mcp-header " +
                    $"is not a whole number within the JavaScript safe integer range (-{MaxSafeInteger} to {MaxSafeInteger}).");
            }

            return McpHeaderEncoder.EncodeValue(canonical);
        }

        return McpHeaderEncoder.ConvertToHeaderValue(argValue);
    }

    /// <summary>
    /// Determines whether the property schema's <c>type</c> keyword declares an <c>integer</c> type,
    /// either directly or as a member of a JSON Schema union array (e.g. <c>["integer", "null"]</c>).
    /// </summary>
    private static bool SchemaTypeIsInteger(JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return false;
        }

        switch (typeElement.ValueKind)
        {
            case JsonValueKind.String:
                return typeElement.ValueEquals("integer");

            case JsonValueKind.Array:
                foreach (var entry in typeElement.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String && entry.ValueEquals("integer"))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to interpret a JSON number as a whole integer within the JavaScript safe integer
    /// range. Decimal and exponent forms whose fractional part is zero (e.g. <c>42.0</c>, <c>4.2e1</c>)
    /// are accepted; non-integers and out-of-range values are rejected.
    /// </summary>
    private static bool TryGetCanonicalSafeInteger(JsonElement element, out long value)
    {
        if (element.TryGetInt64(out value))
        {
            return value >= -MaxSafeInteger && value <= MaxSafeInteger;
        }

        // Handle decimal/exponent representations of whole numbers such as "42.0" or "4.2e1".
        // long.TryParse inspects the actual digits (so non-integers such as "42.5" are rejected
        // without rounding) and fails fast on overflow (no large-number allocation).
        const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (long.TryParse(element.GetRawText(), Styles, CultureInfo.InvariantCulture, out long parsed) &&
            parsed >= -MaxSafeInteger && parsed <= MaxSafeInteger)
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Validates a tool's <c>inputSchema</c> for valid <c>x-mcp-header</c> annotations.
    /// Returns <see langword="true"/> if the tool is valid; <see langword="false"/> with a reason if it should be rejected.
    /// </summary>
    internal static bool ValidateToolSchema(Tool tool, out string? rejectionReason)
    {
        rejectionReason = null;

        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ValidateProperties(tool, properties, headerNames, out rejectionReason);
    }

    /// <summary>
    /// Recursively validates properties at any nesting depth for valid <c>x-mcp-header</c> annotations.
    /// </summary>
    private static bool ValidateProperties(Tool tool, JsonElement properties, HashSet<string> headerNames, out string? rejectionReason)
    {
        rejectionReason = null;

        foreach (var property in properties.EnumerateObject())
        {
            // Skip properties whose schema is not an object (e.g., boolean `true`/`false` schemas)
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Recurse into nested object properties
            if (property.Value.TryGetProperty("properties", out var nestedProperties) &&
                nestedProperties.ValueKind == JsonValueKind.Object)
            {
                if (!ValidateProperties(tool, nestedProperties, headerNames, out rejectionReason))
                {
                    return false;
                }
            }

            if (!property.Value.TryGetProperty(XMcpHeaderProperty, out var headerNameElement))
            {
                continue;
            }

            // x-mcp-header value must be a string
            if (headerNameElement.ValueKind != JsonValueKind.String)
            {
                rejectionReason = $"Tool '{tool.Name}': x-mcp-header on property '{property.Name}' is not a string.";
                return false;
            }

            var headerName = headerNameElement.GetString();

            // MUST NOT be empty
            if (string.IsNullOrEmpty(headerName))
            {
                rejectionReason = $"Tool '{tool.Name}': x-mcp-header on property '{property.Name}' is empty.";
                return false;
            }

            // MUST match HTTP field-name token syntax (1*tchar, RFC 9110 Section 5.1)
            // MUST NOT contain control characters including CR and LF
            int invalidIdx = FindFirstNonTchar(headerName!);
            if (invalidIdx >= 0)
            {
                char c = headerName![invalidIdx];
                rejectionReason = $"Tool '{tool.Name}': x-mcp-header '{headerName}' contains invalid character '{c}' (0x{(int)c:X2}).";
                return false;
            }

            // MUST be case-insensitively unique
            if (!headerNames.Add(headerName!))
            {
                rejectionReason = $"Tool '{tool.Name}': duplicate x-mcp-header name '{headerName}' (case-insensitive).";
                return false;
            }

            // MUST only be applied to parameters with primitive types (string, integer, boolean).
            // Parameters with type "number" (or any other non-primitive type) are not permitted.
            // The "type" keyword may be omitted (treated as unknown, not rejected, since many valid
            // schemas constrain the value via enum/const/$ref instead) or expressed as a JSON Schema
            // union array such as ["string", "null"]; only an explicitly disallowed or malformed type
            // causes rejection.
            if (property.Value.TryGetProperty("type", out var typeElement) &&
                !IsAllowedHeaderType(typeElement))
            {
                rejectionReason = $"Tool '{tool.Name}': x-mcp-header on property '{property.Name}' has unsupported type '{typeElement}'. Only 'string', 'integer', and 'boolean' are allowed.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a JSON Schema <c>type</c> keyword is compatible with <c>x-mcp-header</c>,
    /// which per SEP-2243 may only be applied to <c>string</c>, <c>integer</c>, or <c>boolean</c>
    /// parameters. A union array (e.g., <c>["string", "null"]</c>) is allowed as long as it contains
    /// at least one allowed primitive; <c>"null"</c> is tolerated only as an additional union member.
    /// Any other shape (a disallowed type name, a non-string array element, an empty array, or a
    /// non-string/non-array value) is treated as incompatible.
    /// </summary>
    private static bool IsAllowedHeaderType(JsonElement typeElement)
    {
        switch (typeElement.ValueKind)
        {
            case JsonValueKind.String:
                return IsAllowedPrimitiveTypeName(typeElement.GetString());

            case JsonValueKind.Array:
                bool hasAllowedPrimitive = false;
                foreach (var entry in typeElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    var entryName = entry.GetString();
                    if (entryName == "null")
                    {
                        continue;
                    }

                    if (!IsAllowedPrimitiveTypeName(entryName))
                    {
                        return false;
                    }

                    hasAllowedPrimitive = true;
                }

                return hasAllowedPrimitive;

            default:
                // A "type" that is present but is neither a string nor an array of strings is malformed.
                return false;
        }
    }

    private static bool IsAllowedPrimitiveTypeName(string? typeName) =>
        typeName is "string" or "integer" or "boolean";

    // Valid HTTP token characters (tchar) per RFC 9110 Section 5.6.2:
    // tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
    //         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    private const string TcharChars = "!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

#if NET
    private static readonly SearchValues<char> s_tcharValues = SearchValues.Create(TcharChars);

    internal static int FindFirstNonTchar(string value) =>
        value.AsSpan().IndexOfAnyExcept(s_tcharValues);
#else
    // Bitmap for O(1) tchar lookup. All valid chars are in 0x21-0x7E range,
    // so two ulongs (128 bits) cover the entire ASCII range.
    // _tcharBitmapLo covers chars 0-63, _tcharBitmapHi covers chars 64-127.
    private static readonly ulong s_tcharBitmapLo = ComputeBitmapLo();
    private static readonly ulong s_tcharBitmapHi = ComputeBitmapHi();

    private static ulong ComputeBitmapLo()
    {
        ulong bitmap = 0;
        foreach (char c in TcharChars)
        {
            if (c < 64)
            {
                bitmap |= 1UL << c;
            }
        }
        return bitmap;
    }

    private static ulong ComputeBitmapHi()
    {
        ulong bitmap = 0;
        foreach (char c in TcharChars)
        {
            if (c >= 64)
            {
                bitmap |= 1UL << (c - 64);
            }
        }
        return bitmap;
    }

    private static bool IsTchar(char c)
    {
        if (c >= 128)
        {
            return false;
        }

        return c < 64
            ? (s_tcharBitmapLo & (1UL << c)) != 0
            : (s_tcharBitmapHi & (1UL << (c - 64))) != 0;
    }

    internal static int FindFirstNonTchar(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!IsTchar(value[i]))
            {
                return i;
            }
        }
        return -1;
    }
#endif
}
