using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

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
    public static IEnumerable<ContentBlock> AnnotatedMessage(MessageType messageType, bool includeImage = true)
    {
        List<ContentBlock> contents = messageType switch
        {
            MessageType.Error => [new TextContentBlock
            {
                Text = "Error: Operation failed",
                Annotations = new() { Audience = [Role.User, Role.Assistant], Priority = 1.0f }
            }],
            MessageType.Success => [new TextContentBlock
            {
                Text = "Operation completed successfully",
                Annotations = new() { Audience = [Role.User], Priority = 0.7f }
            }],
            MessageType.Debug => [new TextContentBlock
            {
                Text = "Debug: Cache hit ratio 0.95, latency 150ms",
                Annotations = new() { Audience = [Role.Assistant], Priority = 0.3f }
            }],
            _ => throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null)
        };

        if (includeImage)
        {
            contents.Add(new ImageContentBlock
            {
                Data = Encoding.UTF8.GetBytes(TinyImageTool.MCP_TINY_IMAGE.Split(",").Last()),
                MimeType = "image/png",
                Annotations = new() { Audience = [Role.User], Priority = 0.5f }
            });
        }

        return contents;
    }
}
