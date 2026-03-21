using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Manages the MRTR (Multi Round-Trip Request) coordination between a handler and the pipeline.
/// When a handler calls <see cref="McpServer.ElicitAsync(ModelContextProtocol.Protocol.ElicitRequestParams, System.Threading.CancellationToken)"/> or
/// <see cref="McpServer.SampleAsync(ModelContextProtocol.Protocol.CreateMessageRequestParams, System.Threading.CancellationToken)"/>,
/// the handler sets the exchange TCS and suspends on a response TCS. The pipeline detects the exchange
/// via <see cref="ExchangeTask"/>, sends an <see cref="IncompleteResult"/>, and later completes the
/// response TCS when the retry arrives.
/// </summary>
internal sealed class MrtrContext
{
    private TaskCompletionSource<MrtrExchange> _exchangeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _nextInputRequestId;

    /// <summary>
    /// Gets a task that completes when the handler produces an exchange (calls ElicitAsync/SampleAsync/RequestRootsAsync).
    /// </summary>
    public Task<MrtrExchange> ExchangeTask => _exchangeTcs.Task;

    /// <summary>
    /// Called by <see cref="McpServer.ElicitAsync(ModelContextProtocol.Protocol.ElicitRequestParams, System.Threading.CancellationToken)"/>
    /// or <see cref="McpServer.SampleAsync(ModelContextProtocol.Protocol.CreateMessageRequestParams, System.Threading.CancellationToken)"/>
    /// to request input from the client via the MRTR mechanism.
    /// </summary>
    /// <param name="inputRequest">The input request describing what the server needs.</param>
    /// <param name="cancellationToken">A token to cancel the wait for input.</param>
    /// <returns>The client's response to the input request.</returns>
    /// <exception cref="InvalidOperationException">A concurrent server-to-client request is already pending.</exception>
    public async Task<InputResponse> RequestInputAsync(InputRequest inputRequest, CancellationToken cancellationToken)
    {
        var tcs = _exchangeTcs;
        if (tcs.Task.IsCompleted)
        {
            throw new InvalidOperationException("Concurrent server-to-client requests are not supported. Await each ElicitAsync, SampleAsync, or RequestRootsAsync call before making another.");
        }

        var key = $"input_{Interlocked.Increment(ref _nextInputRequestId)}";
        var exchange = new MrtrExchange(key, inputRequest);
        tcs.TrySetResult(exchange);

        return await exchange.ResponseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares the context for the next round of exchange after a retry arrives.
    /// Must be called before completing the previous exchange's response TCS.
    /// </summary>
    public void ResetForNextExchange()
    {
        _exchangeTcs = new TaskCompletionSource<MrtrExchange>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
