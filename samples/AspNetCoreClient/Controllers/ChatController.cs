using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Types;
using OpenAI.Chat;

namespace AspNetCoreClient.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class ChatController : ControllerBase
    {

        private static readonly Dictionary<Guid,List<ChatMessage>> _Conversations = new();
        private readonly ILogger<ChatController> _logger;
        private readonly ChatClient _chatClient;
        private readonly IMcpClient _mcpClient;
        private readonly ITemplatesProvider _templatesProvider;

        public ChatController(ILogger<ChatController> logger, ChatClient chatClient, IMcpClient mcpClient, ITemplatesProvider templatesProvider)
        {
            _logger = logger;
            _chatClient = chatClient;
            _mcpClient = mcpClient;
            _templatesProvider = templatesProvider;
        }
       

       

        [HttpPost(template:"ask", Name = "Ask")]
        public async Task<ResponseToUser> Ask(Question question)
        {
            var tools = await _mcpClient.ListToolsAsync();
            List<ChatMessage>? messages = await GetOrCreateConversation(question.ConversationId);
            messages.Add(new UserChatMessage(question.Text));
            var co = new ChatCompletionOptions();
            foreach (var tool in tools)
            {
                co.Tools.Add(tool.ToOpenAITool());
            }
            bool requiresAction;

            do
            {
                requiresAction = false;
                ChatCompletion completion = _chatClient.CompleteChat(messages, co);

                switch (completion.FinishReason)
                {
                    case ChatFinishReason.Stop:
                        {
                            // Add the assistant message to the conversation history.
                            messages.Add(new AssistantChatMessage(completion));
                            break;
                        }

                    case ChatFinishReason.ToolCalls:
                        {
                            // First, add the assistant message with tool calls to the conversation history.
                            messages.Add(new AssistantChatMessage(completion));

                            // Then, add a new tool message for each tool call that is resolved.
                            foreach (ChatToolCall toolCall in completion.ToolCalls)
                            {
                                if (tools.Select(t => t.Name).Contains(toolCall.FunctionName, StringComparer.OrdinalIgnoreCase))
                                {
                                    var toolResult = await _mcpClient.CallToolAsync(toolCall.FunctionName, JsonSerializer.Deserialize <Dictionary<string, object?>> (toolCall.FunctionArguments.ToString()));
                                    messages.Add(new ToolChatMessage(toolCall.Id, toolResult.Content[0].Text));
                                }
                                else
                                {
                                    throw new Exception($"Tool {toolCall.FunctionName} not found");
                                }
                            }

                            requiresAction = true;
                            break;
                        }

                    case ChatFinishReason.Length:
                        throw new NotImplementedException("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                    case ChatFinishReason.ContentFilter:
                        throw new NotImplementedException("Omitted content due to a content filter flag.");

                    case ChatFinishReason.FunctionCall:
                        throw new NotImplementedException("Deprecated in favor of tool calls.");

                    default:
                        throw new NotImplementedException(completion.FinishReason.ToString());
                }
            } while (requiresAction);

            return new ResponseToUser { Text = messages.Last().Content[0].Text, ConversationId = question.ConversationId };
        }

        private async Task<List<ChatMessage>> GetOrCreateConversation(Guid conversationId)
        {
            _Conversations.TryGetValue(conversationId, out var messages);
            if (messages == null)
            {
                messages = new();
                messages.Add(new SystemChatMessage(await _templatesProvider.GetSystemMessage("weather")));

                _Conversations.Add(conversationId, messages);
            }

            return messages;
        }
    }
}

 
