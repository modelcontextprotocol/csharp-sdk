using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Net.Http;
using System.Threading.Channels;

namespace ModelContextProtocol.Client;

/// <summary>
/// A transport that automatically detects whether to use Streamable HTTP or SSE transport
/// by trying Streamable HTTP first and falling back to SSE if that fails.
/// </summary>
internal sealed partial class AutoDetectingClientSessionTransport : ITransport
{
    private readonly HttpClientTransportOptions _options;
    private readonly McpHttpClient _httpClient;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly Channel<JsonRpcMessage> _messageChannel;

    public AutoDetectingClientSessionTransport(string endpointName, HttpClientTransportOptions transportOptions, McpHttpClient httpClient, ILoggerFactory? loggerFactory)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _logger = (ILogger?)loggerFactory?.CreateLogger<AutoDetectingClientSessionTransport>() ?? NullLogger.Instance;
        _name = endpointName;

        // Same as TransportBase.cs.
        _messageChannel = Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// Returns the active transport (either StreamableHttp or SSE)
    /// </summary>
    internal ITransport? ActiveTransport { get; private set; }

    public ChannelReader<JsonRpcMessage> MessageReader => _messageChannel.Reader;

    string? ITransport.SessionId => ActiveTransport?.SessionId;

    /// <inheritdoc/>
    public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (ActiveTransport is null)
        {
            return InitializeAsync(message, cancellationToken);
        }

        return ActiveTransport.SendMessageAsync(message, cancellationToken);
    }

    private async Task InitializeAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        // Try StreamableHttp first
        var streamableHttpTransport = new StreamableHttpClientSessionTransport(_name, _options, _httpClient, _messageChannel, _loggerFactory);

        try
        {
            LogAttemptingStreamableHttp(_name);
            using var response = await streamableHttpTransport.SendHttpRequestAsync(message, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                LogUsingStreamableHttp(_name);
                ActiveTransport = streamableHttpTransport;
            }
            else if (await StreamableHttpClientSessionTransport.TryReadJsonRpcErrorAsync(response, cancellationToken).ConfigureAwait(false) is { } parsedError)
            {
                // A JSON-RPC error envelope in the body means the peer IS a Streamable HTTP server.
                // It just rejected our specific request (e.g., -32022 UnsupportedProtocolVersion,
                // -32021 MissingRequiredClientCapability, -32020 HeaderMismatch, or any other
                // application-level error). Don't fall back to SSE — that would mask the real signal
                // and surface a misleading "session id required" error from the SSE GET path.
                // Adopt the Streamable HTTP transport and throw the structured exception so the
                // connect-time fallback logic can react per spec PR #2844. Setting ActiveTransport
                // first makes the catch filter below leave the now-owned transport alone.
                LogUsingStreamableHttp(_name);
                ActiveTransport = streamableHttpTransport;
                throw McpSessionHandler.CreateRemoteProtocolExceptionFromError(parsedError);
            }
            else
            {
                // Non-JSON-RPC error response: either the server doesn't speak MCP at all, or this
                // is an older deployment that expects the SSE transport (which establishes its
                // protocol via GET /sse rather than POST). Fall back to SSE per the original
                // behavior. Capture the underlying error (status + body) before falling back so that,
                // if SSE also fails, we can surface the real Streamable HTTP diagnostic to the caller
                // instead of dropping it on the floor (see https://github.com/modelcontextprotocol/csharp-sdk/issues/1526).
                LogStreamableHttpFailed(_name, response.StatusCode);

                // This reads the response body a second time for the application/json case, where
                // TryReadJsonRpcErrorAsync above already read it. HttpContent buffers after the first
                // read, so this returns the same buffered content and is safe (not a second stream
                // consumption). For the common non-JSON error responses (415, 405, plain text)
                // TryReadJsonRpcErrorAsync returns early on the content type, so there is no double read.
                var streamableHttpError = await HttpResponseMessageExtensions.CreateHttpRequestExceptionWithBodyAsync(response, cancellationToken).ConfigureAwait(false);

                await streamableHttpTransport.DisposeAsync().ConfigureAwait(false);
                await InitializeSseTransportAsync(message, streamableHttpError, cancellationToken).ConfigureAwait(false);
            }
        }
        catch when (ActiveTransport is null)
        {
            // Only dispose the Streamable HTTP transport when we didn't adopt it. If we set
            // ActiveTransport above (success path OR structured-error path), the transport's
            // lifetime is owned by the outer transport from this point on.
            await streamableHttpTransport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task InitializeSseTransportAsync(JsonRpcMessage message, HttpRequestException? streamableHttpError, CancellationToken cancellationToken)
    {
        if (_options.KnownSessionId is not null)
        {
            throw new InvalidOperationException("Streamable HTTP transport is required to resume an existing session.");
        }

        var sseTransport = new SseClientSessionTransport(_name, _options, _httpClient, _messageChannel, _loggerFactory);

        try
        {
            LogAttemptingSSE(_name);
            await sseTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await sseTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            LogUsingSSE(_name);
            ActiveTransport = sseTransport;
        }
        catch (Exception sseError) when (streamableHttpError is not null && sseError is not OperationCanceledException)
        {
            // SSE fallback also failed. Surface the original Streamable HTTP error as the primary failure so the
            // user sees the real server diagnostic (e.g. 415 Unsupported Media Type) instead of the unrelated
            // SSE-fallback error (e.g. a 405 from a Streamable-HTTP-only server that doesn't accept GET). Preserve
            // the original status code and attach the SSE failure as the inner exception so neither is lost, and
            // keep HttpRequestException as the surfaced type so existing callers can still catch it and read StatusCode.
            await sseTransport.DisposeAsync().ConfigureAwait(false);
            LogSseFallbackFailedAfterStreamableHttp(_name, sseError);
#if NET
            throw new HttpRequestException(streamableHttpError.Message, sseError, streamableHttpError.StatusCode);
#else
            // net472 has no HttpRequestException overload that carries a status code, so this target
            // preserves the status text in the message but not a programmatic StatusCode. Preserving the
            // status code is intentionally net5+ only rather than an oversight.
            throw new HttpRequestException(streamableHttpError.Message, sseError);
#endif
        }
        catch
        {
            await sseTransport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ActiveTransport is not null)
            {
                await ActiveTransport.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // In the majority of cases, either the Streamable HTTP transport or SSE transport has completed the channel by now.
            // However, this may not be the case if HttpClient throws during the initial request due to misconfiguration.
            _messageChannel.Writer.TryComplete();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} attempting to connect using Streamable HTTP transport.")]
    private partial void LogAttemptingStreamableHttp(string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} streamable HTTP transport failed with status code {StatusCode}, falling back to SSE transport.")]
    private partial void LogStreamableHttpFailed(string endpointName, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} using Streamable HTTP transport.")]
    private partial void LogUsingStreamableHttp(string endpointName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} attempting to connect using SSE transport.")]
    private partial void LogAttemptingSSE(string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} using SSE transport.")]
    private partial void LogUsingSSE(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} SSE fallback failed after Streamable HTTP also failed; surfacing both errors.")]
    private partial void LogSseFallbackFailedAfterStreamableHttp(string endpointName, Exception sseError);
}
