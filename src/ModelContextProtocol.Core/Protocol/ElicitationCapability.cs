using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capability for a client to provide server-requested additional information during interactions.
/// </summary>
/// <remarks>
/// <para>
/// This capability enables the MCP client to respond to elicitation requests from an MCP server.
/// </para>
/// <para>
/// When this capability is enabled, an MCP server can request the client to provide additional information
/// during interactions. The client must set a <see cref="ModelContextProtocol.Client.McpClientHandlers.ElicitationHandler"/> to process these requests.
/// </para>
/// <para>
/// This class is intentionally empty as the Model Context Protocol specification does not
/// currently define additional properties for sampling capabilities. Future versions of the
/// specification may extend this capability with additional configuration options.
/// </para>
/// </remarks>
public sealed class ElicitationCapability
{
    /// <summary>
    /// Gets or sets the handler for processing <see cref="RequestMethods.ElicitationCreate"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler function is called when an MCP server requests the client to provide additional
    /// information during interactions. The client must set this property for the elicitation capability to work.
    /// </para>
    /// <para>
    /// The handler receives message parameters and a cancellation token.
    /// It should return a <see cref="ElicitResult"/> containing the response to the elicitation request.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    [Obsolete($"Use {nameof(McpClientHandlers.ElicitationHandler)} instead.")]
    public Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? ElicitationHandler
    {
        get => throw new NotSupportedException($"Use {nameof(McpClientHandlers.ElicitationHandler)} instead.");
        set => throw new NotSupportedException($"Use {nameof(McpClientHandlers.ElicitationHandler)} instead.");
    }
}