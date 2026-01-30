using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace ConformanceServer.Prompts;

public class ConformancePrompts
{
    // Sample base64 encoded 1x1 red PNG pixel for testing
    private const string TestImageBase64 =
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

    [McpServerPrompt(Name = "test_simple_prompt"), Description("Simple prompt without arguments")]
    public static string SimplePrompt() => "This is a simple prompt without arguments";

    [McpServerPrompt(Name = "test_prompt_with_arguments"), Description("Parameterized prompt")]
    public static string ParameterizedPrompt(
        [Description("First test argument")] string arg1,
        [Description("Second test argument")] string arg2)
    {
        return $"Prompt with arguments: arg1={arg1}, arg2={arg2}";
    }

    [McpServerPrompt(Name = "test_prompt_with_embedded_resource"), Description("Prompt with embedded resource")]
    public static IEnumerable<PromptMessage> PromptWithEmbeddedResource(
        [Description("URI of the resource to embed")] string resourceUri)
    {
        return [
            new PromptMessage
            {
                Role = Role.User,
                Content = new EmbeddedResourceBlock
                {
                    Resource = new TextResourceContents
                    {
                        Uri = resourceUri,
                        Text = "Embedded resource content for testing.",
                        MimeType = "text/plain"
                    }
                }
            },
            new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "Please process the embedded resource above." } },
        ];
    }

    [McpServerPrompt(Name = "test_prompt_with_image"), Description("Prompt with image")]
    public static IEnumerable<PromptMessage> PromptWithImage()
    {
        return [
            new PromptMessage
            {
                Role = Role.User,
                Content = new ImageContentBlock
                {
                    MimeType = "image/png",
                    Data = System.Text.Encoding.UTF8.GetBytes(TestImageBase64)
                }
            },
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = "Please analyze the image above." }
            },
        ];
    }
}
