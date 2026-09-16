using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Tracks MRTR protocol mode via incoming and outgoing message filters.
/// Used by MRTR tests to verify the correct protocol mode (MRTR vs legacy) was used.
/// </summary>
internal sealed class ServerMessageTracker
{
    private static readonly HashSet<string> LegacyMrtrMethods =
    [
        RequestMethods.ElicitationCreate,
        RequestMethods.SamplingCreateMessage,
        RequestMethods.RootsList,
    ];

    private readonly ConcurrentBag<string> _legacyRequestMethods = [];
    private int _mrtrRetryCount;
    private int _incompleteResultCount;

    /// <summary>
    /// Adds incoming and outgoing message filters to track MRTR protocol usage.
    /// Call this in <c>services.Configure&lt;McpServerOptions&gt;</c> or <c>AddMcpServer</c> callbacks.
    /// </summary>
    public void AddFilters(McpMessageFilters messageFilters)
    {
        // Track outgoing legacy JSON-RPC requests and InputRequiredResult responses.
        messageFilters.OutgoingFilters.Add(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request && LegacyMrtrMethods.Contains(request.Method))
            {
                _legacyRequestMethods.Add(request.Method);
            }
            else if (context.JsonRpcMessage is JsonRpcResponse response &&
                     response.Result is JsonObject resultObj &&
                     resultObj.TryGetPropertyValue("resultType", out var resultTypeNode) &&
                     resultTypeNode?.GetValue<string>() == "input_required")
            {
                Interlocked.Increment(ref _incompleteResultCount);
            }

            await next(context, cancellationToken);
        });

        // Track incoming MRTR retries (requests with inputResponses or requestState in params).
        messageFilters.IncomingFilters.Add(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request &&
                request.Params is JsonObject paramsObj &&
                (paramsObj.ContainsKey("inputResponses") || paramsObj.ContainsKey("requestState")))
            {
                Interlocked.Increment(ref _mrtrRetryCount);
            }

            await next(context, cancellationToken);
        });
    }

    /// <summary>
    /// Asserts that MRTR was used: at least one InputRequiredResult response was sent
    /// and no legacy JSON-RPC requests (elicitation/create, sampling/createMessage, roots/list) were sent.
    /// </summary>
    public void AssertMrtrUsed()
    {
        Assert.True(_incompleteResultCount > 0,
            "Expected at least one InputRequiredResult response (MRTR mode), but none were detected.");
        Assert.Empty(_legacyRequestMethods);
    }

    /// <summary>
    /// Asserts that MRTR was used at least once (at least one InputRequiredResult response was sent),
    /// independent of whether the session also issued any legacy server-to-client requests.
    /// </summary>
    public void AssertMrtrUsedAtLeastOnce()
    {
        Assert.True(_incompleteResultCount > 0,
            "Expected at least one InputRequiredResult response (MRTR mode), but none were detected.");
    }

    /// <summary>
    /// Asserts that legacy mode was used: at least one legacy JSON-RPC request was sent
    /// and no MRTR retries or InputRequiredResult responses were detected.
    /// </summary>
    public void AssertMrtrNotUsed()
    {
        Assert.NotEmpty(_legacyRequestMethods);
        Assert.Equal(0, _mrtrRetryCount);
        Assert.Equal(0, _incompleteResultCount);
    }
}
