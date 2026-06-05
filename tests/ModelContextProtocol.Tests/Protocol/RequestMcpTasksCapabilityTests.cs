using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class RequestMcpTasksCapabilityTests
{
    [Fact]
    public static void RequestMcpTasksCapability_SerializationRoundTrip_ToolsOnly()
    {
        // Arrange
        var original = new RequestMcpTasksCapability
        {
            Tools = new ToolsMcpTasksCapability
            {
                Call = new CallToolMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Tools);
        Assert.NotNull(deserialized.Tools.Call);
        Assert.Null(deserialized.Sampling);
        Assert.Null(deserialized.Elicitation);
    }

    [Fact]
    public static void RequestMcpTasksCapability_SerializationRoundTrip_SamplingOnly()
    {
        // Arrange
        var original = new RequestMcpTasksCapability
        {
            Sampling = new SamplingMcpTasksCapability
            {
                CreateMessage = new CreateMessageMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Tools);
        Assert.NotNull(deserialized.Sampling);
        Assert.NotNull(deserialized.Sampling.CreateMessage);
        Assert.Null(deserialized.Elicitation);
    }

    [Fact]
    public static void RequestMcpTasksCapability_SerializationRoundTrip_ElicitationOnly()
    {
        // Arrange
        var original = new RequestMcpTasksCapability
        {
            Elicitation = new ElicitationMcpTasksCapability
            {
                Create = new CreateElicitationMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Tools);
        Assert.Null(deserialized.Sampling);
        Assert.NotNull(deserialized.Elicitation);
        Assert.NotNull(deserialized.Elicitation.Create);
    }

    [Fact]
    public static void RequestMcpTasksCapability_HasCorrectJsonPropertyNames()
    {
        var capability = new RequestMcpTasksCapability
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
        };

        string json = JsonSerializer.Serialize(capability, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"tools\":", json);
        Assert.Contains("\"sampling\":", json);
        Assert.Contains("\"elicitation\":", json);
        Assert.Contains("\"call\":", json);
        Assert.Contains("\"createMessage\":", json);
        Assert.Contains("\"create\":", json);
    }
}
