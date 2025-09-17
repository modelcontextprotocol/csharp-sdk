using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) client that connects to and communicates with an MCP server.
/// </summary>
[Obsolete($"Use {nameof(McpClient)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
public interface IMcpClient : IMcpEndpoint
{
    /// <summary>
    /// Gets the capabilities supported by the connected server.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    ServerCapabilities ServerCapabilities { get; }

    /// <summary>
    /// Gets the implementation information of the connected server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property provides identification details about the connected server, including its name and version.
    /// It is populated during the initialization handshake and is available after a successful connection.
    /// </para>
    /// <para>
    /// This information can be useful for logging, debugging, compatibility checks, and displaying server
    /// information to users.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    Implementation ServerInfo { get; }

    /// <summary>
    /// Gets any instructions describing how to use the connected server and its features.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains instructions provided by the server during initialization that explain
    /// how to effectively use its capabilities. These instructions can include details about available
    /// tools, expected input formats, limitations, or any other helpful information.
    /// </para>
    /// <para>
    /// This can be used by clients to improve an LLM's understanding of available tools, prompts, and resources. 
    /// It can be thought of like a "hint" to the model and may be added to a system prompt.
    /// </para>
    /// </remarks>
    string? ServerInstructions { get; }
}
