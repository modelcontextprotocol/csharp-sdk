using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Security.Claims;

namespace ProtectedMcpServer.Tools;

[McpServerToolType]
public sealed class IdentityTools(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(
        Name = "who_am_i",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Show the authenticated identity and OAuth client information for this MCP connection.")]
    public string WhoAmI()
    {
        ClaimsPrincipal user = httpContextAccessor.HttpContext?.User
            ?? throw new McpException("The authenticated HTTP context is unavailable.");

        if (user.Identity?.IsAuthenticated is not true)
        {
            throw new McpException("The MCP caller is not authenticated.");
        }

        string name = user.Identity.Name ?? "unknown";
        string subject = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
        string clientId = user.FindFirst("client_id")?.Value ?? "unknown";
        string scopes = user.FindFirst("scope")?.Value ?? "none";

        return $"""
            AUTHENTICATED MCP CALLER
            Name: {name}
            Subject: {subject}
            OAuth client: {clientId}
            Scopes: {scopes}
            """;
    }
}
