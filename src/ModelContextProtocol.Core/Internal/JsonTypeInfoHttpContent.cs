using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Internal;

internal sealed class JsonTypeInfoHttpContent<T> : HttpContent
{
    private readonly T _value;
    private readonly JsonTypeInfo<T> _typeInfo;

    public JsonTypeInfoHttpContent(T value, JsonTypeInfo<T> typeInfo)
    {
        _value = value;
        _typeInfo = typeInfo;

        // Match StringContent's default behavior (application/json; charset=utf-8).
        Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
    }

#if NET
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(stream, _value, _typeInfo, cancellationToken);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);
#else
    // HttpContent.SerializeToStreamAsync does not provide a CancellationToken on non-NET TFMs.
    // Cancellation can still abort the underlying HTTP request, but it won't interrupt serialization itself.
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        JsonSerializer.SerializeAsync(stream, _value, _typeInfo, CancellationToken.None);
#endif

    protected override bool TryComputeLength(out long length)
    {
        // Intentionally unknown length to avoid buffering the entire JSON payload just to compute Content-Length.
        length = 0;
        return false;
    }
}
