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

    [McpServerTool(Name = "annotatedMessage"), Description("Generates an annotated message")]
    public static IEnumerable<string> AnnotatedMessage(MessageType messageType, bool includeImage = true)
    {
        throw new NotSupportedException("Unable to write annotations to the output.");
    }
}
