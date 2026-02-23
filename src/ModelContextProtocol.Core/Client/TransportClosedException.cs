using ModelContextProtocol.Protocol;
using System.Threading.Channels;

namespace ModelContextProtocol.Client;

/// <summary>
/// <see cref="IOException"/> used to smuggle <see cref="ClientCompletionDetails"/> through
/// the <see cref="ChannelWriter{T}.TryComplete(Exception?)"/> mechanism.
/// </summary>
/// <remarks>
/// This could be made public in the future to allow custom <see cref="ITransport"/>
/// implementations to provide their own <see cref="ClientCompletionDetails"/>-derived types
/// by completing their channel with this exception.
/// </remarks>
internal sealed class TransportClosedException(ClientCompletionDetails details) :
    IOException(details.Exception?.Message, details.Exception)
{
    public ClientCompletionDetails Details { get; } = details;
}
