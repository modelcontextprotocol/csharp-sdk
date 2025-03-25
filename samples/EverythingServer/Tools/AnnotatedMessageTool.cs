using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public static class AnnotatedMessageTool
{
    public enum MessageType
    {
        Error,
        Success,
        Debug,
    }

    [McpServerTool, Description("Generates an annotated message")]
    public static IEnumerable<string> AnnotatedMessage(MessageType messageType, bool includeImage = true)
    {
        return ["incomplete"];
    }
}
