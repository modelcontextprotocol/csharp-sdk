using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) client that connects to and communicates with an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// The MCP client provides a standardized way to interact with AI tool servers, allowing applications to:
/// <list type="bullet">
///   <li>Connect to MCP-compliant servers</li>
///   <li>Discover and use available tools, prompts, and resources</li>
///   <li>Execute tool calls and prompt completions</li>
///   <li>Subscribe to and receive notifications from resources</li>
/// </list>
/// </para>
/// <para>
/// An IMcpClient instance is typically created using <see cref="McpClientFactory"/> and should be disposed properly when finished.
/// </para>
/// <example>
/// <code>
/// // Connect to an MCP server via StdIo transport
/// await using var mcpClient = await McpClientFactory.CreateAsync(
///     new()
///     {
///         Id = "demo-server",
///         Name = "Demo Server",
///         TransportType = TransportTypes.StdIo,
///         TransportOptions = new()
///         {
///             ["command"] = "npx",
///             ["arguments"] = "-y @modelcontextprotocol/server-everything",
///         }
///     });
///     
/// // List available tools from the server
/// var tools = await mcpClient.ListToolsAsync();
/// foreach (var tool in tools)
/// {
///     Console.WriteLine($"Connected to server with tool: {tool.Name}");
/// }
/// </code>
/// </example>
/// </remarks>
public interface IMcpClient : IMcpEndpoint
{
    /// <summary>
    /// Gets the capabilities supported by the connected server.
    /// </summary>
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
    /// <para>
    /// The property will throw an InvalidOperationException if accessed before a successful connection is established.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Connect to the server
    /// await mcpClient.ConnectAsync(cancellationToken);
    /// 
    /// // Access server information
    /// Console.WriteLine($"Connected to {mcpClient.ServerInfo.Name} version {mcpClient.ServerInfo.Version}");
    /// 
    /// // Use server information for version compatibility checks
    /// if (Version.TryParse(mcpClient.ServerInfo.Version, out var version) &amp;&amp; version.Major >= 2)
    /// {
    ///     // Use features only available in version 2.0+
    /// }
    /// </code>
    /// </para>
    /// </remarks>
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
    /// <para>
    /// Example usage:
    /// <code>
    /// // Add server instructions to system messages if available
    /// if (mcpClient.ServerInstructions is not null)
    /// {
    ///     messages.Add(new ChatMessage(ChatRole.System, mcpClient.ServerInstructions));
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    string? ServerInstructions { get; }
}