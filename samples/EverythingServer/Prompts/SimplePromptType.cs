using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Prompts;

[McpServerPromptType]
public class SimplePromptType
{
    [McpServerPrompt(Name = "simple_prompt", IconSource = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/62ecdc0d7ca5c6df32148c169556bc8d3782fca4/assets/Light%20bulb/Flat/light_bulb_flat.svg"), Description("A prompt without arguments")]
    public static string SimplePrompt() => "This is a simple prompt without arguments";
}
