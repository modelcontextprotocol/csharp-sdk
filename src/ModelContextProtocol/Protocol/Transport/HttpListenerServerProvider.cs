using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// HTTP server provider using HttpListener.
/// </summary>
[ExcludeFromCodeCoverage]
internal class HttpListenerServerProvider : IDisposable
{
    private static readonly byte[] s_accepted = "Accepted"u8.ToArray();

    private const string SseEndpoint = "/sse";
    private const string MessageEndpoint = "/message";

    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Func<string, CancellationToken, bool>? _messageHandler;
    private StreamWriter? _streamWriter;
    private bool _isRunning;

    /// <summary>
    /// Creates a new instance of the HTTP server provider.
    /// </summary>
    /// <param name="port">The port to listen on</param>
    public HttpListenerServerProvider(int port)
    {
        _port = port;
    }

    public Task<string> GetSseEndpointUri()
    {
        return Task.FromResult($"http://localhost:{_port}{SseEndpoint}");
    }

    public Task InitializeMessageHandler(Func<string, CancellationToken, bool> messageHandler)
    {
        _messageHandler = messageHandler;
        return Task.CompletedTask;
    }

    public async Task SendEvent(string data, string eventId)
    {
        if (_streamWriter == null)
        {
            throw new McpServerException("Stream writer not initialized");
        }
        if (eventId != null)
        {
            await _streamWriter.WriteLineAsync($"id: {eventId}").ConfigureAwait(false);
        }
        await _streamWriter.WriteLineAsync($"data: {data}").ConfigureAwait(false);
        await _streamWriter.WriteLineAsync().ConfigureAwait(false); // Empty line to finish the event
        await _streamWriter.FlushAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _isRunning = true;

        // Start listening for connections
        _ = Task.Run(() => ListenForConnectionsAsync(_cts.Token), cancellationToken).ConfigureAwait(false);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
            return Task.CompletedTask;

        _cts?.Cancel();
        _listener?.Stop();

        _streamWriter?.Close();

        _isRunning = false;
        return Task.CompletedTask;
    }

    private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
    {
        if (_listener == null)
        {
            throw new McpServerException("Listener not initialized");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);

                // Process the request in a separate task
                _ = Task.Run(() => ProcessRequestAsync(context, cancellationToken), cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown requested, exit gracefully
                break;
            }
            catch (Exception)
            {
                // Log error but continue listening
                if (!cancellationToken.IsCancellationRequested)
                {
                    // Continue listening if not shutting down
                    continue;
                }
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (request == null)
                throw new McpServerException("Request is null");

            // Handle SSE connection
            if (request.HttpMethod == "GET" && request.Url?.LocalPath == SseEndpoint)
            {
                await HandleSseConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            }
            // Handle message POST
            else if (request.HttpMethod == "POST" && request.Url?.LocalPath == MessageEndpoint)
            {
                await HandleMessageAsync(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Not found
                response.StatusCode = 404;
                response.Close();
            }
        }
        catch (Exception)
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { /* Ignore errors during error handling */ }
        }
    }

    private async Task HandleSseConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        // Get the output stream and create a StreamWriter
        var outputStream = response.OutputStream;
        _streamWriter = new StreamWriter(outputStream, Encoding.UTF8) { AutoFlush = true };

        // Keep the connection open until cancelled
        try
        {
            // Immediately send the "endpoint" event with the POST URL
            await _streamWriter.WriteLineAsync("event: endpoint").ConfigureAwait(false);
            await _streamWriter.WriteLineAsync($"data: {MessageEndpoint}").ConfigureAwait(false);
            await _streamWriter.WriteLineAsync().ConfigureAwait(false); // blank line to end an SSE message
            await _streamWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Keep the connection open by "pinging" or just waiting
            // until the client disconnects or the server is canceled.
            while (!cancellationToken.IsCancellationRequested && response.OutputStream.CanWrite)
            {
                // Do a periodic no-op to keep connection alive:
                await _streamWriter.WriteLineAsync(": keep-alive").ConfigureAwait(false);
                await _streamWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception)
        {
            // Client disconnected or other error
        }
        finally
        {
            // Remove client on disconnect
            try
            {
                _streamWriter.Close();
                response.Close();
            }
            catch { /* Ignore errors during cleanup */ }
        }
    }

    private async Task HandleMessageAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // Read the request body
        string requestBody;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        // Process the message asynchronously
        if (_messageHandler != null && _messageHandler(requestBody, cancellationToken))
        {
            // Return 202 Accepted
            response.StatusCode = 202;

            // Write "accepted" response
            await response.OutputStream.WriteAsync(s_accepted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Return 400 Bad Request
            response.StatusCode = 400;
        }

        response.Close();
    }


    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    protected virtual void Dispose(bool disposing)
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
        _listener?.Close();
    }
}
