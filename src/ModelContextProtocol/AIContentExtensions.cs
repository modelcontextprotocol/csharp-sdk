using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>
/// Provides extension methods for converting between Model Context Protocol (MCP) types and Microsoft AI client/server API types.
/// </summary>
/// <remarks>
/// This class serves as a critical adapter layer between Model Context Protocol (MCP) types and the AIContent model types
/// from the Microsoft.Extensions.AI namespace. It enables seamless bidirectional conversion between:
/// <list type="bullet">
///   <item><description><see cref="PromptMessage"/> and <see cref="ChatMessage"/></description></item>
///   <item><description><see cref="GetPromptResult"/> and collections of <see cref="ChatMessage"/></description></item>
///   <item><description><see cref="Content"/> and <see cref="AIContent"/></description></item>
///   <item><description><see cref="ResourceContents"/> and <see cref="AIContent"/></description></item>
/// </list>
/// 
/// These conversions are essential in scenarios such as:
/// <list type="bullet">
///   <item><description>Integrating MCP clients with AI client applications</description></item>
///   <item><description>Processing AI content between protocol-specific and application-specific formats</description></item>
///   <item><description>Building adapters between MCP systems and other AI frameworks</description></item>
///   <item><description>Converting between wire formats and client API representations</description></item>
/// </list>
/// 
/// When working with the MCP infrastructure, these extensions allow developers to easily transform data
/// received from MCP endpoints into formats that can be directly used with AI client libraries, and vice versa.
/// </remarks>
public static class AIContentExtensions
{
    /// <summary>
    /// Converts a <see cref="PromptMessage"/> to a <see cref="ChatMessage"/> object.
    /// </summary>
    /// <param name="promptMessage">The prompt message to convert.</param>
    /// <returns>A <see cref="ChatMessage"/> object created from the prompt message.</returns>
    /// <remarks>
    /// <para>
    /// This method transforms a protocol-specific <see cref="PromptMessage"/> from the Model Context Protocol
    /// into a standard <see cref="ChatMessage"/> object that can be used with AI client libraries.
    /// </para>
    /// <para>
    /// The role mapping is preserved in the conversion:
    /// <list type="bullet">
    ///   <item><description><see cref="Role.User"/> → <see cref="ChatRole.User"/></description></item>
    ///   <item><description><see cref="Role.Assistant"/> → <see cref="ChatRole.Assistant"/></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The original <see cref="PromptMessage"/> object is stored in the <see cref="AIContent.RawRepresentation"/>
    /// property, enabling round-trip conversions if needed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get a prompt message from an MCP source
    /// PromptMessage promptMessage = new PromptMessage
    /// {
    ///     Role = Role.User,
    ///     Content = new Content { Text = "Hello, AI!", Type = "text" }
    /// };
    /// 
    /// // Convert to ChatMessage for use with AI clients
    /// ChatMessage chatMessage = promptMessage.ToChatMessage();
    /// 
    /// // Use the chat message with Microsoft.Extensions.AI
    /// Console.WriteLine($"{chatMessage.Role}: {chatMessage.Contents.FirstOrDefault()?.ToString()}");
    /// </code>
    /// </example>
    /// <seealso cref="ToChatMessages(GetPromptResult)"/>
    /// <seealso cref="ToPromptMessages(ChatMessage)"/>
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
    /// Each message's role is preserved in the conversion.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get prompt result from an MCP client
    /// var result = await mcpClient.GetPromptAsync("SomePromptName");
    /// 
    /// // Convert to ChatMessages for use with AI clients
    /// var chatMessages = result.ToChatMessages();
    /// 
    /// // Use the chat messages
    /// foreach (var message in chatMessages)
    /// {
    ///     Console.WriteLine($"{message.Role}: {message.Text}");
    /// }
    /// </code>
    /// </example>
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
    /// The role is preserved in the conversion (User → User, Assistant → Assistant).
    /// Only content items of type <see cref="TextContent"/> or <see cref="DataContent"/> are processed.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a chat message
    /// var chatMessage = new ChatMessage
    /// {
    ///     Role = ChatRole.User,
    ///     Contents = [new TextContent("Hello, how can you help me today?")]
    /// };
    /// 
    /// // Convert to PromptMessages for use with MCP
    /// var promptMessages = chatMessage.ToPromptMessages();
    /// 
    /// // Use the prompt messages with MCP protocol
    /// var getPromptResult = new GetPromptResult
    /// {
    ///     Messages = promptMessages
    /// };
    /// </code>
    /// </example>
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
    /// <para>
    /// This method converts Model Context Protocol content types to the equivalent Microsoft.Extensions.AI 
    /// content types, enabling seamless integration between the protocol and AI client libraries.
    /// </para>
    /// <para>
    /// The conversion follows these rules:
    /// <list type="bullet">
    ///   <item><description>When <see cref="Content.Type"/> is "image" or "audio" with data → <see cref="DataContent"/> with base64-decoded data and preserved MIME type</description></item>
    ///   <item><description>When <see cref="Content.Resource"/> is not null → Uses <see cref="ToAIContent(ResourceContents)"/> for the resource</description></item>
    ///   <item><description>Otherwise → <see cref="TextContent"/> using the text content</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The original <see cref="Content"/> object is stored in the <see cref="AIContent.RawRepresentation"/>
    /// property, enabling round-trip conversions if needed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Convert a Content with text to AIContent
    /// var textContent = new Content 
    /// {
    ///     Text = "Hello, world!",
    ///     Type = "text"
    /// };
    /// AIContent aiContent = textContent.ToAIContent();
    /// 
    /// // Convert a Content with image data to AIContent
    /// var imageContent = new Content
    /// {
    ///     Type = "image",
    ///     MimeType = "image/png",
    ///     Data = Convert.ToBase64String(imageBytes)
    /// };
    /// AIContent aiImageContent = imageContent.ToAIContent();
    /// 
    /// // Convert a Content with a resource to AIContent
    /// var resourceContent = new Content
    /// {
    ///     Type = "resource",
    ///     Resource = new TextResourceContents
    ///     {
    ///         Uri = "resource://document.txt",
    ///         Text = "This is a resource text"
    ///     }
    /// };
    /// AIContent aiResourceContent = resourceContent.ToAIContent();
    /// </code>
    /// </example>
    /// <seealso cref="ToAIContent(ResourceContents)"/>
    /// <seealso cref="DataContent"/>
    /// <seealso cref="TextContent"/>
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
    /// <para>
    /// This method converts Model Context Protocol resource types to the equivalent Microsoft.Extensions.AI 
    /// content types, enabling seamless integration between the protocol and AI client libraries.
    /// </para>
    /// <para>
    /// The conversion follows these rules:
    /// <list type="bullet">
    ///   <item><description><see cref="BlobResourceContents"/> → <see cref="DataContent"/> with base64-decoded data and preserved MIME type</description></item>
    ///   <item><description><see cref="TextResourceContents"/> → <see cref="TextContent"/> with the text preserved</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The URI of the resource is preserved in the <see cref="AIContent.AdditionalProperties"/> dictionary
    /// with the key "uri", allowing applications to maintain resource identifiers.
    /// </para>
    /// <para>
    /// The original <see cref="ResourceContents"/> object is stored in the <see cref="AIContent.RawRepresentation"/>
    /// property, enabling round-trip conversions if needed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Convert a TextResourceContents to AIContent
    /// var textResource = new TextResourceContents
    /// {
    ///     Uri = "resource://document.txt",
    ///     MimeType = "text/plain",
    ///     Text = "This is a text resource"
    /// };
    /// AIContent textContent = textResource.ToAIContent();
    /// 
    /// // Use the converted content with Microsoft.Extensions.AI client
    /// await aiClient.GenerateTextAsync(new TextGenerationOptions
    /// {
    ///     Messages = [
    ///         new ChatMessage
    ///         {
    ///             Role = ChatRole.User,
    ///             Contents = [textContent]
    ///         }
    ///     ]
    /// });
    /// 
    /// // Convert a BlobResourceContents to AIContent
    /// var blobResource = new BlobResourceContents
    /// {
    ///     Uri = "resource://image.png",
    ///     MimeType = "image/png",
    ///     Blob = Convert.ToBase64String(imageBytes)
    /// };
    /// AIContent imageContent = blobResource.ToAIContent();
    /// </code>
    /// </example>
    /// <exception cref="NotSupportedException">Thrown when the resource type is not supported.</exception>
    /// <seealso cref="BlobResourceContents"/>
    /// <seealso cref="TextResourceContents"/>
    /// <seealso cref="DataContent"/>
    /// <seealso cref="TextContent"/>
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
    /// <example>
    /// <code>
    /// // Get Content objects from a source
    /// IEnumerable&lt;Content&gt; contents = someResponse.Contents;
    /// 
    /// // Convert all Content objects to AIContent objects
    /// IList&lt;AIContent&gt; aiContents = contents.ToAIContents();
    /// 
    /// // Use the converted contents
    /// foreach (var content in aiContents)
    /// {
    ///     if (content is TextContent textContent)
    ///     {
    ///         Console.WriteLine($"Text content: {textContent.Text}");
    ///     }
    ///     else if (content is DataContent dataContent)
    ///     {
    ///         Console.WriteLine($"Binary content: {dataContent.MediaType}, {dataContent.Data.Length} bytes");
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ToAIContent(Content)"/>
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
    /// <example>
    /// <code>
    /// // Get resources from a read resource result
    /// ReadResourceResult result = await client.ReadResourceAsync("resource://folder");
    /// 
    /// // Convert all resources to AIContent objects
    /// IList&lt;AIContent&gt; aiContents = result.Contents.ToAIContents();
    /// 
    /// // Use the converted contents
    /// foreach (var content in aiContents)
    /// {
    ///     if (content is TextContent textContent)
    ///     {
    ///         Console.WriteLine($"Text content: {textContent.Text}");
    ///     }
    ///     else if (content is DataContent dataContent)
    ///     {
    ///         Console.WriteLine($"Binary content: {dataContent.MediaType}, {dataContent.Data.Length} bytes");
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ToAIContent(ResourceContents)"/>
    /// <seealso cref="ReadResourceResult.Contents"/>
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
