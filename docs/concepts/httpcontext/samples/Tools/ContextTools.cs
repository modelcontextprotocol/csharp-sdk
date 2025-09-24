using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace HttpContext.Tools;


// <snippet_AccessHttpContext>
public class ContextTools(IHttpContextAccessor _httpContextAccessor)
{
    [McpServerTool(UseStructuredContent = true)]
    [Description("Retrieves the HTTP headers from the current request and returns them as a JSON object.")]
    public object GetHttpHeaders()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return "No HTTP context available";
        }

        // Remainder of GetHttpHeaders method follows
        // </snippet_AccessHttpContext>

        var headers = new Dictionary<string, string>();
        foreach (var header in context.Request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value.ToArray());
        }

        return headers;
    }
}
