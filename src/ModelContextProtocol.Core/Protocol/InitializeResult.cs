using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a <see cref="RequestMethods.Initialize"/> request sent to the server during connection establishment.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="InitializeResult"/> is sent by the server in response to an <see cref="InitializeRequestParams"/>
/// message from the client. It contains information about the server, its capabilities, and the protocol version
/// that will be used for the session.
/// </para>
/// <para>
/// After receiving this response, the client should send an <see cref="NotificationMethods.InitializedNotification"/>
/// notification to complete the handshake.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class InitializeResult : Result
{
    /// <summary>
    /// Gets or sets the version of the Model Context Protocol that the server will use for this session.
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
    /// See the <see href="https://spec.modelcontextprotocol.io/specification/">protocol specification</see> for version details.
    /// </para>
    /// </remarks>
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the server's capabilities.
    /// </summary>
    /// <remarks>
    /// This property defines the features the server supports, such as "tools", "prompts", "resources", or "logging",
    /// and other protocol-specific functionality.
    /// </remarks>
    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; set; }

    /// <summary>
    /// Gets or sets information about the server implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// This information identifies the server during the initialization handshake.
    /// Clients might use this information for logging, debugging, or compatibility checks.
    /// </remarks>
    [JsonPropertyName("serverInfo")]
    public required Implementation ServerInfo { get; set; }

    /// <summary>
    /// Gets or sets optional instructions for using the server and its features.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These instructions should focus on guidance that helps a model use the server effectively,
    /// such as workflow tips, capability relationships, and server-specific conventions.
    /// They should avoid repeating tool descriptions, prompt descriptions, or resource descriptions
    /// that are already available through other protocol responses.
    /// </para>
    /// <para>
    /// Client applications often use these instructions as system messages for LLM interactions
    /// to provide context about available functionality.
    /// </para>
    /// </remarks>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }
}
