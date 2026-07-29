#pragma warning disable MCPEXP003

using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpAppElicitationTests
{
    [Fact]
    public void AddClientCapabilities_AddsCoreAndExistingUiExtensionSettings()
    {
        var capabilities = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                [McpApps.ExtensionId] = new JsonObject
                {
                    ["custom"] = true,
                },
            },
        };

        McpAppElicitation.AddClientCapabilities(capabilities);

        Assert.NotNull(capabilities.Elicitation?.Form);
        var ui = Assert.IsType<JsonObject>(capabilities.Extensions[McpApps.ExtensionId]);
        Assert.True(ui["custom"]!.GetValue<bool>());
        Assert.NotNull(ui["elicitation"]);
        Assert.Contains(
            McpApps.HtmlMimeType,
            ui["mimeTypes"]!.AsArray().Select(value => value!.GetValue<string>()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsSupported_RequiresBothPeersAndCoreFormCapability()
    {
        var client = CreateClientCapabilities();
        var server = CreateServerCapabilities();

        Assert.True(McpAppElicitation.IsSupported(client, server));
        Assert.False(McpAppElicitation.IsSupported(new ClientCapabilities(), server));
        Assert.False(McpAppElicitation.IsSupported(client, new ServerCapabilities()));

        client.Elicitation = null;
        Assert.False(McpAppElicitation.IsSupported(client, server));
    }

    [Fact]
    public void SetAppUi_RoundTripsMetadataWithoutChangingCoreRequest()
    {
        var request = CreateRequest();

        var result = McpAppElicitation.SetAppUi(
            request,
            "ui://example/choose-option.html");

        Assert.Same(request, result);
        Assert.Equal("Choose an option", result.Message);
        Assert.Equal(
            "ui://example/choose-option.html",
            McpAppElicitation.GetAppUi(result)?.ResourceUri);
    }

    [Fact]
    public void SetAppUiIfSupported_FallsBackToUnmodifiedNativeRequest()
    {
        var request = CreateRequest();

        var result = McpAppElicitation.SetAppUiIfSupported(
            request,
            new ClientCapabilities(),
            CreateServerCapabilities(),
            "ui://example/choose-option.html");

        Assert.Same(request, result);
        Assert.Null(result.Meta);
    }

    [Fact]
    public void SetAppUiIfSupported_AttachesUiWhenNegotiated()
    {
        var request = CreateRequest();

        McpAppElicitation.SetAppUiIfSupported(
            request,
            CreateClientCapabilities(),
            CreateServerCapabilities(),
            "ui://example/choose-option.html");

        Assert.Equal(
            "ui://example/choose-option.html",
            McpAppElicitation.GetAppUi(request)?.ResourceUri);
    }

    [Fact]
    public void SetAppUiIfSupported_ClientOverload_AttachesUiWhenClientSupportsIt()
    {
        var request = CreateRequest();

        McpAppElicitation.SetAppUiIfSupported(
            request,
            CreateClientCapabilities(),
            "ui://example/choose-option.html");

        Assert.NotNull(McpAppElicitation.GetAppUi(request));
    }

    [Theory]
    [InlineData("https://example.com/view.html")]
    [InlineData("relative/view.html")]
    [InlineData("")]
    public void SetAppUi_RejectsInvalidResourceUri(string resourceUri)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            McpAppElicitation.SetAppUi(CreateRequest(), resourceUri));
    }

    [Fact]
    public void SetAppUi_RejectsUrlMode()
    {
        var request = new ElicitRequestParams
        {
            Mode = "url",
            Message = "Continue",
            ElicitationId = "123",
            Url = "https://example.com",
        };

        Assert.Throws<ArgumentException>(() =>
            McpAppElicitation.SetAppUi(request, "ui://example/view.html"));
    }

    [Fact]
    public void GetAppUi_ReturnsNullForMalformedOrNonUiMetadata()
    {
        var malformed = CreateRequest();
        malformed.Meta = new JsonObject { ["ui"] = "not-an-object" };
        Assert.Null(McpAppElicitation.GetAppUi(malformed));

        var nonUi = CreateRequest();
        nonUi.Meta = new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["resourceUri"] = "https://example.com",
            },
        };
        Assert.Null(McpAppElicitation.GetAppUi(nonUi));
    }

    private static ClientCapabilities CreateClientCapabilities()
    {
        var capabilities = new ClientCapabilities();
        return McpAppElicitation.AddClientCapabilities(capabilities);
    }

    private static ServerCapabilities CreateServerCapabilities() => new()
    {
        Extensions = new Dictionary<string, object>
        {
            [McpApps.ExtensionId] = new McpUiServerCapabilities
            {
                Elicitation = new McpUiElicitationCapability(),
            },
        },
    };

    private static ElicitRequestParams CreateRequest() => new()
    {
        Message = "Choose an option",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
            {
                ["choice"] = new ElicitRequestParams.StringSchema(),
            },
            Required = ["choice"],
        },
    };
}
