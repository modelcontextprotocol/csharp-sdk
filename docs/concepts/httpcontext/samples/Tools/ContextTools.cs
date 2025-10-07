using ModelContextProtocol.Server;
using System.ComponentModel;

namespace HttpContext.Tools;

// <snippet_AccessHttpContext>
public class ContextTools(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(UseStructuredContent = true)]
    [Description("Retrieves the HTTP headers from the current request and returns them as a JSON object.")]
    public object GetHttpHeaders()
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            return "No HTTP context available";
        }

        var headers = new Dictionary<string, string>();
        foreach (var header in context.Request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value.ToArray());
        }

        return headers;
    }
// </snippet_AccessHttpContext>
}
