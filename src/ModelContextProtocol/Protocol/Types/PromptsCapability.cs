using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the server's capability to provide and manage predefined prompt templates that clients can use.
/// </summary>
/// <remarks>
/// <para>
/// The prompts capability allows a server to expose a collection of predefined prompt templates that clients
/// can discover and use. These prompts can be static (defined in the <see cref="PromptCollection"/>) or
/// dynamically generated through handlers.
/// </para>
/// <para>
/// This capability supports three main features:
/// <list type="bullet">
///   <item>
///     <description>Listing available prompts (<see cref="ListPromptsHandler"/>)</description>
///   </item>
///   <item>
///     <description>Retrieving specific prompts (<see cref="GetPromptHandler"/>)</description>
///   </item>
///   <item>
///     <description>Notifications when the prompt list changes (<see cref="ListChanged"/>)</description>
///   </item>
/// </list>
/// </para>
/// <para>
/// When configured on a server, clients can discover and utilize prompts through the MCP protocol's
/// prompts/list and prompts/get endpoints.
/// </para>
/// <para>
/// Example usage in server configuration:
/// <code>
/// var serverOptions = new McpServerOptions
/// {
///     Capabilities = new ServerCapabilities
///     {
///         Prompts = new PromptsCapability
///         {
///             ListChanged = true,
///             PromptCollection = new McpServerPrimitiveCollection&lt;McpServerPrompt&gt;(),
///             ListPromptsHandler = (request, cancellationToken) => 
///             {
///                 // Return available prompts
///                 return Task.FromResult(new ListPromptsResult { /* ... */ });
///             },
///             GetPromptHandler = (request, cancellationToken) =>
///             {
///                 // Return requested prompt
///                 return Task.FromResult(new GetPromptResult { /* ... */ });
///             }
///         }
///     }
/// };
/// </code>
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </para>
/// </remarks>
public class PromptsCapability
{
    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the prompt list.
    /// When set to <see langword="true"/>, the server will send notifications using 
    /// <see cref="NotificationMethods.PromptListChangedNotification"/> when prompts are added, 
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their prompt cache.
    /// </summary>
    /// <remarks>
    /// This capability enables clients to stay synchronized with server-side changes to available prompts.
    /// The server will broadcast the <c>notifications/prompts/list_changed</c> notification when prompts change.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for list prompts requests. This handler is invoked when a client
    /// requests a list of available prompts from the server.
    /// </summary>
    /// <remarks>
    /// The handler provides available prompts when a client calls the prompts/list endpoint.
    /// Results from this handler are combined with any prompts defined in <see cref="PromptCollection"/>.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for get prompt requests. This handler is invoked when a client requests details
    /// for a specific prompt by name and provides arguments for the prompt if needed.
    /// </summary>
    /// <remarks>
    /// When this handler is registered, it enables clients to request detailed information about specific prompts.
    /// The handler receives the request context containing the prompt name and any arguments, and should return
    /// a <see cref="GetPromptResult"/> with the prompt messages and other details.
    /// 
    /// This handler will be invoked if the requested prompt name is not found in the <see cref="PromptCollection"/>,
    /// allowing for dynamic prompt generation or retrieval from external sources.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>>? GetPromptHandler { get; set; }

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
    /// - For ListPrompts requests: The server returns all prompts from this collection plus any additional prompts
    ///   provided by the <see cref="ListPromptsHandler"/> if it's defined.
    /// </para>
    /// <para>
    /// - For GetPrompt requests: The server first checks this collection for the requested prompt. If not found,
    ///   it will invoke the <see cref="GetPromptHandler"/> as a fallback if one is defined.
    /// </para>
    /// <para>
    /// Prompts can be added to this collection either via attribute-based registration using
    /// <c>WithPrompts&lt;T&gt;</c> or programmatically by creating and adding individual <see cref="McpServerPrompt"/> instances.
    /// </para>
    /// <para>
    /// Example of programmatically adding a prompt:
    /// <code>
    /// // Create a prompt
    /// var prompt = McpServerPrompt.Create(
    ///     () => "What is the weather like in Paris today?",
    ///     new McpServerPromptCreateOptions
    ///     {
    ///         Name = "weather-inquiry",
    ///         Description = "A prompt that asks about the weather in a specific city"
    ///     });
    ///     
    /// // Add to server options
    /// serverOptions.Capabilities.Prompts.PromptCollection.Add(prompt);
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerPrompt>? PromptCollection { get; set; }
}