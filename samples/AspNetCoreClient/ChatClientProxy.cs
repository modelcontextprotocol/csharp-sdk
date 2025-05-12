using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace AspNetCoreClient
{
    public class ChatClientProxy : IChatClient
    {
        private readonly  IChatClient _chatClient;
        public ChatClientProxy(ChatClient chatClient)
        {
            _chatClient = chatClient.AsIChatClient();
        }   
        public void Dispose()
        {
            _chatClient.Dispose();
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return await _chatClient.GetResponseAsync(messages, options, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
           return _chatClient.GetService(serviceType, serviceKey);  
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken); 
        }
    }
}

 
