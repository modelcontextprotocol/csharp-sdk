using System.Text.Json.Nodes;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Manages the MRTR (Multi Round-Trip Request) coordination between a handler and the pipeline.
/// When a handler calls <see cref="McpServer.ElicitAsync(ModelContextProtocol.Protocol.ElicitRequestParams, System.Threading.CancellationToken)"/> or
/// <see cref="McpServer.SampleAsync(ModelContextProtocol.Protocol.CreateMessageRequestParams, System.Threading.CancellationToken)"/>,
/// the handler writes to the channel and suspends on a TCS. The pipeline reads from the channel,
/// sends an <see cref="IncompleteResult"/>, and later completes the TCS when the retry arrives.
/// </summary>
internal sealed class MrtrContext
{
    /// <summary>
    /// The experimental capability key used by clients to signal MRTR support during initialization.
    /// </summary>
    internal const string ExperimentalCapabilityKey = "mrtr";

    private readonly Channel<MrtrExchange> _exchanges = Channel.CreateUnbounded<MrtrExchange>(
        new UnboundedChannelOptions { SingleReader = true });

    private int _nextInputRequestId;

    /// <summary>
    /// Gets the channel reader for consuming exchanges produced by the handler.
    /// </summary>
    public ChannelReader<MrtrExchange> ExchangeReader => _exchanges.Reader;

    /// <summary>
    /// Called by <see cref="McpServer.ElicitAsync(ModelContextProtocol.Protocol.ElicitRequestParams, System.Threading.CancellationToken)"/>
    /// or <see cref="McpServer.SampleAsync(ModelContextProtocol.Protocol.CreateMessageRequestParams, System.Threading.CancellationToken)"/>
    /// to request input from the client via the MRTR mechanism.
    /// </summary>
    /// <param name="inputRequest">The input request describing what the server needs.</param>
    /// <param name="cancellationToken">A token to cancel the wait for input.</param>
    /// <returns>The client's response to the input request.</returns>
    public async Task<InputResponse> RequestInputAsync(InputRequest inputRequest, CancellationToken cancellationToken)
    {
        var key = $"input_{Interlocked.Increment(ref _nextInputRequestId)}";

        var exchange = new MrtrExchange(key, inputRequest);

        await _exchanges.Writer.WriteAsync(exchange, cancellationToken).ConfigureAwait(false);

        return await exchange.ResponseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signals that the handler has completed normally.
    /// </summary>
    public void Complete() => _exchanges.Writer.TryComplete();

    /// <summary>
    /// Signals that the handler has faulted.
    /// </summary>
    public void Fault(Exception exception) => _exchanges.Writer.TryComplete(exception);
}

/// <summary>
/// Represents a single exchange between the handler and the pipeline during an MRTR flow.
/// The handler creates the exchange and awaits the response TCS. The pipeline reads the exchange,
/// sends the <see cref="InputRequest"/> to the client, and completes the TCS when the response arrives.
/// </summary>
internal sealed class MrtrExchange
{
    public MrtrExchange(string key, InputRequest inputRequest)
    {
        Key = key;
        InputRequest = inputRequest;
        ResponseTcs = new TaskCompletionSource<InputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// The unique key identifying this exchange within the MRTR round trip.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The input request that needs to be fulfilled by the client.
    /// </summary>
    public InputRequest InputRequest { get; }

    /// <summary>
    /// The TCS that will be completed with the client's response.
    /// </summary>
    public TaskCompletionSource<InputResponse> ResponseTcs { get; }
}

/// <summary>
/// Represents a continuation for a suspended MRTR handler, stored between round trips.
/// </summary>
internal sealed class MrtrContinuation
{
    public MrtrContinuation(Task<JsonNode?> handlerTask, MrtrContext mrtrContext, IReadOnlyList<MrtrExchange> pendingExchanges)
    {
        HandlerTask = handlerTask;
        MrtrContext = mrtrContext;
        PendingExchanges = pendingExchanges;
    }

    /// <summary>
    /// The handler task that is suspended awaiting input.
    /// </summary>
    public Task<JsonNode?> HandlerTask { get; }

    /// <summary>
    /// The MRTR context for the handler's async flow.
    /// </summary>
    public MrtrContext MrtrContext { get; }

    /// <summary>
    /// The exchanges that are awaiting responses from the client.
    /// </summary>
    public IReadOnlyList<MrtrExchange> PendingExchanges { get; }
}
