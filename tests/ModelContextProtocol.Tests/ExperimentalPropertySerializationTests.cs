using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Validates that the internal property pattern used for experimental properties
/// produces the expected serialization behavior for SDK consumers using source generators.
/// </summary>
/// <remarks>
/// <para>
/// Experimental properties (e.g. <see cref="ServerCapabilities.Extensions"/>, <see cref="ClientCapabilities.Extensions"/>)
/// use an internal <c>*Core</c> property for serialization. A consumer's source-generated
/// <see cref="JsonSerializerContext"/> cannot see internal members, so experimental data is
/// silently dropped unless the consumer chains the SDK's resolver into their options.
/// </para>
/// <para>
/// These tests depend on <see cref="ServerCapabilities.Extensions"/> and <see cref="ClientCapabilities.Extensions"/>
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

        var capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Extensions = new Dictionary<string, object> { ["io.test"] = new JsonObject { ["enabled"] = true } }
        };

        string json = JsonSerializer.Serialize(capabilities, options);
        Assert.DoesNotContain("\"extensions\"", json);
        Assert.Contains("\"tools\"", json);
    }

    [Fact]
    public void ExperimentalProperties_IgnoredOnDeserialize_WithConsumerContextOnly()
    {
        string json = JsonSerializer.Serialize(
            new ServerCapabilities
            {
                Tools = new ToolsCapability(),
                Extensions = new Dictionary<string, object> { ["io.test"] = new JsonObject { ["enabled"] = true } }
            },
            McpJsonUtilities.DefaultOptions);
        Assert.Contains("\"extensions\"", json);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolverChain = { ConsumerJsonContext.Default }
        };
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, options)!;
        Assert.NotNull(deserialized.Tools);
        Assert.Null(deserialized.Extensions);
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

        var capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Extensions = new Dictionary<string, object> { ["io.test"] = new JsonObject { ["enabled"] = true } }
        };

        string json = JsonSerializer.Serialize(capabilities, options);
        Assert.Contains("\"extensions\"", json);
        Assert.Contains("\"tools\"", json);

        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, options)!;
        Assert.NotNull(deserialized.Tools);
        Assert.NotNull(deserialized.Extensions);
        Assert.True(deserialized.Extensions.ContainsKey("io.test"));
    }

    [Fact]
    public void ExperimentalProperties_RoundTrip_WithDefaultOptions()
    {
        var capabilities = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object> { ["io.test"] = new JsonObject { ["enabled"] = true } }
        };

        string json = JsonSerializer.Serialize(capabilities, McpJsonUtilities.DefaultOptions);
        Assert.Contains("\"extensions\"", json);

        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.NotNull(deserialized.Extensions);
        Assert.True(deserialized.Extensions.ContainsKey("io.test"));
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
