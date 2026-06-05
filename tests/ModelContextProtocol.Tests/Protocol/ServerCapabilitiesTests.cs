using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ServerCapabilitiesTests
{
    [Fact]
    public static void ServerCapabilities_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ServerCapabilities
        {
            Logging = new LoggingCapability(),
            Prompts = new PromptsCapability { ListChanged = true },
            Resources = new ResourcesCapability { Subscribe = true, ListChanged = true },
            Tools = new ToolsCapability { ListChanged = false },
            Completions = new CompletionsCapability(),
            Tasks = new McpTasksCapability(),
            Extensions = new Dictionary<string, object>
            {
                ["io.modelcontextprotocol/apps"] = new object()
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Logging);
        Assert.NotNull(deserialized.Prompts);
        Assert.True(deserialized.Prompts.ListChanged);
        Assert.NotNull(deserialized.Resources);
        Assert.True(deserialized.Resources.Subscribe);
        Assert.True(deserialized.Resources.ListChanged);
        Assert.NotNull(deserialized.Tools);
        Assert.False(deserialized.Tools.ListChanged);
        Assert.NotNull(deserialized.Completions);
        Assert.NotNull(deserialized.Tasks);
        Assert.NotNull(deserialized.Extensions);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/apps"));
    }

    [Fact]
    public static void ServerCapabilities_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ServerCapabilities();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Experimental);
        Assert.Null(deserialized.Logging);
        Assert.Null(deserialized.Prompts);
        Assert.Null(deserialized.Resources);
        Assert.Null(deserialized.Tools);
        Assert.Null(deserialized.Completions);
        Assert.Null(deserialized.Tasks);
        Assert.Null(deserialized.Extensions);
    }

    [Fact]
    public static void ServerCapabilities_Extensions_DeserializesFromJson()
    {
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/apps": {},
                    "io.modelcontextprotocol/custom": {
                        "option": 42,
                        "enabled": true
                    }
                }
            }
            """;

        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Equal(2, deserialized.Extensions.Count);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/apps"));
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/custom"));
    }

    [Fact]
    public static void ServerCapabilities_Extensions_EmptyObjectDeserializesAsEmptyDictionary()
    {
        string json = """{"extensions": {}}""";

        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Empty(deserialized.Extensions);
    }
}
