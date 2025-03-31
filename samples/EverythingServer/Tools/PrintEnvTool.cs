using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace EverythingServer.Tools;

[McpServerToolType]
public static class PrintEnvTool
{
    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "printEnv"), Description("Prints all environment variables, helpful for debugging MCP server configuration")]
    public static string PrintEnv()
    {
        Debugger.Launch();
        var envVars = Environment.GetEnvironmentVariables();
        return JsonSerializer.Serialize(envVars, options);
    }
}
