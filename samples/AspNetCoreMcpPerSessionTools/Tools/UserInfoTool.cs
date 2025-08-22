using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpPerSessionTools.Tools;

/// <summary>
/// User information and session-related tools
/// </summary>
[McpServerToolType]
public sealed class UserInfoTool
{
    [McpServerTool, Description("Gets information about the current MCP session.")]
    public static string GetSessionInfo()
    {
        return $"MCP Session Information:\n" +
               $"- Session ID: {Guid.NewGuid():N}[..8] (simulated)\n" +
               $"- Session Start: {DateTime.Now.AddMinutes(-new Random().Next(1, 60)):HH:mm:ss}\n" +
               $"- Protocol Version: MCP 2025-06-18\n" +
               $"- Transport: HTTP";
    }

    [McpServerTool, Description("Gets system information about the server environment.")]
    public static string GetSystemInfo()
    {
        return $"System Information:\n" +
               $"- Server Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC\n" +
               $"- Platform: {Environment.OSVersion.Platform}\n" +
               $"- Runtime: .NET {Environment.Version}\n" +
               $"- Processor Count: {Environment.ProcessorCount}\n" +
               $"- Working Set: {Environment.WorkingSet / (1024 * 1024):F1} MB";
    }

    [McpServerTool, Description("Echoes back the provided message with session context.")]
    public static string EchoWithContext(
        [Description("Message to echo back")] string message)
    {
        return $"[Session Echo] {DateTime.Now:HH:mm:ss}: {message}";
    }

    [Description("Gets basic connection information about the client.")]
    [McpServerTool]
    public static string GetConnectionInfo()
    {
        return $"Connection Information:\n" +
               $"- Connection Type: HTTP MCP Transport\n" +
               $"- Connected At: {DateTime.Now.AddMinutes(-new Random().Next(1, 30)):HH:mm:ss}\n" +
               $"- Status: Active\n" +
               $"- Messages Exchanged: {new Random().Next(1, 100)} (simulated)";
    }
}