using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

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
/// using an AI model. The client must set a <see cref="McpClientHandlers.SamplingHandler"/> to process these requests.
/// </para>
/// </remarks>
public sealed class SamplingCapability
{
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
    /// You can create a handler using the <see cref="AIContentExtensions.CreateSamplingHandler"/> extension
    /// method with any implementation of <see cref="IChatClient"/>.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    [Obsolete($"Use {nameof(McpClientOptions.Handlers.SamplingHandler)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>>? SamplingHandler { get; set; }

    /// <summary>
    /// Gets or sets whether the client supports context inclusion via includeContext parameter.
    /// </summary>
    /// <remarks>
    /// If not declared, servers should only use includeContext: "none".
    /// </remarks>
    [JsonPropertyName("context")]
    public SamplingContextCapability? Context { get; set; }

    /// <summary>
    /// Gets or sets whether the client supports tool use via tools and toolChoice parameters.
    /// </summary>
    [JsonPropertyName("tools")]
    public SamplingToolsCapability? Tools { get; set; }
}

