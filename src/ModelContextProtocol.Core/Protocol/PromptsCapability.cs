using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the server's capability to provide predefined prompt templates that clients can use.
/// </summary>
/// <remarks>
/// <para>
/// The prompts capability allows a server to expose a collection of predefined prompt templates that clients
/// can discover and use. These prompts can be static (defined in the <see cref="PromptCollection"/>) or
/// dynamically generated through handlers.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class PromptsCapability
{
    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the prompt list.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the server will send notifications using 
    /// <see cref="NotificationMethods.PromptListChangedNotification"/> when prompts are added, 
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their prompt cache. This capability enables clients to stay synchronized with server-side changes 
    /// to available prompts.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsList"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests a list of available prompts from the server
    /// via a <see cref="RequestMethods.PromptsList"/> request. Results from this handler are returned
    /// along with any prompts defined in <see cref="PromptCollection"/>.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, ValueTask<ListPromptsResult>>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsGet"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client requests details for a specific prompt by name and provides arguments 
    /// for the prompt if needed. The handler receives the request context containing the prompt name and any arguments, 
    /// and should return a <see cref="GetPromptResult"/> with the prompt messages and other details.
    /// </para>
    /// <para>
    /// This handler will be invoked if the requested prompt name is not found in the <see cref="PromptCollection"/>,
    /// allowing for dynamic prompt generation or retrieval from external sources.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, ValueTask<GetPromptResult>>? GetPromptHandler { get; set; }

    /// <summary>
    /// Gets or sets a collection of prompts that will be served by the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="PromptCollection"/> contains the predefined prompts that clients can request from the server.
    /// This collection works in conjunction with <see cref="ListPromptsHandler"/> and <see cref="GetPromptHandler"/>
    /// when those are provided:
    /// </para>
    /// <para>
    /// - For <see cref="RequestMethods.PromptsList"/> requests: The server returns all prompts from this collection 
    ///   plus any additional prompts provided by the <see cref="ListPromptsHandler"/> if it's set.
    /// </para>
    /// <para>
    /// - For <see cref="RequestMethods.PromptsGet"/> requests: The server first checks this collection for the requested prompt.
    ///   If not found, it will invoke the <see cref="GetPromptHandler"/> as a fallback if one is set.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerPrompt>? PromptCollection { get; set; }
}