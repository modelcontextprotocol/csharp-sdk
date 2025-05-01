using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Handles processing the request/response body pairs for the Streamable HTTP transport.
/// This is typically used via <see cref="JsonRpcMessage.RelatedTransport"/>.
/// </summary>
internal sealed class StreamableHttpPostTransport(ChannelWriter<JsonRpcMessage>? incomingChannel, IDuplexPipe httpBodies) : ITransport
{
    private readonly SseWriter _sseWriter = new();
    private readonly HashSet<RequestId> _pendingRequests = [];

    // REVIEW: Should we introduce a send-only interface for RelatedTransport?
    public ChannelReader<JsonRpcMessage> MessageReader => throw new NotSupportedException("JsonRpcMessage.RelatedTransport should only be used for sending messages.");

    /// <returns>
    /// True, if data was written to the respond body.
    /// False, if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
    public async ValueTask<bool> RunAsync(CancellationToken cancellationToken)
    {
        // The incomingChannel is null to handle the potential client GET request to handle unsolicited JsonRpcMessages.
        if (incomingChannel is not null)
        {
            var message = await JsonSerializer.DeserializeAsync(httpBodies.Input.AsStream(),
                McpJsonUtilities.JsonContext.Default.JsonRpcMessage, cancellationToken).ConfigureAwait(false);
            await OnMessageReceivedAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (_pendingRequests.Count == 0)
        {
            return false;
        }

        _sseWriter.MessageFilter = StopOnFinalResponseFilter;
        await _sseWriter.WriteAllAsync(httpBodies.Output.AsStream(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _sseWriter.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _sseWriter.DisposeAsync().ConfigureAwait(false);
    }

    private async IAsyncEnumerable<SseItem<JsonRpcMessage?>> StopOnFinalResponseFilter(IAsyncEnumerable<SseItem<JsonRpcMessage?>> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in messages.WithCancellation(cancellationToken))
        {
            yield return message;

            if (message.Data is JsonRpcMessageWithId response)
            {
                if (_pendingRequests.Remove(response.Id) && _pendingRequests.Count == 0)
                {
                    // Complete the SSE response stream now that all pending requests have been processed.
                    break;
                }
            }
        }
    }

    private async ValueTask OnMessageReceivedAsync(JsonRpcMessage? message, CancellationToken cancellationToken)
    {
        if (message is null)
        {
            throw new InvalidOperationException("Received invalid null message.");
        }

        if (message is JsonRpcRequest request)
        {
            _pendingRequests.Add(request.Id);
        }

        message.RelatedTransport = this;

        // Really an assertion. This doesn't get called when incomingChannel is null for GET requests.
        Throw.IfNull(incomingChannel);
        await incomingChannel.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
