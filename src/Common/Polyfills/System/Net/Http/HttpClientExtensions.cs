#if !NET
using ModelContextProtocol;

namespace System.Net.Http;

internal static class HttpClientExtensions
{
    public static async Task<Stream> ReadAsStreamAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        Throw.IfNull(content);

        cancellationToken.ThrowIfCancellationRequested();
        return await content.ReadAsStreamAsync();
    }

    public static async Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        Throw.IfNull(content);

        cancellationToken.ThrowIfCancellationRequested();
        return await content.ReadAsStringAsync();
    }
}
#endif