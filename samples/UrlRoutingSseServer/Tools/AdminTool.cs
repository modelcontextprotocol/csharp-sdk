using ModelContextProtocol.Server;
using System.ComponentModel;

namespace UrlRoutingSseServer.Tools;

[McpServerToolType]
public sealed class AdminTool
{
    [McpServerTool, Description("Gets system status information - admin only.")]
    [McpServerToolRoute("admin")]
    public static string GetSystemStatus()
    {
        return $"System Status: Running | Uptime: {Environment.TickCount64 / 1000}s | Memory: {GC.GetTotalMemory(false) / 1024 / 1024}MB";
    }

    [McpServerTool, Description("Restarts a service - admin only.")]
    [McpServerToolRoute("admin")]
    public static string RestartService([Description("Name of the service to restart")] string serviceName)
    {
        return $"Service '{serviceName}' restart initiated (simulated)";
    }
}