using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace HttpContext.Tools;

// <snippet_ConstructorParameter>
public class ContextTools
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ContextTools(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // remainder of ContextTools follows
    // </snippet_ConstructorParameter>

    // <snippet_AccessHttpContext>
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

    [McpServerTool(UseStructuredContent = true)]
    [Description("Retrieves the request information from the current HTTP context and returns it as structured content.")]
    public object GetRequestInfo()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return new { Error = "No HTTP context available" };
        }

        var requestInfo = new
        {
            context.Request.Method,
            Path = context.Request.Path.Value,
            QueryString = context.Request.QueryString.Value,
            context.Request.ContentType,
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString(),
            context.Request.IsHttps
        };

        return requestInfo;
    }

    [McpServerTool(UseStructuredContent = true)]
    [Description("Retrieves the user claims from the current HTTP context and returns them as a JSON object.")]
    public object GetUserClaims()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return "No HTTP context available";
        }

        var claims = context.User.Claims.Select(c => new
        {
            c.Type,
            c.Value
        }).ToList();

        return claims;
    }
}
