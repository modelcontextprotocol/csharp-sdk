using System.Net.Http.Headers;
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

            var headerValue = McpHeaderEncoder.ConvertToHeaderValue(argValue);
            if (headerValue is not null)
            {
                headers.Add($"{McpHttpHeaders.ParamPrefix}{headerName}", headerValue);
            }
        }
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

            // MUST only be applied to primitive types (integer, string, boolean).
            // Parameters with type "number" are not permitted.
            if (property.Value.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                var typeName = typeElement.GetString();
                if (typeName is not ("string" or "integer" or "boolean"))
                {
                    rejectionReason = $"Tool '{tool.Name}': x-mcp-header on property '{property.Name}' has non-primitive type '{typeName}'.";
                    return false;
                }
            }
        }

        return true;
    }

    // Valid HTTP token characters (tchar) per RFC 9110 Section 5.6.2:
    // tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
    //         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    private const string TcharChars = "!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

#if NET
    private static readonly SearchValues<char> s_tcharValues = SearchValues.Create(TcharChars);

    /// <summary>
    /// Returns <see langword="true"/> if every character in <paramref name="value"/> is a valid
    /// HTTP token character (tchar) per RFC 9110 Section 5.6.2.
    /// </summary>
    private static bool IsValidTcharString(string value) =>
        value.AsSpan().IndexOfAnyExcept(s_tcharValues) < 0;

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

    private static bool IsValidTcharString(string value)
    {
        foreach (char c in value)
        {
            if (!IsTchar(c))
            {
                return false;
            }
        }
        return true;
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
