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
    /// Supported values are <c>2024-11-05</c>, <c>2025-03-26</c>, <c>2025-06-18</c>, <c>2025-11-25</c>,
    /// and <c>2026-07-28</c>.
    /// </para>
    /// <para>
    /// When <see langword="null"/> (the default), the client prefers the latest revision (<c>2026-07-28</c>),
    /// which removed the <c>initialize</c> handshake and Streamable HTTP sessions. It probes with
    /// <c>server/discover</c> and automatically falls back to the <c>initialize</c> handshake,
    /// downgrading to an initialize-capable version the server advertises, when the server does not support that revision.
    /// </para>
    /// <para>
    /// When non-<see langword="null"/>, this value is both the requested version and the minimum the client
    /// will accept: the client requests exactly this version and refuses to downgrade below it, throwing an
    /// <see cref="McpException"/> instead of falling back. Setting it to <c>2026-07-28</c> therefore disables
    /// the automatic initialize-handshake server fallback, and setting it to a version that still supports Streamable HTTP
    /// sessions, such as <c>2025-11-25</c>, forces the <c>initialize</c> handshake and fails if the server
    /// negotiates a different version. To try more than one version, leave this unset for automatic fallback
    /// or retry the connection with a different value.
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
    /// Gets or sets the timeout applied to the <c>server/discover</c> probe that the client issues
    /// before falling back to the <c>initialize</c> handshake.
    /// </summary>
    /// <value>
    /// The probe timeout. The default value is 5 seconds. Use
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable the separate probe timeout
    /// and rely solely on <see cref="InitializationTimeout"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// This timeout only has an effect when the client prefers the <c>2026-07-28</c> protocol revision, that is,
    /// when <see cref="ProtocolVersion"/> is <see langword="null"/> (the default) or <c>2026-07-28</c>.
    /// In that mode the client first probes the server with a
    /// <c>server/discover</c> request. A server that predates the <c>2026-07-28</c> revision may
    /// silently drop the unknown method, so the probe is bounded by this timeout; when it elapses the
    /// client concludes the server requires <c>initialize</c> and falls back to that handshake on the
    /// same connection. When the caller pins an initialize-capable <see cref="ProtocolVersion"/>, no probe is issued
    /// and this value has no effect.
    /// </para>
    /// <para>
    /// The default is intentionally short so that dual-path clients fall back quickly against initialize-handshake
    /// servers. Increase it for high-latency environments (for example, cold-start serverless peers or
    /// satellite links) where a short probe could trigger the initialize fallback before a server on the
    /// per-request metadata revision has had a chance to respond. The probe is always also bounded by
    /// <see cref="InitializationTimeout"/>, which governs the overall connect budget: if this value is
    /// greater than or equal to <see cref="InitializationTimeout"/>, the probe is effectively bounded by
    /// <see cref="InitializationTimeout"/> alone.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not positive and is not <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    public TimeSpan DiscoverProbeTimeout
    {
        get;
        set
        {
            if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "must be positive or Timeout.InfiniteTimeSpan.");
            }
            field = value;
        }
    } = TimeSpan.FromSeconds(5);

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
