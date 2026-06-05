using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class GetTaskRequestParamsTests
{
    [Fact]
    public static void GetTaskRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new GetTaskRequestParams
        {
            TaskId = "get-task-123"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
    }
}
