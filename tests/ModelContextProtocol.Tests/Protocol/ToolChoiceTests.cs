using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class ToolChoiceTests
{
    [Fact]
    public void DefaultModeIsNull()
    {
        Assert.Null(new ToolChoice().Mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    [InlineData("required")]
    [InlineData("auto")]
    [InlineData("something_custom")]
    public void SerializesWithMode(string? mode)
    {
        ToolChoice toolChoice = new() { Mode = mode };

        var json = JsonSerializer.Serialize(toolChoice, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolChoice>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(mode, deserialized.Mode);
    }
}
