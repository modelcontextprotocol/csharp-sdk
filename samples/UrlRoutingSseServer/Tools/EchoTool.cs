using ModelContextProtocol.Server;
using System.ComponentModel;

namespace UrlRoutingSseServer.Tools;

[McpServerToolType]
public sealed class EchoTool
{
    [McpServerTool, Description("Echoes the input back to the client.")]
    [McpServerToolRoute("echo")]
    public static string Echo(string message)
    {
        return "hello " + message;
    }

    [McpServerTool(Name = "echo_advanced"), Description("Advanced echo with formatting options.")]
    [McpServerToolRoute("echo", "utilities")]
    public static string EchoAdvanced(
        [Description("The message to echo")] string message,
        [Description("Whether to convert to uppercase")] bool uppercase = false)
    {
        var result = $"Advanced echo: {message}";
        return uppercase ? result.ToUpper() : result;
    }
}
