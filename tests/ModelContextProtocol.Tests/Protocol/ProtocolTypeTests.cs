using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ProtocolTypeTests
{
    [Fact]
    public static void ToolInputSchema_HasValidDefaultSchema()
    {
        var tool = new Tool();
        JsonElement jsonElement = tool.InputSchema;

        Assert.Equal(JsonValueKind.Object, jsonElement.ValueKind);
        Assert.Single(jsonElement.EnumerateObject());
        Assert.True(jsonElement.TryGetProperty("type", out JsonElement typeElement));
        Assert.Equal(JsonValueKind.String, typeElement.ValueKind);
        Assert.Equal("object", typeElement.GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("3.5e3")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("""{"type":"number"}""")]
    [InlineData("""{"type":"array"}""")]
    [InlineData("""{"type":["object"]}""")]
    public static void ToolInputSchema_RejectsInvalidSchemaDocuments(string invalidSchema)
    {
        using var document = JsonDocument.Parse(invalidSchema);
        var tool = new Tool();

        Assert.Throws<ArgumentException>(() => tool.InputSchema = document.RootElement);
    }

    [Theory]
    [InlineData("""{"type":"object"}""")]
    [InlineData("""{"type":"object", "properties": {}, "required" : [] }""")]
    [InlineData("""{"type":"object", "title": "MyAwesomeTool", "description": "It's awesome!", "properties": {}, "required" : ["NotAParam"] }""")]
    public static void ToolInputSchema_AcceptsValidSchemaDocuments(string validSchema)
    {
        using var document = JsonDocument.Parse(validSchema);
        Tool tool = new()
        {
            InputSchema = document.RootElement
        };

        Assert.True(JsonElement.DeepEquals(document.RootElement, tool.InputSchema));
    }

    [Theory]
    [InlineData(Role.User, "\"user\"")]
    [InlineData(Role.Assistant, "\"assistant\"")]
    public static void SerializeRole_ShouldBeCamelCased(Role role, string expectedValue)
    {
        var actualValue = JsonSerializer.Serialize(role);

        Assert.Equal(expectedValue, actualValue);
    }

    [Theory]
    [InlineData(LoggingLevel.Debug, "\"debug\"")]
    [InlineData(LoggingLevel.Info, "\"info\"")]
    [InlineData(LoggingLevel.Notice, "\"notice\"")]
    [InlineData(LoggingLevel.Warning, "\"warning\"")]
    [InlineData(LoggingLevel.Error, "\"error\"")]
    [InlineData(LoggingLevel.Critical, "\"critical\"")]
    [InlineData(LoggingLevel.Alert, "\"alert\"")]
    [InlineData(LoggingLevel.Emergency, "\"emergency\"")]
    public static void SerializeLoggingLevel_ShouldBeCamelCased(LoggingLevel level, string expectedValue)
    {
        var actualValue = JsonSerializer.Serialize(level);

        Assert.Equal(expectedValue, actualValue);
    }

    [Theory]
    [InlineData(ContextInclusion.None, "\"none\"")]
    [InlineData(ContextInclusion.ThisServer, "\"thisServer\"")]
    [InlineData(ContextInclusion.AllServers, "\"allServers\"")]
    public static void ContextInclusion_ShouldBeCamelCased(ContextInclusion level, string expectedValue)
    {
        var actualValue = JsonSerializer.Serialize(level);

        Assert.Equal(expectedValue, actualValue);
    }
}
