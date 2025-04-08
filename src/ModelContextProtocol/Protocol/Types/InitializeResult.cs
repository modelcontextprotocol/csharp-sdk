using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Result of the initialization request sent to the server during connection establishment.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="InitializeResult"/> is sent by the server in response to an <see cref="InitializeRequestParams"/> 
/// message from the client. It contains information about the server, its capabilities, and the protocol version
/// that will be used for the session.
/// </para>
/// <para>
/// After receiving this response, the client should send an initialization notification to complete the handshake.
/// This message is a critical part of the Model Context Protocol connection establishment workflow.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// // Server-side code
/// InitializeResult result = new InitializeResult
/// {
///     ProtocolVersion = "2024-11-05",
///     ServerInfo = new Implementation { Name = "MyMcpServer", Version = "1.0.0" },
///     Capabilities = new ServerCapabilities 
///     {
///         // Configure server capabilities as needed
///         Completions = new CompletionsCapability()
///     },
///     Instructions = "Optional usage instructions for the client"
/// };
/// 
/// // Client-side processing
/// Task&lt;InitializeResult&gt; response = client.SendRequestAsync&lt;InitializeRequestParams, InitializeResult&gt;(
///     RequestMethods.Initialize,
///     requestParams,
///     McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
///     McpJsonUtilities.JsonContext.Default.InitializeResult,
///     cancellationToken);
/// </code>
/// </para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </remarks>
public record InitializeResult
{
    /// <summary>
    /// The version of the Model Context Protocol that the server will use for this session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the protocol version the server has agreed to use, which should match the client's 
    /// requested version. If there's a mismatch, the client should throw an exception to prevent 
    /// communication issues due to incompatible protocol versions.
    /// </para>
    /// <para>
    /// The protocol uses a date-based versioning scheme in the format "YYYY-MM-DD".
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// // Check protocol version match
    /// if (result.ProtocolVersion != requestedVersion)
    /// {
    ///     throw new McpException($"Server protocol version mismatch. Expected {requestedVersion}, got {result.ProtocolVersion}");
    /// }
    /// </code>
    /// </para>
    /// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for version details</see>
    /// </remarks>
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    /// <summary>
    /// The server's capabilities.
    /// </summary>
    /// <remarks>
    /// Defines the features the server supports, such as tools, prompts, resources, logging, 
    /// and other protocol-specific functionality.
    /// </remarks>
    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; init; }

    /// <summary>
    /// Information about the server implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This information identifies the server during the initialization handshake.
    /// Clients may use this information for logging, debugging, or compatibility checks.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var result = new InitializeResult
    /// {
    ///     ServerInfo = new Implementation { Name = "MyMcpServer", Version = "1.0.0" },
    ///     ProtocolVersion = "2024-11-05",
    ///     Capabilities = new ServerCapabilities()
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonPropertyName("serverInfo")]
    public required Implementation ServerInfo { get; init; }

    /// <summary>
    /// Optional instructions for using the server and its features.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These instructions provide guidance to clients on how to effectively use the server's capabilities.
    /// They can include details about available tools, expected input formats, limitations,
    /// or any other information that helps clients interact with the server properly.
    /// </para>
    /// <para>
    /// Client applications often use these instructions as system messages for LLM interactions
    /// to provide context about available functionality.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var result = new InitializeResult
    /// {
    ///     // Other properties...
    ///     Instructions = "This server provides tools for data analysis. Use the 'analyzeData' tool with CSV input."
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }
}
