using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>
/// Provides extension methods for converting between Model Context Protocol (MCP) types and Microsoft.Extensions.AI types.
/// </summary>
/// <remarks>
/// This class serves as an adapter layer between Model Context Protocol (MCP) types and the <see cref="AIContent"/> model types
/// from the Microsoft.Extensions.AI namespace.
/// </remarks>
public static class AIContentExtensions
{
    /// <summary>
    /// Converts a <see cref="PromptMessage"/> to a <see cref="ChatMessage"/> object.
    /// </summary>
    /// <param name="promptMessage">The prompt message to convert.</param>
    /// <returns>A <see cref="ChatMessage"/> object created from the prompt message.</returns>
    /// <remarks>
    /// This method transforms a protocol-specific <see cref="PromptMessage"/> from the Model Context Protocol
    /// into a standard <see cref="ChatMessage"/> object that can be used with AI client libraries.
    /// </remarks>
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

    /// <summary>
    /// Converts a <see cref="GetPromptResult"/> to a list of <see cref="ChatMessage"/> objects.
    /// </summary>
    /// <param name="promptResult">The prompt result containing messages to convert.</param>
    /// <returns>A list of <see cref="ChatMessage"/> objects created from the prompt messages.</returns>
    /// <remarks>
    /// This method transforms protocol-specific <see cref="PromptMessage"/> objects from a Model Context Protocol
    /// prompt result into standard <see cref="ChatMessage"/> objects that can be used with AI client libraries.
    /// </remarks>
    public static IList<ChatMessage> ToChatMessages(this GetPromptResult promptResult)
    {
        Throw.IfNull(promptResult);

        return promptResult.Messages.Select(m => m.ToChatMessage()).ToList();
    }

    /// <summary>
    /// Converts a <see cref="ChatMessage"/> to a list of <see cref="PromptMessage"/> objects.
    /// </summary>
    /// <param name="chatMessage">The chat message to convert.</param>
    /// <returns>A list of <see cref="PromptMessage"/> objects created from the chat message's contents.</returns>
    /// <remarks>
    /// This method transforms standard <see cref="ChatMessage"/> objects used with AI client libraries into
    /// protocol-specific <see cref="PromptMessage"/> objects for the Model Context Protocol system.
    /// Only representable content items are processed.
    /// </remarks>
    public static IList<PromptMessage> ToPromptMessages(this ChatMessage chatMessage)
    {
        Throw.IfNull(chatMessage);

        Role r = chatMessage.Role == ChatRole.User ? Role.User : Role.Assistant;

        List<PromptMessage> messages = [];
        foreach (var content in chatMessage.Contents)
        {
            if (content is TextContent or DataContent)
            {
                messages.Add(new PromptMessage { Role = r, Content = content.ToContent() });
            }
        }

        return messages;
    }

    /// <summary>Creates a new <see cref="AIContent"/> from the content of a <see cref="Content"/>.</summary>
    /// <param name="content">The <see cref="Content"/> to convert.</param>
    /// <returns>The created <see cref="AIContent"/>.</returns>
    /// <remarks>
    /// This method converts Model Context Protocol content types to the equivalent Microsoft.Extensions.AI 
    /// content types, enabling seamless integration between the protocol and AI client libraries.
    /// </remarks>
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
            ac = resourceContents.ToAIContent();
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
    /// <remarks>
    /// This method converts Model Context Protocol resource types to the equivalent Microsoft.Extensions.AI 
    /// content types, enabling seamless integration between the protocol and AI client libraries.
    /// </remarks>
    public static AIContent ToAIContent(this ResourceContents content)
    {
        Throw.IfNull(content);

        AIContent ac = content switch
        {
            BlobResourceContents blobResource => new DataContent(Convert.FromBase64String(blobResource.Blob), blobResource.MimeType ?? "application/octet-stream"),
            TextResourceContents textResource => new TextContent(textResource.Text),
            _ => throw new NotSupportedException($"Resource type '{content.GetType().Name}' is not supported.")
        };

        (ac.AdditionalProperties ??= [])["uri"] = content.Uri;
        ac.RawRepresentation = content;

        return ac;
    }

    /// <summary>Creates a list of <see cref="AIContent"/> from a sequence of <see cref="Content"/>.</summary>
    /// <param name="contents">The <see cref="Content"/> instances to convert.</param>
    /// <returns>The created <see cref="AIContent"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method converts a collection of Model Context Protocol content objects into a collection of
    /// Microsoft.Extensions.AI content objects. It's useful when working with multiple content items, such as
    /// when processing the contents of a message or response.
    /// </para>
    /// <para>
    /// Each <see cref="Content"/> object is converted using <see cref="ToAIContent(Content)"/>,
    /// preserving the type-specific conversion logic for text, images, audio, and resources.
    /// </para>
    /// </remarks>
    public static IList<AIContent> ToAIContents(this IEnumerable<Content> contents)
    {
        Throw.IfNull(contents);

        return [.. contents.Select(ToAIContent)];
    }

    /// <summary>Creates a list of <see cref="AIContent"/> from a sequence of <see cref="ResourceContents"/>.</summary>
    /// <param name="contents">The <see cref="ResourceContents"/> instances to convert.</param>
    /// <returns>A list of <see cref="AIContent"/> objects created from the resource contents.</returns>
    /// <remarks>
    /// <para>
    /// This method converts a collection of Model Context Protocol resource objects into a collection of
    /// Microsoft.Extensions.AI content objects. It's useful when working with multiple resources, such as
    /// when processing the contents of a <see cref="ReadResourceResult"/>.
    /// </para>
    /// <para>
    /// Each <see cref="ResourceContents"/> object is converted using <see cref="ToAIContent(ResourceContents)"/>,
    /// preserving the type-specific conversion logic: text resources become <see cref="TextContent"/> objects and
    /// binary resources become <see cref="DataContent"/> objects.
    /// </para>
    /// </remarks>
    public static IList<AIContent> ToAIContents(this IEnumerable<ResourceContents> contents)
    {
        Throw.IfNull(contents);

        return [.. contents.Select(ToAIContent)];
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
