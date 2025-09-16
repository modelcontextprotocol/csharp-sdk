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
/// </remarks>
public sealed class ElicitationCapability
{
    // Currently empty in the spec, but may be extended in the future.
}