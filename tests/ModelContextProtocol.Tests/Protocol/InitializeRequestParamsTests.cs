using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class InitializeRequestParamsTests
{
    [Fact]
    public static void InitializeRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new InitializeRequestParams
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new ClientCapabilities
            {
                Roots = new RootsCapability { ListChanged = true },
                Sampling = new SamplingCapability()
            },
            ClientInfo = new Implementation
            {
                Name = "test-client",
                Version = "1.0.0"
            },
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InitializeRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("2024-11-05", deserialized.ProtocolVersion);
        Assert.NotNull(deserialized.Capabilities);
        Assert.NotNull(deserialized.Capabilities.Roots);
        Assert.True(deserialized.Capabilities.Roots.ListChanged);
        Assert.NotNull(deserialized.Capabilities.Sampling);
        Assert.Equal("test-client", deserialized.ClientInfo.Name);
        Assert.Equal("1.0.0", deserialized.ClientInfo.Version);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }
}
