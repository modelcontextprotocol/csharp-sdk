using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class AnnotatedMessageTool
{
    public enum MessageType
    {
        Error,
        Success,
        Debug,
    }

    [McpServerTool(Name = "annotatedMessage"), Description("Generates an annotated message")]
    public static IEnumerable<Content> AnnotatedMessage(MessageType messageType, bool includeImage = true)
    {
        List<Content> contents = messageType switch
        {
            MessageType.Error => [new()
            {
                Type = "text",
                Text = "Error: Operation failed",
                Annotations = new() { Audience = [Role.User, Role.Assistant], Priority = 1.0f }
            }],
            MessageType.Success => [new()
            {
                Type = "text",
                Text = "Operation completed successfully",
                Annotations = new() { Audience = [Role.User], Priority = 0.7f }
            }],
            MessageType.Debug => [new()
            {
                Type = "text",
                Text = "Debug: Cache hit ratio 0.95, latency 150ms",
                Annotations = new() { Audience = [Role.Assistant], Priority = 0.3f }
            }],
            _ => throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null)
        };

        if (includeImage)
        {
            contents.Add(new()
            {
                Type = "image",
                Data = TinyImageTool.MCP_TINY_IMAGE.Split(",").Last(),
                MimeType = "image/png",
                Annotations = new() { Audience = [Role.User], Priority = 0.5f }
            });
        }

        return contents;
    }
}
