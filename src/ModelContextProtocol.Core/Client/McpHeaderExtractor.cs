using System.Net.Http.Headers;
using System.Text.Json;
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
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        return McpHeaderEncoder.EncodeValue(value);
    }
}
