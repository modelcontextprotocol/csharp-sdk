using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ModelContextProtocol.Client;

internal class McpHttpClient(HttpClient httpClient)
{
    internal static readonly MediaTypeHeaderValue s_applicationJsonContentType = new("application/json");

    internal virtual async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, JsonRpcMessage? message, CancellationToken cancellationToken)
    {
        Debug.Assert(request.Content is null, "The request body should only be supplied as a JsonRpcMessage");
        Debug.Assert(message is null || request.Method == HttpMethod.Post, "All messages should be sent in POST requests.");

        using var content = CreatePostBodyContent(message);
        request.Content = content;
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private HttpContent? CreatePostBodyContent(JsonRpcMessage? message)
    {
        if (message is null)
        {
            return null;
        }

        // Buffer the serialized message so the request carries a Content-Length rather than
        // being streamed with Transfer-Encoding: chunked. JsonContent serializes lazily and
        // cannot report a length, so HttpClient falls back to chunked encoding, which some
        // hosts reject outright (for example the local Azure Functions Python worker).
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = s_applicationJsonContentType;
        return content;
    }
}
