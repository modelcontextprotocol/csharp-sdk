using System.Net;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// HTTP server provider using HttpListener.
/// </summary>
internal sealed class HttpListenerServerProvider : IAsyncDisposable
{
    private static readonly byte[] s_accepted = "Accepted"u8.ToArray();

    private const string SseEndpoint = "/sse";
    private const string MessageEndpoint = "/message";

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private Task _listeningTask = Task.CompletedTask;

    private readonly TaskCompletionSource<bool> _completed = new();
    private int _outstandingOperations;

    private int _state;
    private const int StateNotStarted = 0;
    private const int StateRunning = 1;
    private const int StateStopped = 2;

    /// <summary>
    /// Creates a new instance of the HTTP server provider.
    /// </summary>
    /// <param name="port">The port to listen on</param>
    public HttpListenerServerProvider(int port)
    {
        if (port < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _listener = new();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public required Func<Stream, CancellationToken, Task> OnSseConnectionAsync { get; set; }
    public required Func<Stream, CancellationToken, Task<bool>> OnMessageAsync { get; set; }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, StateRunning, StateNotStarted) != StateNotStarted)
        {
            throw new ObjectDisposedException("Server may not be started twice.");
        }

        // Start listening for connections
        _listener.Start();

        OperationAdded(); // for the listening task
        _listeningTask = Task.Run(async () =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownTokenSource.Token);
                cts.Token.Register(_listener.Stop);
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync().ConfigureAwait(false);

                        // Process the request in a separate task
                        OperationAdded(); // for the processing task; decremented in ProcessRequestAsync
                        _ = Task.Run(() => ProcessRequestAsync(context, cts.Token), CancellationToken.None);
                    }
                    catch (Exception)
                    {
                        if (cts.IsCancellationRequested)
                        {
                            // Shutdown requested, exit gracefully
                            break;
                        }
                    }
                }
            }
            finally
            {
                OperationCompleted(); // for the listening task
            }
        }, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, StateStopped, StateRunning) != StateRunning)
        {
            return;
        }

        await _shutdownTokenSource.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _listeningTask.ConfigureAwait(false);
        await _completed.Task.ConfigureAwait(false);
    }

    /// <summary>Gets a <see cref="Task"/> that completes when the server has finished its work.</summary>
    public Task Completed => _completed.Task;

    private void OperationAdded() => Interlocked.Increment(ref _outstandingOperations);

    private void OperationCompleted()
    {
        if (Interlocked.Decrement(ref _outstandingOperations) == 0)
        {
            // All operations completed
            _completed.TrySetResult(true);
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            if (request is null || response is null)
            {
                return;
            }

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
                response.StatusCode = 500;
                response.Close();
            }
            catch { /* Ignore errors during error handling */ }
        }
        finally
        {
            OperationCompleted();
        }
    }

    private async Task HandleSseConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        // Keep the connection open until cancelled
        try
        {
            await OnSseConnectionAsync(response.OutputStream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            // Remove client on disconnect
            try
            {
                response.Close();
            }
            catch { /* Ignore errors during cleanup */ }
        }
    }

    private async Task HandleMessageAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // Process the message asynchronously
        if (await OnMessageAsync(request.InputStream, cancellationToken))
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
}
