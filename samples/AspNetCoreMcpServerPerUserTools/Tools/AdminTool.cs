using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServerPerUserTools.Tools;

/// <summary>
/// Administrative tools available only to admin users
/// </summary>
[McpServerToolType]
public sealed class AdminTool
{
    [McpServerTool, Description("Gets system status information. Requires admin privileges.")]
    public static string GetSystemStatus()
    {
        var uptime = Environment.TickCount64;
        var memoryUsage = GC.GetTotalMemory(false);
        
        return $"System Status:\n" +
               $"- Uptime: {TimeSpan.FromMilliseconds(uptime):dd\\.hh\\:mm\\:ss}\n" +
               $"- Memory Usage: {memoryUsage / (1024 * 1024):F2} MB\n" +
               $"- Processor Count: {Environment.ProcessorCount}\n" +
               $"- OS Version: {Environment.OSVersion}";
    }

    [McpServerTool, Description("Manages server configuration. Requires admin privileges.")]
    public static string ManageConfig(
        [Description("Configuration action to perform")] string action,
        [Description("Configuration key")] string? key = null,
        [Description("Configuration value")] string? value = null)
    {
        return action.ToLower() switch
        {
            "list" => "Available configs:\n- debug_mode: false\n- max_connections: 100\n- log_level: info",
            "get" when !string.IsNullOrEmpty(key) => $"Config '{key}': [simulated value for {key}]",
            "set" when !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) => 
                $"Config '{key}' set to '{value}' (simulated)",
            _ => "Usage: action must be 'list', 'get' (with key), or 'set' (with key and value)"
        };
    }

    [McpServerTool, Description("Views audit logs. Requires admin privileges.")]
    public static string ViewAuditLogs([Description("Number of recent entries to show")] int count = 10)
    {
        var logs = new List<string>();
        for (int i = 0; i < Math.Min(count, 10); i++)
        {
            logs.Add($"{DateTime.Now.AddMinutes(-i):HH:mm:ss} - User action logged (simulated entry {i + 1})");
        }
        return $"Recent audit logs ({logs.Count} entries):\n" + string.Join("\n", logs);
    }
}