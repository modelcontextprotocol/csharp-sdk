using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.Initialize"/> request sent by a client to a server during the protocol handshake.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="InitializeRequestParams"/> is the first message sent in the Model Context Protocol
/// communication flow. It establishes the connection between client and server, negotiates the protocol
/// version, and declares the client's capabilities.
/// </para>
/// <para>
/// After sending this request, the client should wait for an <see cref="InitializeResult"/> response
/// before sending an <see cref="NotificationMethods.InitializedNotification"/> notification to complete the handshake.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class InitializeRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the version of the Model Context Protocol that the client wants to use.
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
    /// See the <see href="https://spec.modelcontextprotocol.io/specification/">protocol specification</see> for version details.
    /// </para>
    /// </remarks>
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the client's capabilities.
    /// </summary>
    /// <remarks>
    /// Capabilities define the features the client supports, such as "sampling" or "roots".
    /// </remarks>
    [JsonPropertyName("capabilities")]
    public required ClientCapabilities Capabilities { get; set; }

    /// <summary>
    /// Gets or sets information about the client implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// This information is required during the initialization handshake to identify the client.
    /// Servers might use this information for logging, debugging, or compatibility checks.
    /// </remarks>
    [JsonPropertyName("clientInfo")]
    public required Implementation ClientInfo { get; set; }
}
