using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using Xunit;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Records outgoing server→client JSON-RPC request methods via a message filter.
/// Used by MRTR tests to verify the correct protocol mode was used.
/// </summary>
internal sealed class ServerMessageTracker
{
    private readonly ConcurrentBag<string> _outgoingMethods = [];

    /// <summary>
    /// Adds an outgoing message filter that records outgoing JSON-RPC request methods.
    /// Call this in <c>services.Configure&lt;McpServerOptions&gt;</c> or <c>AddMcpServer</c> callbacks.
    /// </summary>
    public void AddOutgoingFilter(McpMessageFilters messageFilters)
    {
        messageFilters.OutgoingFilters.Add(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request)
            {
                _outgoingMethods.Add(request.Method);
            }

            await next(context, cancellationToken);
        });
    }

    /// <summary>
    /// Asserts that no legacy elicitation or sampling JSON-RPC requests were sent.
    /// Use in experimental-mode tests to verify MRTR was used instead of legacy requests.
    /// </summary>
    public void AssertNoLegacyMrtrRequests()
    {
        Assert.DoesNotContain(RequestMethods.ElicitationCreate, _outgoingMethods);
        Assert.DoesNotContain(RequestMethods.SamplingCreateMessage, _outgoingMethods);
    }
}
