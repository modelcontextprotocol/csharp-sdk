using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServerPerUserTools.Tools;

/// <summary>
/// Public tools available to all users (no permission required)
/// </summary>
[McpServerToolType]
public sealed class PublicTool
{
    [McpServerTool, Description("Echoes the input back to the client. Available to all users.")]
    public static string Echo(string message)
    {
        return "Echo: " + message;
    }

    [McpServerTool, Description("Gets the current server time. Available to all users.")]
    public static string GetTime()
    {
        return $"Current server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC";
    }
}