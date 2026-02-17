using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class CancelMcpTaskRequestParamsTests
{
    [Fact]
    public static void CancelMcpTaskRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new CancelMcpTaskRequestParams
        {
            TaskId = "cancel-task-456"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CancelMcpTaskRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
    }
}
