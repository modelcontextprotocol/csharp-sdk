namespace ModelContextProtocol.Server;

/// <summary>
/// Configuration options for creating an SSE event stream.
/// </summary>
[Obsolete(Obsoletions.LegacyStatefulHttp_Message, DiagnosticId = Obsoletions.LegacyStatefulHttp_DiagnosticId, UrlFormat = Obsoletions.LegacyStatefulHttp_Url)]
public sealed class SseEventStreamOptions
{
    /// <summary>
    /// Gets or sets the session ID associated with the event stream.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the stream ID that uniquely identifies this stream within a session.
    /// </summary>
    public required string StreamId { get; set; }

    /// <summary>
    /// Gets or sets the mode of the event stream. Defaults to <see cref="SseEventStreamMode.Streaming"/>.
    /// </summary>
    public SseEventStreamMode Mode { get; set; }
}
