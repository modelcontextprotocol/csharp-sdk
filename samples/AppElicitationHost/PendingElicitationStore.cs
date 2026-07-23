using ModelContextProtocol.Protocol;
using System.Text.Json;

public sealed class PendingElicitationStore
{
    private readonly object _gate = new();
    private PendingElicitation? _pending;

    public Task<ElicitResult> PublishAsync(
        ElicitRequestParams request,
        string resourceUri,
        string html,
        CancellationToken cancellationToken)
    {
        var pending = new PendingElicitation(Guid.NewGuid().ToString("N"), request, resourceUri, html);
        lock (_gate)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException("This minimal host supports one active elicitation at a time.");
            }
            _pending = pending;
        }

        cancellationToken.Register(() => pending.Completion.TrySetCanceled(cancellationToken));
        return AwaitAndClearAsync(pending);
    }

    public PendingElicitation? GetPending()
    {
        lock (_gate)
        {
            return _pending;
        }
    }

    public bool Complete(string id, ElicitResult result)
    {
        lock (_gate)
        {
            return _pending?.Id == id && _pending.Completion.TrySetResult(result);
        }
    }

    private async Task<ElicitResult> AwaitAndClearAsync(PendingElicitation pending)
    {
        try
        {
            return await pending.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }
            }
        }
    }
}

public sealed class PendingElicitation(
    string id,
    ElicitRequestParams request,
    string resourceUri,
    string html)
{
    public string Id { get; } = id;
    public ElicitRequestParams Request { get; } = request;
    public string ResourceUri { get; } = resourceUri;
    public string Html { get; } = html;
    public TaskCompletionSource<ElicitResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class SubmitElicitation
{
    public string Action { get; set; } = "cancel";
    public IDictionary<string, JsonElement>? Content { get; set; }
}
