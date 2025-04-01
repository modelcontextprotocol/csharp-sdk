using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Prompts;

[McpServerPromptType]
public class SimplePromptType
{
    [McpServerPrompt(Name = "simple_prompt"), Description("A prompt without arguments")]
    public static string SimplePrompt() => "This is a simple prompt without arguments";
}
