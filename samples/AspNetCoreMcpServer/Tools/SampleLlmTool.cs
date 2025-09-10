using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServer.Tools;

/// <summary>
/// This tool uses dependency injection and async method
/// </summary>
[McpServerToolType]
public sealed class SampleLlmTool
{
    [McpServerTool(Name = "sampleLLM"), Description("Samples from an LLM using MCP's sampling feature")]
    public static async Task<string> SampleLLM(
        McpServer thisServer,
        [Description("The prompt to send to the LLM")] string prompt,
        [Description("Maximum number of tokens to generate")] int maxTokens,
        CancellationToken cancellationToken)
    {
        ChatOptions options = new()
        {
            Instructions = "You are a helpful test server.",
            MaxOutputTokens = maxTokens,
            Temperature = 0.7f,
        };

        var samplingResponse = await thisServer.AsSamplingChatClient().GetResponseAsync(prompt, options, cancellationToken);

        return $"LLM sampling result: {samplingResponse}";
    }
}
