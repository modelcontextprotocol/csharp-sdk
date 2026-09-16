using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a single exchange between the handler and the pipeline during an MRTR flow.
/// The handler creates the exchange and awaits the response TCS. The pipeline reads the exchange,
/// sends the <see cref="InputRequest"/> to the client, and completes the TCS when the response arrives.
/// </summary>
internal sealed class MrtrExchange
{
    public MrtrExchange(string key, InputRequest inputRequest, TaskCompletionSource<MrtrExchange> sourceTcs)
    {
        Key = key;
        InputRequest = inputRequest;
        SourceTcs = sourceTcs;
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
    /// The <see cref="TaskCompletionSource{TResult}"/> that this exchange was set as the result of.
    /// Used by <see cref="MrtrContext.ResetForNextExchange"/> on retry to validate
    /// the expected state via <see cref="Interlocked.CompareExchange{T}"/>.
    /// </summary>
    internal TaskCompletionSource<MrtrExchange> SourceTcs { get; }

    /// <summary>
    /// The TCS that will be completed with the client's response.
    /// </summary>
    public TaskCompletionSource<InputResponse> ResponseTcs { get; }
}
