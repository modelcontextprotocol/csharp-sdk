using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides configuration options for creating <see cref="McpClient"/> instances.
/// </summary>
/// <remarks>
/// These options are typically passed to <see cref="McpClient.CreateAsync"/> when creating a client.
/// They define client capabilities, protocol version, and other client-specific settings.
/// </remarks>
public sealed class McpClientOptions
{
    /// <summary>
    /// Gets or sets information about this client implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This information is sent to the server during initialization to identify the client.
    /// It's often displayed in server logs and can be used for debugging and compatibility checks.
    /// </para>
    /// <para>
    /// When not specified, information sourced from the current process is used.
    /// </para>
    /// </remarks>
    public Implementation? ClientInfo { get; set; }

    /// <summary>
    /// Gets or sets the client capabilities to advertise to the server.
    /// </summary>
    public ClientCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Gets or sets the metadata to include in the <c>_meta</c> field of the <see cref="RequestMethods.Initialize"/> request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, this value is sent as <see cref="RequestParams.Meta"/> on the <see cref="InitializeRequestParams"/> during the initialization handshake.
    /// This allows passing implementation-specific data to the server alongside the standard <c>initialize</c> parameters,
    /// such as authentication context a server validates before completing the handshake.
    /// </para>
    /// <para>
    /// When <see langword="null"/>, no <c>_meta</c> field is sent.
    /// </para>
    /// </remarks>
    public JsonObject? InitializeMeta { get; set; }

    /// <summary>
    /// Gets or sets the protocol version to request from the server, using a date-based versioning scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The protocol version is a key part of the initialization handshake. The client and server must
    /// agree on a compatible protocol version to communicate successfully.
    /// </para>
    /// <para>
    /// If non-<see langword="null"/>, this version will be sent to the server, and the handshake
    /// will fail if the version in the server's response does not match this version.
    /// If <see langword="null"/>, the client will request the latest version supported by the server
    /// but will allow any supported version that the server advertises in its response.
    /// </para>
    /// </remarks>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets a timeout for the client-server initialization handshake sequence.
    /// </summary>
    /// <value>
    /// The timeout for the client-server initialization handshake sequence. The default value is 60 seconds.
    /// </value>
    /// <remarks>
    /// <para>
    /// This timeout determines how long the client will wait for the server to respond during
    /// the initialization protocol handshake. If the server doesn't respond within this timeframe,
    /// an exception is thrown.
    /// </para>
    /// <para>
    /// Setting an appropriate timeout prevents the client from hanging indefinitely when
    /// connecting to unresponsive servers.
    /// </para>
    /// </remarks>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the container of handlers used by the client for processing protocol messages.
    /// </summary>
    public McpClientHandlers Handlers
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of consecutive task polls during which a task may report
    /// <see cref="McpTaskStatus.InputRequired"/> without publishing any new input requests, before
    /// the client treats the task as stuck, issues a best-effort <c>tasks/cancel</c>, and throws
    /// an <see cref="McpException"/>.
    /// </summary>
    /// <value>
    /// The maximum number of consecutive stuck polls allowed. The default value is <c>60</c>.
    /// </value>
    /// <remarks>
    /// <para>
    /// This guard prevents an unbounded poll loop when the server keeps a task in
    /// <see cref="McpTaskStatus.InputRequired"/> but never publishes new input requests after the
    /// client has already responded to every previously surfaced request. It only affects the
    /// long-poll path used by <see cref="McpClient.CallToolAsync(CallToolRequestParams, CancellationToken)"/>;
    /// it does not affect direct <see cref="McpClient.GetTaskAsync(string, CancellationToken)"/> calls.
    /// </para>
    /// <para>
    /// Callers should size this value with the configured server-side poll interval in mind: the
    /// effective wall-clock timeout is roughly <c>MaxConsecutiveStuckPolls * pollIntervalMs</c>.
    /// Setting this to a very small value can cause false positives for servers that are slow to
    /// surface follow-up input requests; setting it too large can mask misbehaving servers.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than <c>1</c>.</exception>
    public int MaxConsecutiveStuckPolls
    {
        get;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "must be greater than or equal to 1.");
            }
            field = value;
        }
    } = 60;
}
