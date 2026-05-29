using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides data for the <see cref="IMcpTaskStore.InputResponseReceived"/> event.
/// </summary>
[Experimental(Experimentals.Extensions_DiagnosticId, UrlFormat = Experimentals.Extensions_Url)]
public sealed class InputResponseReceivedEventArgs
{
    /// <summary>
    /// Gets the task identifier.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Gets the request identifier that was resolved.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the response payload.
    /// </summary>
    public required JsonElement Response { get; init; }
}
