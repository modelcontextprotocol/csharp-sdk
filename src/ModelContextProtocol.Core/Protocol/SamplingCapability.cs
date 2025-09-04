using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capability for a client to generate text or other content using an AI model.
/// </summary>
/// <remarks>
/// <para>
/// This capability enables the MCP client to respond to sampling requests from an MCP server.
/// </para>
/// <para>
/// When this capability is enabled, an MCP server can request the client to generate content
/// using an AI model. The client must set a <see cref="SamplingHandler"/> to process these requests.
/// </para>
/// </remarks>
public sealed class SamplingCapability
{
    // Currently empty in the spec, but may be extended in the future

    /// <summary>
    /// Gets or sets the handler for processing <see cref="RequestMethods.SamplingCreateMessage"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler function is called when an MCP server requests the client to generate content
    /// using an AI model. The client must set this property for the sampling capability to work.
    /// </para>
    /// <para>
    /// The handler receives message parameters, a progress reporter for updates, and a 
    /// cancellation token. It should return a <see cref="CreateMessageResult"/> containing the 
    /// generated content.
    /// </para>
    /// <para>
    /// You can create a handler using the <see cref="McpClient.CreateSamplingHandler"/> extension
    /// method with any implementation of <see cref="IChatClient"/>.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>>? SamplingHandler { get; set; }
}