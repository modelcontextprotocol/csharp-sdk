using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of an MCP client connecting to a specific server.
/// </summary>
public interface IMcpClient : IMcpEndpoint
{
    /// <summary>
    /// Gets the capabilities supported by the connected server.
    /// </summary>
    ServerCapabilities ServerCapabilities { get; }

    /// <summary>
    /// Gets the implementation information of the connected server.
    /// </summary>
    Implementation ServerInfo { get; }

    /// <summary>
    /// Gets any instructions describing how to use the connected server and its features.
    /// </summary>
    /// <remarks>
    /// This can be used by clients to improve an LLM's understanding of available tools, prompts, and resources. 
    /// It can be thought of like a "hint" to the model and may be added to a system prompt.
    /// </remarks>
    string? ServerInstructions { get; }
}