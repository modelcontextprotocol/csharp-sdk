using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpToolType]
public static class AnnotatedMessageTool
{
    public enum MessageType
    {
        Error,
        Success,
        Debug,
    }

    [McpTool, Description("Generates an annotated message")]
    public static IEnumerable<string> AnnotatedMessage(MessageType messageType, bool includeImage = true)
    {
        return ["incomplete"];
    }
}
