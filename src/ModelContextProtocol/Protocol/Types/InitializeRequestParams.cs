using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Parameters for an initialization request sent to the server during the protocol handshake.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="InitializeRequestParams"/> is the first message sent in the Model Context Protocol
/// communication flow. It establishes the connection between client and server, negotiates the protocol
/// version, and declares the client's capabilities.
/// </para>
/// <para>
/// After sending this request, the client should wait for an <see cref="InitializeResult"/> response
/// before sending an initialized notification to complete the handshake.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var response = await client.SendRequestAsync(
///     RequestMethods.Initialize,
///     new InitializeRequestParams
///     {
///         ProtocolVersion = "2024-11-05",
///         ClientInfo = new Implementation { Name = "MyMcpClient", Version = "1.0.0" },
///         Capabilities = new ClientCapabilities()
///     },
///     McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
///     McpJsonUtilities.JsonContext.Default.InitializeResult,
///     cancellationToken
/// );
/// </code>
/// </para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </remarks>
public class InitializeRequestParams : RequestParams
{
    /// <summary>
    /// The version of the Model Context Protocol that the client wants to use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Protocol version is specified using a date-based versioning scheme in the format "YYYY-MM-DD".
    /// The client and server must agree on a protocol version to communicate successfully.
    /// </para>
    /// <para>
    /// During initialization, the server will check if it supports this requested version. If there's a 
    /// mismatch, the server will reject the connection with a version mismatch error.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var params = new InitializeRequestParams
    /// {
    ///     ProtocolVersion = "2024-11-05", // November 5, 2024 protocol version
    ///     ClientInfo = new Implementation { Name = "MyClient", Version = "1.0" }
    /// };
    /// </code>
    /// </para>
    /// <see href="https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle/">See the protocol specification for version details</see>
    /// </remarks>
    [JsonPropertyName("protocolVersion")]

    public required string ProtocolVersion { get; init; }
    /// <summary>
    /// The client's capabilities.
    /// </summary>
    /// <remarks>
    /// Capabilities define the features the client supports, such as sampling, roots, completion,
    /// and other protocol-specific functionality.
    /// </remarks>
    [JsonPropertyName("capabilities")]
    public ClientCapabilities? Capabilities { get; init; }

    /// <summary>
    /// Information about the client implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This information is required during the initialization handshake to identify the client.
    /// Servers may use this information for logging, debugging, or compatibility checks.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var params = new InitializeRequestParams
    /// {
    ///     ClientInfo = new Implementation { Name = "MyMcpClient", Version = "1.0.0" },
    ///     ProtocolVersion = "2024-11-05"
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonPropertyName("clientInfo")]
    public required Implementation ClientInfo { get; init; }
}
