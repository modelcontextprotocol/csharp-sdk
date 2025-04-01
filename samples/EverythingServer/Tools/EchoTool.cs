using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class EchoTool
{
    [McpServerTool(Name = "echo"), Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"Echo: {message}";
}
