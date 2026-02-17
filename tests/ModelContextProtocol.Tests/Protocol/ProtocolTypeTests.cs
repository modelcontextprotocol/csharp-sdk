using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ProtocolTypeTests
{
    [Theory]
    [InlineData(Role.User, "\"user\"")]
    [InlineData(Role.Assistant, "\"assistant\"")]
    public static void SerializeRole_ShouldBeCamelCased(Role role, string expectedValue)
    {
        var actualValue = JsonSerializer.Serialize(role, McpJsonUtilities.DefaultOptions);

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
        var actualValue = JsonSerializer.Serialize(level, McpJsonUtilities.DefaultOptions);

        Assert.Equal(expectedValue, actualValue);
    }

    [Theory]
    [InlineData(ContextInclusion.None, "\"none\"")]
    [InlineData(ContextInclusion.ThisServer, "\"thisServer\"")]
    [InlineData(ContextInclusion.AllServers, "\"allServers\"")]
    public static void ContextInclusion_ShouldBeCamelCased(ContextInclusion level, string expectedValue)
    {
        var actualValue = JsonSerializer.Serialize(level, McpJsonUtilities.DefaultOptions);

        Assert.Equal(expectedValue, actualValue);
    }
}
