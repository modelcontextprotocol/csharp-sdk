using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListTasksRequestParamsTests
{
    [Fact]
    public static void ListTasksRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new ListTasksRequestParams
        {
            Cursor = "cursor-abc123"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListTasksRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Cursor, deserialized.Cursor);
    }
}
