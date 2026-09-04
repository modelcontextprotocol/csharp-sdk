#if NET
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides an MCP Streamable HTTP client transport over a child process's standard input and output.
/// </summary>
/// <remarks>
/// <para>
/// This transport launches a child process using <see cref="StdioClientTransportOptions"/>, then supplies
/// its standard output and input as a single connection through
/// <see cref="SocketsHttpHandler.ConnectCallback"/>. HTTP framing and MCP Streamable HTTP behavior are
/// provided by <see cref="SocketsHttpHandler"/> and <see cref="HttpClientTransport"/>.
/// </para>
/// <para>
/// The <see cref="HttpClientTransportOptions.Endpoint"/> is synthetic, but its authority and path are
/// used as the HTTP/2 <c>:authority</c> and <c>:path</c> values. Only cleartext, exact HTTP/2 is supported.
/// </para>
/// </remarks>
[Experimental(Experimentals.SpecificationFeature_DiagnosticId, UrlFormat = Experimentals.HttpOverStdio_Url)]
public sealed class HttpOverStdioClientTransport : IClientTransport
{
    private readonly StdioClientTransportOptions _stdioOptions;
    private readonly HttpClientTransportOptions _httpOptions;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpOverStdioClientTransport"/> class.
    /// </summary>
    /// <param name="stdioOptions">Options used to launch and manage the child server process.</param>
    /// <param name="httpOptions">Options used by the existing Streamable HTTP client transport.</param>
    /// <param name="loggerFactory">An optional logger factory for transport diagnostics.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stdioOptions"/> or <paramref name="httpOptions"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The HTTP endpoint is not cleartext HTTP or the transport mode is not
    /// <see cref="HttpTransportMode.StreamableHttp"/>.
    /// </exception>
    public HttpOverStdioClientTransport(
        StdioClientTransportOptions stdioOptions,
        HttpClientTransportOptions httpOptions,
        ILoggerFactory? loggerFactory = null)
    {
        Throw.IfNull(stdioOptions);
        Throw.IfNull(httpOptions);

        if (httpOptions.TransportMode is not HttpTransportMode.StreamableHttp)
        {
            throw new ArgumentException(
                $"{nameof(HttpOverStdioClientTransport)} requires {nameof(HttpClientTransportOptions)}." +
                $"{nameof(HttpClientTransportOptions.TransportMode)} to be {nameof(HttpTransportMode.StreamableHttp)}.",
                nameof(httpOptions));
        }

        if (!string.Equals(httpOptions.Endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{nameof(HttpOverStdioClientTransport)} requires a cleartext HTTP endpoint because TLS is not negotiated over stdio.",
                nameof(httpOptions));
        }

        _stdioOptions = stdioOptions;
        _httpOptions = httpOptions;
        _loggerFactory = loggerFactory;
        Name = httpOptions.Name ??
            stdioOptions.Name ??
            $"http-over-stdio-{Path.GetFileNameWithoutExtension(stdioOptions.Command)}";
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        StdioClientProcess process = StdioClientTransport.StartProcess(_stdioOptions, _loggerFactory, Name);
        HttpClient? resourceHttpClient = null;
        HttpClient? defaultOAuthBackchannel = null;

        try
        {
            var connectionClaimed = 0;
            var connectionStream = new HttpOverStdioDuplexStream(process.StandardOutput, process.StandardInput);
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = _httpOptions.ConnectionTimeout,
                EnableMultipleHttp2Connections = false,
                MaxConnectionsPerServer = 1,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                UseProxy = false,
                ConnectCallback = (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (Interlocked.Exchange(ref connectionClaimed, 1) != 0)
                    {
                        throw new IOException(
                            "The HTTP-over-stdio transport provides exactly one physical HTTP/2 connection.");
                    }

                    return ValueTask.FromResult<Stream>(connectionStream);
                },
            };

            resourceHttpClient = new HttpClient(new ExactHttp2Handler(handler))
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            if (_httpOptions.OAuth is { Backchannel: null })
            {
                defaultOAuthBackchannel = new HttpClient();
            }

            var httpTransport = new HttpClientTransport(
                _httpOptions,
                resourceHttpClient,
                defaultOAuthBackchannel,
                _loggerFactory,
                ownsHttpClient: false);
            ITransport innerTransport = await httpTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            return new HttpOverStdioClientSessionTransport(
                Name,
                innerTransport,
                process,
                resourceHttpClient,
                defaultOAuthBackchannel,
                _loggerFactory);
        }
        catch (Exception connectException)
        {
            Exception? cleanupException = null;
            try
            {
                resourceHttpClient?.Dispose();
                defaultOAuthBackchannel?.Dispose();
                process.CloseStandardInput();
                await process.WaitForExitWithinTimeoutAsync().ConfigureAwait(false);
                process.Dispose(processRunning: true);
            }
            catch (Exception ex)
            {
                cleanupException = ex;
            }

            if (cleanupException is not null)
            {
                throw new IOException(
                    "Failed to clean up the HTTP-over-stdio child process after connection setup failed.",
                    new AggregateException(connectException, cleanupException));
            }

            throw;
        }
    }

    private sealed class ExactHttp2Handler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Version = HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
#endif
