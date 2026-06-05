using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Validates that the internal property pattern used for experimental properties
/// produces the expected serialization behavior for SDK consumers using source generators.
/// </summary>
/// <remarks>
/// <para>
/// Experimental properties (e.g. <see cref="Tool.Execution"/>, <see cref="ServerCapabilities.Tasks"/>)
/// use an internal <c>*Core</c> property for serialization. A consumer's source-generated
/// <see cref="JsonSerializerContext"/> cannot see internal members, so experimental data is
/// silently dropped unless the consumer chains the SDK's resolver into their options.
/// </para>
/// <para>
/// These tests depend on <see cref="Tool.Execution"/> and <see cref="ServerCapabilities.Tasks"/>
/// being experimental. When those APIs stabilize, update these tests to reference whatever
/// experimental properties exist at that time, or remove them entirely if no experimental
/// APIs remain.
/// </para>
/// </remarks>
public class ExperimentalPropertySerializationTests
{
    [Fact]
    public void ExperimentalProperties_Dropped_WithConsumerContextOnly()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolverChain = { ConsumerJsonContext.Default }
        };

        var tool = new Tool
        {
            Name = "test-tool",
            Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
        };

        string json = JsonSerializer.Serialize(tool, options);
        Assert.DoesNotContain("\"execution\"", json);
        Assert.Contains("\"name\"", json);
    }

    [Fact]
    public void ExperimentalProperties_IgnoredOnDeserialize_WithConsumerContextOnly()
    {
        string json = JsonSerializer.Serialize(
            new Tool
            {
                Name = "test-tool",
                Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
            },
            McpJsonUtilities.DefaultOptions);
        Assert.Contains("\"execution\"", json);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolverChain = { ConsumerJsonContext.Default }
        };
        var deserialized = JsonSerializer.Deserialize<Tool>(json, options)!;
        Assert.Equal("test-tool", deserialized.Name);
        Assert.Null(deserialized.Execution);
    }

    [Fact]
    public void ExperimentalProperties_RoundTrip_WhenSdkResolverIsChained()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolverChain =
            {
                McpJsonUtilities.DefaultOptions.TypeInfoResolver!,
                ConsumerJsonContext.Default,
            }
        };

        var tool = new Tool
        {
            Name = "test-tool",
            Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
        };

        string json = JsonSerializer.Serialize(tool, options);
        Assert.Contains("\"execution\"", json);
        Assert.Contains("\"name\"", json);

        var deserialized = JsonSerializer.Deserialize<Tool>(json, options)!;
        Assert.Equal("test-tool", deserialized.Name);
        Assert.NotNull(deserialized.Execution);
        Assert.Equal(ToolTaskSupport.Optional, deserialized.Execution.TaskSupport);
    }

    [Fact]
    public void ExperimentalProperties_RoundTrip_WithDefaultOptions()
    {
        var capabilities = new ServerCapabilities
        {
            Tasks = new McpTasksCapability()
        };

        string json = JsonSerializer.Serialize(capabilities, McpJsonUtilities.DefaultOptions);
        Assert.Contains("\"tasks\"", json);

        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.NotNull(deserialized.Tasks);
    }
}

[JsonSerializable(typeof(Tool))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ClientCapabilities))]
[JsonSerializable(typeof(CallToolResult))]
[JsonSerializable(typeof(CallToolRequestParams))]
[JsonSerializable(typeof(CreateMessageRequestParams))]
[JsonSerializable(typeof(ElicitRequestParams))]
internal partial class ConsumerJsonContext : JsonSerializerContext;
