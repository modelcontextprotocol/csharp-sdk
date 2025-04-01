using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.Tests;

    /// <summary>
    /// A test transport implementation for client tests.
    /// </summary>
    public class TestClientTransport : IClientTransport
    {
        private readonly Dictionary<RequestId, Func<CancellationToken, Task<JsonRpcResponse>>> _responseHandlers = new();
        private readonly Channel<IJsonRpcMessage> _incomingMessages = Channel.CreateUnbounded<IJsonRpcMessage>();
        
        public List<IJsonRpcMessage> SentMessages { get; } = new();
        
        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<ITransport>(new TestConnectedTransport(this));
        }
        
        public void SetupRequestResponse(JsonRpcRequest request, Func<CancellationToken, Task<JsonRpcResponse>> responseHandler)
        {
            _responseHandlers[request.Id] = responseHandler;
        }
        
        public async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            if (_responseHandlers.TryGetValue(request.Id, out var handler))
            {
                return await handler(cancellationToken);
            }
            
            return new JsonRpcResponse { Id = request.Id, Result = new { } };
        }
        
        private class TestConnectedTransport : ITransport
        {
            private readonly TestClientTransport _parent;
            
            public TestConnectedTransport(TestClientTransport parent)
            {
                _parent = parent;
            }
            
            public bool IsConnected => true;
            
            public ChannelReader<IJsonRpcMessage> MessageReader => _parent._incomingMessages.Reader;
            
            public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
            {
                _parent.SentMessages.Add(message);
                
                if (message is JsonRpcRequest request)
                {
                    var response = await _parent.HandleRequestAsync(request, cancellationToken);
                    if (response != null)
                    {
                        await _parent._incomingMessages.Writer.WriteAsync(response, cancellationToken);
                    }
                }
            }
            
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }