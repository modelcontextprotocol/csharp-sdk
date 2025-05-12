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
}

 
