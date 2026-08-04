using ModelContextProtocol.Protocol;

namespace AspNetCoreMcpClient;

/// <summary>
/// Bridges an MCP elicitation request to an application-owned HTTP interaction.
/// </summary>
public sealed class ElicitationBroker
{
    private readonly Dictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public async ValueTask<ElicitResult> RequestAsync(
        string sessionId,
        ElicitRequestParams? request,
        CancellationToken cancellationToken)
    {
        var pending = new PendingRequest(Guid.NewGuid(), request);

        lock (_lock)
        {
            if (!_pending.TryAdd(sessionId, pending))
            {
                throw new InvalidOperationException("Only one elicitation can be pending for an application session.");
            }
        }

        try
        {
            return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (_pending.TryGetValue(sessionId, out var current) && ReferenceEquals(current, pending))
                {
                    _pending.Remove(sessionId);
                }
            }
        }
    }

    public PendingElicitation? GetPending(string sessionId)
    {
        lock (_lock)
        {
            return _pending.TryGetValue(sessionId, out var pending)
                ? new PendingElicitation(pending.Id, pending.Request)
                : null;
        }
    }

    public bool TryRespond(string sessionId, Guid requestId, ElicitResult response)
    {
        ArgumentNullException.ThrowIfNull(response);

        lock (_lock)
        {
            return _pending.TryGetValue(sessionId, out var pending) &&
                pending.Id == requestId &&
                pending.Completion.TrySetResult(response);
        }
    }

    public void Cancel(string sessionId)
    {
        lock (_lock)
        {
            if (_pending.Remove(sessionId, out var pending))
            {
                pending.Completion.TrySetCanceled();
            }
        }
    }

    private sealed class PendingRequest(Guid id, ElicitRequestParams? request)
    {
        public Guid Id { get; } = id;

        public ElicitRequestParams? Request { get; } = request;

        public TaskCompletionSource<ElicitResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed record PendingElicitation(Guid Id, ElicitRequestParams? Request);
