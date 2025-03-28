using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>Provides helpers for conversions related to <see cref="AIContent"/>.</summary>
public static class AIContentExtensions
{
    /// <summary>Creates a <see cref="ChatMessage"/> from a <see cref="PromptMessage"/>.</summary>
    /// <param name="promptMessage">The message to convert.</param>
    /// <returns>The created <see cref="ChatMessage"/>.</returns>
    public static ChatMessage ToChatMessage(this PromptMessage promptMessage)
    {
        Throw.IfNull(promptMessage);

        return new()
        {
            RawRepresentation = promptMessage,
            Role = promptMessage.Role == Role.User ? ChatRole.User : ChatRole.Assistant,
            Contents = [ToAIContent(promptMessage.Content)]
        };
    }

    /// <summary>Creates a new <see cref="AIContent"/> from the content of a <see cref="Content"/>.</summary>
    /// <param name="content">The <see cref="Content"/> to convert.</param>
    /// <returns>The created <see cref="AIContent"/>.</returns>
    public static AIContent ToAIContent(this Content content)
    {
        Throw.IfNull(content);

        AIContent ac;
        if (content is { Type: "image" or "audio", MimeType: not null, Data: not null })
        {
            ac = new DataContent(Convert.FromBase64String(content.Data), content.MimeType);
        }
        else if (content is { Type: "resource" } && content.Resource is { } resourceContents)
        {
            ac = resourceContents.Blob is not null && resourceContents.MimeType is not null ?
                new DataContent(Convert.FromBase64String(resourceContents.Blob), resourceContents.MimeType) :
                new TextContent(resourceContents.Text);

            (ac.AdditionalProperties ??= [])["uri"] = resourceContents.Uri;
        }
        else
        {
            ac = new TextContent(content.Text);
        }

        ac.RawRepresentation = content;

        return ac;
    }

    /// <summary>Creates a new <see cref="AIContent"/> from the content of a <see cref="ResourceContents"/>.</summary>
    /// <param name="content">The <see cref="ResourceContents"/> to convert.</param>
    /// <returns>The created <see cref="AIContent"/>.</returns>
    public static AIContent ToAIContent(this ResourceContents content)
    {
        Throw.IfNull(content);

        AIContent ac = content.Blob is not null && content.MimeType is not null ?
            new DataContent(Convert.FromBase64String(content.Blob), content.MimeType) :
            new TextContent(content.Text);

        (ac.AdditionalProperties ??= [])["uri"] = content.Uri;
        ac.RawRepresentation = content;

        return ac;
    }

    /// <summary>Creates a list of <see cref="AIContent"/> from a sequence of <see cref="Content"/>.</summary>
    /// <param name="contents">The <see cref="Content"/> instances to convert.</param>
    /// <returns>The created <see cref="AIContent"/> instances.</returns>
    public static IList<AIContent> ToAIContents(this IEnumerable<Content> contents)
    {
        Throw.IfNull(contents);

        return contents.Select(ToAIContent).ToList();
    }

    /// <summary>Creates a list of <see cref="AIContent"/> from a sequence of <see cref="ResourceContents"/>.</summary>
    /// <param name="contents">The <see cref="ResourceContents"/> instances to convert.</param>
    /// <returns>The created <see cref="AIContent"/> instances.</returns>
    public static IList<AIContent> ToAIContents(this IEnumerable<ResourceContents> contents)
    {
        Throw.IfNull(contents);

        return contents.Select(ToAIContent).ToList();
    }

    /// <summary>Extracts the data from a <see cref="DataContent"/> as a Base64 string.</summary>
    internal static string GetBase64Data(this DataContent dataContent)
    {
#if NET
        return Convert.ToBase64String(dataContent.Data.Span);
#else
        return MemoryMarshal.TryGetArray(dataContent.Data, out ArraySegment<byte> segment) ?
            Convert.ToBase64String(segment.Array!, segment.Offset, segment.Count) :
            Convert.ToBase64String(dataContent.Data.ToArray());
#endif
    }

    internal static Content ToContent(this AIContent content) =>
        content switch
        {
            TextContent textContent => new()
            {
                Text = textContent.Text,
                Type = "text",
            },

            DataContent dataContent => new()
            {
                Data = dataContent.GetBase64Data(),
                MimeType = dataContent.MediaType,
                Type =
                    dataContent.HasTopLevelMediaType("image") ? "image" :
                    dataContent.HasTopLevelMediaType("audio") ? "audio" :
                    "resource",
            },
            
            _ => new()
            {
                Text = JsonSerializer.Serialize(content, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object))),
                Type = "text",
            }
        };
}
