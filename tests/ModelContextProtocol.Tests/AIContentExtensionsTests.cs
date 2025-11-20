using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

public class AIContentExtensionsTests
{
    [Fact]
    public void CallToolResult_ToChatMessage_ProducesExpectedAIContent()
    {
        CallToolResult toolResult = new() { Content = [new TextContentBlock { Text = "This is a test message." }] };

        Assert.Throws<ArgumentNullException>(() => AIContentExtensions.ToChatMessage(null!, "call123"));
        Assert.Throws<ArgumentNullException>(() => AIContentExtensions.ToChatMessage(toolResult, null!));

        ChatMessage message = AIContentExtensions.ToChatMessage(toolResult, "call123");
        
        Assert.NotNull(message);
        Assert.Equal(ChatRole.Tool, message.Role);
        
        FunctionResultContent frc = Assert.IsType<FunctionResultContent>(Assert.Single(message.Contents));
        Assert.Same(toolResult, frc.RawRepresentation);
        Assert.Equal("call123", frc.CallId);
        JsonElement result = Assert.IsType<JsonElement>(frc.Result);
        Assert.Contains("This is a test message.", result.ToString());
    }
}