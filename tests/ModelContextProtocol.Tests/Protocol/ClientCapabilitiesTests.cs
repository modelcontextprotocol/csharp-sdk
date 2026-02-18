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
            Tasks = new McpTasksCapability()
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
    }
}
