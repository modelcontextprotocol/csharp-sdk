﻿using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Server;

/// <summary>
/// Handles processing the request/response body pairs for the Streamable HTTP transport.
/// This is typically used via <see cref="JsonRpcMessage.RelatedTransport"/>.
/// </summary>
internal sealed class StreamableHttpPostTransport(StreamableHttpServerTransport parentTransport, IDuplexPipe httpBodies) : ITransport
{
    private readonly SseWriter _sseWriter = new();
    private RequestId _pendingRequest;

    public ChannelReader<JsonRpcMessage> MessageReader => throw new NotSupportedException("JsonRpcMessage.RelatedTransport should only be used for sending messages.");

    /// <returns>
    /// True, if data was written to the respond body.
    /// False, if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
    public async ValueTask<bool> RunAsync(CancellationToken cancellationToken)
    {
        var message = await JsonSerializer.DeserializeAsync(httpBodies.Input.AsStream(),
            McpJsonUtilities.JsonContext.Default.JsonRpcMessage, cancellationToken).ConfigureAwait(false);
        await OnMessageReceivedAsync(message, cancellationToken).ConfigureAwait(false);

        if (_pendingRequest.Id is null)
        {
            return false;
        }

        _sseWriter.MessageFilter = StopOnFinalResponseFilter;
        await _sseWriter.WriteAllAsync(httpBodies.Output.AsStream(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (parentTransport.Stateless && message is JsonRpcRequest)
        {
            throw new InvalidOperationException("Server to client requests are not supported in stateless mode.");
        }

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

            if (message.Data is JsonRpcMessageWithId response && response.Id == _pendingRequest)
            {
                // Complete the SSE response stream now that all pending requests have been processed.
                break;
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
            _pendingRequest = request.Id;

            // Store client capabilities so they can be serialized by "stateless" callers for use in later requests.
            if (parentTransport.Stateless && request.Method == RequestMethods.Initialize)
            {
                parentTransport.InitializeRequest = JsonSerializer.Deserialize(request.Params, McpJsonUtilities.JsonContext.Default.InitializeRequestParams);
            }
        }

        message.RelatedTransport = this;

        await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
