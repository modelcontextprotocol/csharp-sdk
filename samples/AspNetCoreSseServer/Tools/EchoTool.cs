using ModelContextProtocol.Server;
using System.ComponentModel;
using AspNetCoreSseServer.Attributes;

namespace TestServerWithHosting.Tools;

[McpServerToolType]
public sealed class EchoTool
{
    [McpServerTool, Description("Echoes the input back to the client.")]
    [LimitCalls(maxCalls: 10)]
    public static string Echo(string message)
    {
        return "hello " + message;
    }
}
