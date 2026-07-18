namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the mode of an SSE event stream.
/// </summary>
[Obsolete(Obsoletions.LegacyStatefulHttp_Message, DiagnosticId = Obsoletions.LegacyStatefulHttp_DiagnosticId, UrlFormat = Obsoletions.LegacyStatefulHttp_Url)]
public enum SseEventStreamMode
{
    /// <summary>
    /// Causes the event stream returned by <see cref="ISseEventStreamReader.ReadEventsAsync(System.Threading.CancellationToken)"/> to only end when
    /// the associated <see cref="ISseEventStreamWriter"/> gets disposed.
    /// </summary>
    Streaming = 0,

    /// <summary>
    /// Causes the event stream returned by <see cref="ISseEventStreamReader.ReadEventsAsync(System.Threading.CancellationToken)"/> to end
    /// after the most recent event has been consumed. This forces clients to keep making new requests in order to receive
    /// the latest messages.
    /// </summary>
    Polling = 1,
}
