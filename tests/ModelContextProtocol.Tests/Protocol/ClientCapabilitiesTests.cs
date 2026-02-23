using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ClientCapabilitiesTests
{
    [Fact]
    public static void ClientCapabilities_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ClientCapabilities
        {
            Roots = new RootsCapability { ListChanged = true },
            Sampling = new SamplingCapability
            {
                Context = new SamplingContextCapability(),
                Tools = new SamplingToolsCapability()
            },
            Elicitation = new ElicitationCapability
            {
                Form = new FormElicitationCapability(),
                Url = new UrlElicitationCapability()
            },
            Tasks = new McpTasksCapability(),
            Extensions = new Dictionary<string, object>
            {
                ["io.modelcontextprotocol/test"] = new object()
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Roots);
        Assert.True(deserialized.Roots.ListChanged);
        Assert.NotNull(deserialized.Sampling);
        Assert.NotNull(deserialized.Sampling.Context);
        Assert.NotNull(deserialized.Sampling.Tools);
        Assert.NotNull(deserialized.Elicitation);
        Assert.NotNull(deserialized.Elicitation.Form);
        Assert.NotNull(deserialized.Elicitation.Url);
        Assert.NotNull(deserialized.Tasks);
        Assert.NotNull(deserialized.Extensions);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/test"));
    }

    [Fact]
    public static void ClientCapabilities_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ClientCapabilities();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Experimental);
        Assert.Null(deserialized.Roots);
        Assert.Null(deserialized.Sampling);
        Assert.Null(deserialized.Elicitation);
        Assert.Null(deserialized.Tasks);
        Assert.Null(deserialized.Extensions);
    }

    [Fact]
    public static void ClientCapabilities_Extensions_DeserializesFromJson()
    {
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/oauth-client-credentials": {},
                    "io.modelcontextprotocol/test-extension": {
                        "setting1": "value1",
                        "setting2": 42
                    }
                }
            }
            """;

        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Equal(2, deserialized.Extensions.Count);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/oauth-client-credentials"));
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/test-extension"));
    }

    [Fact]
    public static void ClientCapabilities_Extensions_EmptyObjectDeserializesAsEmptyDictionary()
    {
        string json = """{"extensions": {}}""";

        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Empty(deserialized.Extensions);
    }
}
