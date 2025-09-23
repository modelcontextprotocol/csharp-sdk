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
/// <para>
/// This class is intentionally empty as the Model Context Protocol specification does not
/// currently define additional properties for sampling capabilities. Future versions of the
/// specification may extend this capability with additional configuration options.
/// </para>
/// </remarks>
public sealed class SamplingCapability
{
}