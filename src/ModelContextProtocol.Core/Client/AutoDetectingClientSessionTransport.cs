using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Net;
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
            else
            {
                // Streamable HTTP failed. Capture the underlying error (status + body) before falling back to SSE
                // so that, if SSE also fails, we can surface the real Streamable HTTP diagnostic to the caller
                // instead of dropping it on the floor (see https://github.com/modelcontextprotocol/csharp-sdk/issues/1526).
                LogStreamableHttpFailed(_name, response.StatusCode);

                var streamableHttpError = await CreateStreamableHttpErrorAsync(response, cancellationToken).ConfigureAwait(false);

                await streamableHttpTransport.DisposeAsync().ConfigureAwait(false);
                await InitializeSseTransportAsync(message, streamableHttpError, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // If nothing threw inside the try block, we've either set streamableHttpTransport as the
            // ActiveTransport, or else we will have disposed it in the !IsSuccessStatusCode else block.
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
            // SSE fallback also failed. Surface the original Streamable HTTP error as the primary failure
            // so the user sees the real server diagnostic (e.g. 415 Unsupported Media Type) instead of the
            // unrelated SSE-fallback error (e.g. a 405 from a Streamable-HTTP-only server that doesn't accept GET).
            await sseTransport.DisposeAsync().ConfigureAwait(false);
            LogSseFallbackFailedAfterStreamableHttp(_name, sseError);
            throw new AggregateException(
                "Streamable HTTP transport failed and the SSE fallback also failed. " +
                "The first inner exception is the original Streamable HTTP error (the real server response); " +
                "the second is the SSE fallback failure.",
                streamableHttpError,
                sseError);
        }
        catch
        {
            await sseTransport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<HttpRequestException> CreateStreamableHttpErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Best-effort read of the response body so the exception message includes the server's diagnostic
        // (e.g. "Content-Type must be 'application/json'" on a 415). Mirrors EnsureSuccessStatusCodeWithResponseBodyAsync.
        string? responseBody = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            const int MaxResponseBodyLength = 1024;
            if (responseBody.Length > MaxResponseBodyLength)
            {
                responseBody = responseBody.Substring(0, MaxResponseBodyLength) + "...";
            }
        }
        catch
        {
            // Ignore all errors reading the response body (e.g., stream closed, timeout, cancellation) - we'll throw without it.
        }

        return HttpResponseMessageExtensions.CreateHttpRequestException(response, responseBody);
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
