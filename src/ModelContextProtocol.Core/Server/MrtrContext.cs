using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Manages the MRTR (Multi Round-Trip Request) coordination between a handler and the pipeline.
/// When a handler calls <see cref="McpServer.ElicitAsync(ModelContextProtocol.Protocol.ElicitRequestParams, System.Threading.CancellationToken)"/> or
/// <see cref="McpServer.SampleAsync(ModelContextProtocol.Protocol.CreateMessageRequestParams, System.Threading.CancellationToken)"/>,
/// the handler sets the exchange TCS and suspends on a response TCS. The pipeline detects the exchange
/// via <see cref="InitialExchangeTask"/> or the task returned by <see cref="ResetForNextExchange"/>,
/// sends an <see cref="IncompleteResult"/>, and later completes the response TCS when the retry arrives.
/// </summary>
internal sealed class MrtrContext
{
    private TaskCompletionSource<MrtrExchange> _exchangeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _nextInputRequestId;

    /// <summary>
    /// Gets the task for the initial MRTR exchange. Set once in the constructor and never changes.
    /// For subsequent exchanges after a retry, use the task returned by <see cref="ResetForNextExchange"/>.
    /// </summary>
    public Task<MrtrExchange> InitialExchangeTask { get; }

    public MrtrContext()
    {
        InitialExchangeTask = _exchangeTcs.Task;
    }

    /// <summary>
    /// Prepares the context for the next round of exchange after a retry arrives.
    /// Uses <see cref="Interlocked.CompareExchange{T}"/> to atomically validate that
    /// <see cref="_exchangeTcs"/> still references the TCS that produced <paramref name="previousExchange"/>,
    /// ensuring concurrent calls reliably fail.
    /// </summary>
    /// <param name="previousExchange">The exchange from the previous round whose
    /// response has been (or is about to be) completed.</param>
    /// <returns>A task that completes when the handler requests input via
    /// <see cref="RequestInputAsync"/>.</returns>
    /// <exception cref="InvalidOperationException">The context state was modified concurrently.</exception>
    public Task<MrtrExchange> ResetForNextExchange(MrtrExchange previousExchange)
    {
        var newTcs = new TaskCompletionSource<MrtrExchange>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _exchangeTcs, newTcs, previousExchange.SourceTcs) != previousExchange.SourceTcs)
        {
            throw new InvalidOperationException("MrtrContext was modified concurrently.");
        }

        return newTcs.Task;
    }

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
        var key = $"input_{Interlocked.Increment(ref _nextInputRequestId)}";
        var tcs = _exchangeTcs;
        var exchange = new MrtrExchange(key, inputRequest, tcs);

        // TrySetResult is the sole atomicity gate. If it returns false,
        // the TCS was already completed by a prior call — concurrent exchanges
        // are not supported.
        if (!tcs.TrySetResult(exchange))
        {
            throw new InvalidOperationException(
                "Concurrent server-to-client requests are not supported. " +
                "Await each ElicitAsync, SampleAsync, or RequestRootsAsync call before making another.");
        }

        return await exchange.ResponseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
