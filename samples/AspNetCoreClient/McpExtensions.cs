using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI.Chat;

namespace AspNetCoreClient
{
    public static class McpExtensions
    {
        public static IList<ChatTool> ToOpenAITools(this IList<McpClientTool> tools)
        {
            var ret = new List<ChatTool>();
            foreach (var tool in tools)
            {
                ret.Add(tool.ToOpenAITool());
            }
            return ret;
        }

        public static ChatTool ToOpenAITool(this McpClientTool tool)
        {
                return ChatTool.CreateFunctionTool(tool.Name, tool.Description, new BinaryData(tool.JsonSchema));
        }
     
            

    }

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

 
