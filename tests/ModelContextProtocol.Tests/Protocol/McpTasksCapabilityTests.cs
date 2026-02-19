using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class McpTasksCapabilityTests
{
    [Fact]
    public static void McpTasksCapability_SerializationRoundTrip_WithAllProperties()
    {
        // Arrange
        var original = new McpTasksCapability
        {
            List = new ListMcpTasksCapability(),
            Cancel = new CancelMcpTasksCapability(),
            Requests = new RequestMcpTasksCapability
            {
                Tools = new ToolsMcpTasksCapability
                {
                    Call = new CallToolMcpTasksCapability()
                },
                Sampling = new SamplingMcpTasksCapability
                {
                    CreateMessage = new CreateMessageMcpTasksCapability()
                },
                Elicitation = new ElicitationMcpTasksCapability
                {
                    Create = new CreateElicitationMcpTasksCapability()
                }
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.List);
        Assert.NotNull(deserialized.Cancel);
        Assert.NotNull(deserialized.Requests);
        Assert.NotNull(deserialized.Requests.Tools);
        Assert.NotNull(deserialized.Requests.Tools.Call);
        Assert.NotNull(deserialized.Requests.Sampling);
        Assert.NotNull(deserialized.Requests.Sampling.CreateMessage);
        Assert.NotNull(deserialized.Requests.Elicitation);
        Assert.NotNull(deserialized.Requests.Elicitation.Create);
    }

    [Fact]
    public static void McpTasksCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        // Arrange
        var original = new McpTasksCapability();

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.List);
        Assert.Null(deserialized.Cancel);
        Assert.Null(deserialized.Requests);
    }

    [Fact]
    public static void McpTasksCapability_HasCorrectJsonPropertyNames()
    {
        var capability = new McpTasksCapability
        {
            List = new ListMcpTasksCapability(),
            Cancel = new CancelMcpTasksCapability(),
            Requests = new RequestMcpTasksCapability
            {
                Tools = new ToolsMcpTasksCapability
                {
                    Call = new CallToolMcpTasksCapability()
                }
            }
        };

        string json = JsonSerializer.Serialize(capability, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"list\":", json);
        Assert.Contains("\"cancel\":", json);
        Assert.Contains("\"requests\":", json);
        Assert.Contains("\"tools\":", json);
        Assert.Contains("\"call\":", json);
    }
}
