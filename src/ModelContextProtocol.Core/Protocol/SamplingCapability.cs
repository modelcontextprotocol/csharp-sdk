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
/// using an AI model. The client must set a <see cref="ModelContextProtocol.Client.McpClientHandlers.SamplingHandler"/> to process these requests.
/// </para>
/// </remarks>
public sealed class SamplingCapability
{
    // Currently empty in the spec, but may be extended in the future
}