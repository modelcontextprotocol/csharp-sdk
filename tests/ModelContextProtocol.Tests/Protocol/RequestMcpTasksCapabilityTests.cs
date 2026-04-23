using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class RequestMcpTasksCapabilityTests
{
    [Fact]
    public static void ServerRequestMcpTasksCapability_SerializationRoundTrip_ToolsOnly()
    {
        // Arrange
        var original = new ServerRequestMcpTasksCapability
        {
            Tools = new ToolsMcpTasksCapability
            {
                Call = new CallToolMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ServerRequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Tools);
        Assert.NotNull(deserialized.Tools.Call);
    }

    [Fact]
    public static void ServerRequestMcpTasksCapability_HasCorrectJsonPropertyNames()
    {
        var capability = new ServerRequestMcpTasksCapability
        {
            Tools = new ToolsMcpTasksCapability
            {
                Call = new CallToolMcpTasksCapability()
            }
        };

        string json = JsonSerializer.Serialize(capability, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"tools\":", json);
        Assert.Contains("\"call\":", json);
    }

    [Fact]
    public static void ClientRequestMcpTasksCapability_SerializationRoundTrip_SamplingOnly()
    {
        // Arrange
        var original = new ClientRequestMcpTasksCapability
        {
            Sampling = new SamplingMcpTasksCapability
            {
                CreateMessage = new CreateMessageMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ClientRequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Sampling);
        Assert.NotNull(deserialized.Sampling.CreateMessage);
        Assert.Null(deserialized.Elicitation);
    }

    [Fact]
    public static void ClientRequestMcpTasksCapability_SerializationRoundTrip_ElicitationOnly()
    {
        // Arrange
        var original = new ClientRequestMcpTasksCapability
        {
            Elicitation = new ElicitationMcpTasksCapability
            {
                Create = new CreateElicitationMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ClientRequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Sampling);
        Assert.NotNull(deserialized.Elicitation);
        Assert.NotNull(deserialized.Elicitation.Create);
    }

    [Fact]
    public static void ClientRequestMcpTasksCapability_HasCorrectJsonPropertyNames()
    {
        var capability = new ClientRequestMcpTasksCapability
        {
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

        Assert.Contains("\"sampling\":", json);
        Assert.Contains("\"elicitation\":", json);
        Assert.Contains("\"createMessage\":", json);
        Assert.Contains("\"create\":", json);
    }
}
