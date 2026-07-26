using ModelContextProtocol.Protocol;
using System.Threading.Channels;

namespace ModelContextProtocol.Client;

/// <summary>
/// An <see cref="IOException"/> that indicates the client transport was closed, carrying
/// structured <see cref="ClientCompletionDetails"/> about why the closure occurred.
/// </summary>
/// <remarks>
/// <para>
/// This exception is thrown when an MCP transport closes, either during initialization
/// (e.g., from <see cref="McpClient.CreateAsync"/>) or during an active session.
/// Callers can catch this exception to access the <see cref="Details"/> property
/// for structured information about the closure.
/// </para>
/// <para>
/// For stdio-based transports, the <see cref="Details"/> will be a
/// <see cref="StdioClientCompletionDetails"/> instance providing access to the
/// server process exit code, process ID, and standard error output.
/// </para>
/// <para>
/// Custom <see cref="ITransport"/> implementations can provide their own
/// <see cref="ClientCompletionDetails"/>-derived types by completing their
/// <see cref="ChannelWriter{T}"/> with this exception.
/// </para>
/// </remarks>
public sealed class ClientTransportClosedException(ClientCompletionDetails details) :
    IOException(details.Exception?.Message ?? "The transport was closed.", details.Exception)
{
    /// <summary>
    /// Gets the structured details about why the transport was closed.
    /// </summary>
    /// <remarks>
    /// The concrete type of the returned <see cref="ClientCompletionDetails"/> depends on
    /// the transport that was used. For example, <see cref="StdioClientCompletionDetails"/>
    /// for stdio-based transports and <see cref="HttpClientCompletionDetails"/> for HTTP-based transports.
    /// </remarks>
    public ClientCompletionDetails Details { get; } = details;
}
