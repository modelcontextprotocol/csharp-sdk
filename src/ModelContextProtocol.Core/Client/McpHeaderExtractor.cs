using System.Net.Http.Headers;
using System.Text.Json;
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

        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty(XMcpHeaderProperty, out var headerNameElement))
            {
                continue;
            }

            var headerName = headerNameElement.GetString();
            if (string.IsNullOrEmpty(headerName))
            {
                continue;
            }

            // Look for the corresponding argument value
            if (!arguments.Value.TryGetProperty(property.Name, out var argValue))
            {
                continue;
            }

            // Null values → omit header per SEP
            if (argValue.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var headerValue = ConvertJsonElementToHeaderValue(argValue);
            if (headerValue is not null)
            {
                headers.Add($"{McpHttpHeaders.ParamPrefix}{headerName}", headerValue);
            }
        }
    }

    private static string? ConvertJsonElementToHeaderValue(JsonElement element)
    {
        object? value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        return McpHeaderEncoder.EncodeValue(value);
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

        foreach (var property in properties.EnumerateObject())
        {
            // Skip properties whose schema is not an object (e.g., boolean `true`/`false` schemas)
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty(XMcpHeaderProperty, out var headerNameElement))
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

            // MUST contain only ASCII characters (0x21-0x7E) excluding space and colon
            foreach (char c in headerName!)
            {
                if (c < 0x21 || c > 0x7E || c == ':')
                {
                    rejectionReason = $"Tool '{tool.Name}': x-mcp-header '{headerName}' contains invalid character '{c}' (0x{(int)c:X2}).";
                    return false;
                }
            }

            // MUST be case-insensitively unique
            if (!headerNames.Add(headerName))
            {
                rejectionReason = $"Tool '{tool.Name}': duplicate x-mcp-header name '{headerName}' (case-insensitive).";
                return false;
            }

            // MUST only be applied to primitive types (string, number, boolean)
            if (property.Value.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                var typeName = typeElement.GetString();
                if (typeName is not ("string" or "number" or "integer" or "boolean"))
                {
                    rejectionReason = $"Tool '{tool.Name}': x-mcp-header on property '{property.Name}' has non-primitive type '{typeName}'.";
                    return false;
                }
            }
        }

        return true;
    }
}
