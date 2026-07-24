using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

#pragma warning disable MCPEXP002
internal sealed class ComposedCallToolInvocationState
{
    private readonly object _sync = new();
    private readonly List<ToolCallLifecycle> _pendingOrdinaryLifecycles = [];
    private bool _outerCompleted;
    private bool _outerReturnedAlternate;

    public IReadOnlyList<ToolCallLifecycle> RecordOrdinaryResult(CallToolResult result) =>
        RecordOrdinaryLifecycle(new(result, null, false));

    public IReadOnlyList<ToolCallLifecycle> RecordOrdinaryException(
        Exception exception,
        bool cancellationRequested) =>
        RecordOrdinaryLifecycle(new(null, exception, cancellationRequested));

    public IReadOnlyList<ToolCallLifecycle> CompleteOuter(ResultOrAlternate<CallToolResult> result)
    {
        lock (_sync)
        {
            if (result.IsAlternate)
            {
                _outerReturnedAlternate = true;
                return DrainPendingLifecycles();
            }

            _outerCompleted = true;
            var exceptions = _pendingOrdinaryLifecycles
                .Where(lifecycle => lifecycle.Exception is not null)
                .ToArray();
            _pendingOrdinaryLifecycles.Clear();
            return exceptions.Length > 0 ?
                exceptions :
                [new(result.Result!, null, false)];
        }
    }

    public IReadOnlyList<ToolCallLifecycle> CompleteOuterException(
        Exception exception,
        bool cancellationRequested)
    {
        lock (_sync)
        {
            _outerCompleted = true;
            _pendingOrdinaryLifecycles.Clear();
            return [new(null, exception, cancellationRequested)];
        }
    }

    private IReadOnlyList<ToolCallLifecycle> RecordOrdinaryLifecycle(ToolCallLifecycle lifecycle)
    {
        lock (_sync)
        {
            if (_outerReturnedAlternate)
            {
                return [lifecycle];
            }

            if (!_outerCompleted)
            {
                _pendingOrdinaryLifecycles.Add(lifecycle);
            }

            return [];
        }
    }

    private IReadOnlyList<ToolCallLifecycle> DrainPendingLifecycles()
    {
        if (_pendingOrdinaryLifecycles.Count == 0)
        {
            return [];
        }

        var lifecycles = _pendingOrdinaryLifecycles.ToArray();
        _pendingOrdinaryLifecycles.Clear();
        return lifecycles;
    }
}

internal sealed record ToolCallLifecycle(
    CallToolResult? Result,
    Exception? Exception,
    bool CancellationRequested);
#pragma warning restore MCPEXP002
