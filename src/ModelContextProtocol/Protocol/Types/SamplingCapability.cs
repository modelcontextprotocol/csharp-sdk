using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the capability for a client to generate text or other content using an AI model.
/// This capability enables the MCP client to respond to sampling requests from an MCP server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// When this capability is enabled, an MCP server can request the client to generate content
/// using an AI model. The client must set a <see cref="SamplingHandler"/> to process these requests.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var clientOptions = new McpClientOptions
/// {
///     Capabilities = new ClientCapabilities
///     {
///         Sampling = new SamplingCapability
///         {
///             SamplingHandler = chatClient.CreateSamplingHandler()
///         }
///     }
/// };
/// </code>
/// </para>
/// </remarks>
public class SamplingCapability
{
    // Currently empty in the spec, but may be extended in the future

    /// <summary>
    /// Gets or sets the handler for processing sampling requests from an MCP server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler function is called when an MCP server requests the client to generate content
    /// using an AI model. The client must set this property for the sampling capability to work.
    /// </para>
    /// <para>
    /// The handler receives message parameters, a progress reporter for streaming updates, and a 
    /// cancellation token. It should return a <see cref="CreateMessageResult"/> containing the 
    /// generated content.
    /// </para>
    /// <para>
    /// You can create a handler using the <see cref="McpClientExtensions.CreateSamplingHandler"/> extension
    /// method with any implementation of <see cref="IChatClient"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create an IChatClient using OpenAI or other provider
    /// using IChatClient chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4");
    /// 
    /// // Create a sampling handler and assign it to the client options
    /// var clientOptions = new McpClientOptions
    /// {
    ///     Capabilities = new ClientCapabilities
    ///     {
    ///         Sampling = new SamplingCapability
    ///         {
    ///             SamplingHandler = chatClient.CreateSamplingHandler()
    ///         }
    ///     }
    /// };
    /// 
    /// // Use the options when creating the MCP client
    /// var mcpClient = await McpClientFactory.CreateAsync(serverConfig, clientOptions);
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, Task<CreateMessageResult>>? SamplingHandler { get; set; }
}