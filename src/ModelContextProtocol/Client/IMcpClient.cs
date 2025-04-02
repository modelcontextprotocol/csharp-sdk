using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of an MCP client connecting to a specific server.
/// </summary>
public interface IMcpClient : IMcpEndpoint
{
    /// <summary>
    /// Gets the capabilities supported by the server.
    /// </summary>
    ServerCapabilities? ServerCapabilities { get; }

    /// <summary>
    /// Gets the version and implementation information of the server.
    /// </summary>
    Implementation? ServerInfo { get; }

    /// <summary>
    /// Instructions describing how to use the server and its features.
    /// This can be used by clients to improve the LLM's understanding of available tools, resources, etc. 
    /// It can be thought of like a "hint" to the model. For example, this information MAY be added to the system prompt.
    /// </summary>
    string? ServerInstructions { get; }
}