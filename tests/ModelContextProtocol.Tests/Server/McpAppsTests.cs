#pragma warning disable MCPEXP001

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for MCP Apps extension support: McpApps constants, typed metadata models,
/// McpAppUiAttribute, and McpServerToolCreateOptions.AppUi.
/// </summary>
public class McpAppsTests
{
    #region F1: Constants

    [Fact]
    public void McpApps_Constants_HaveExpectedValues()
    {
        Assert.Equal("text/html;profile=mcp-app", McpApps.ResourceMimeType);
        Assert.Equal("io.modelcontextprotocol/ui", McpApps.ExtensionId);
        Assert.Equal("ui/resourceUri", McpApps.ResourceUriMetaKey);
    }

    [Fact]
    public void McpUiToolVisibility_Constants_HaveExpectedValues()
    {
        Assert.Equal("model", McpUiToolVisibility.Model);
        Assert.Equal("app", McpUiToolVisibility.App);
    }

    #endregion

    #region F2: Typed Metadata Models

    [Fact]
    public void McpUiToolMeta_DefaultsToNull()
    {
        var meta = new McpUiToolMeta();
        Assert.Null(meta.ResourceUri);
        Assert.Null(meta.Visibility);
    }

    [Fact]
    public void McpUiToolMeta_CanBeRoundtrippedAsJson()
    {
        var meta = new McpUiToolMeta
        {
            ResourceUri = "ui://weather/view.html",
            Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App],
        };

        var json = JsonSerializer.Serialize(meta, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpUiToolMeta>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("ui://weather/view.html", deserialized.ResourceUri);
        Assert.Equal(["model", "app"], deserialized.Visibility);
    }

    [Fact]
    public void McpUiToolMeta_OmitsNullProperties()
    {
        var meta = new McpUiToolMeta { ResourceUri = "ui://app" };
        var json = JsonSerializer.Serialize(meta, McpJsonUtilities.DefaultOptions);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("resourceUri", out _));
        Assert.False(doc.RootElement.TryGetProperty("visibility", out _));
    }

    [Fact]
    public void McpUiResourceMeta_CanBeRoundtrippedAsJson()
    {
        var meta = new McpUiResourceMeta
        {
            Domain = "https://app.example.com",
            PrefersBorder = true,
            Csp = new McpUiResourceCsp
            {
                ConnectDomains = ["https://api.example.com"],
                ResourceDomains = ["https://cdn.example.com"],
                FrameDomains = ["https://embed.example.com"],
                BaseUris = ["https://app.example.com"],
            },
            Permissions = new McpUiResourcePermissions
            {
                Allow = ["camera", "microphone"],
            },
        };

        var json = JsonSerializer.Serialize(meta, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpUiResourceMeta>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("https://app.example.com", deserialized.Domain);
        Assert.True(deserialized.PrefersBorder);
        Assert.NotNull(deserialized.Csp);
        Assert.Equal(["https://api.example.com"], deserialized.Csp.ConnectDomains);
        Assert.Equal(["https://cdn.example.com"], deserialized.Csp.ResourceDomains);
        Assert.Equal(["https://embed.example.com"], deserialized.Csp.FrameDomains);
        Assert.Equal(["https://app.example.com"], deserialized.Csp.BaseUris);
        Assert.NotNull(deserialized.Permissions);
        Assert.Equal(["camera", "microphone"], deserialized.Permissions.Allow);
    }

    [Fact]
    public void McpUiClientCapabilities_CanBeRoundtrippedAsJson()
    {
        var caps = new McpUiClientCapabilities
        {
            MimeTypes = [McpApps.ResourceMimeType],
        };

        var json = JsonSerializer.Serialize(caps, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpUiClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal([McpApps.ResourceMimeType], deserialized.MimeTypes);
    }

    #endregion

    #region F3: GetUiCapability

    [Fact]
    public void GetUiCapability_ReturnsNull_WhenCapabilitiesIsNull()
    {
        Assert.Null(McpApps.GetUiCapability(null));
    }

    [Fact]
    public void GetUiCapability_ReturnsNull_WhenExtensionsIsNull()
    {
        var caps = new ClientCapabilities();
        Assert.Null(McpApps.GetUiCapability(caps));
    }

    [Fact]
    public void GetUiCapability_ReturnsNull_WhenExtensionKeyIsMissing()
    {
#pragma warning disable MCPEXP001
        var caps = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                ["other.extension"] = new { },
            }
        };
#pragma warning restore MCPEXP001
        Assert.Null(McpApps.GetUiCapability(caps));
    }

    [Fact]
    public void GetUiCapability_ReturnsCapabilities_WhenExtensionIsPresent()
    {
        // Simulate what the SDK does when deserializing ClientCapabilities from JSON:
        // extensions values come in as JsonElement.
        var json = $$"""
            {
                "extensions": {
                    "{{McpApps.ExtensionId}}": {
                        "mimeTypes": ["{{McpApps.ResourceMimeType}}"]
                    }
                }
            }
            """;

        var caps = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(caps);

        var uiCaps = McpApps.GetUiCapability(caps);

        Assert.NotNull(uiCaps);
        Assert.Equal([McpApps.ResourceMimeType], uiCaps.MimeTypes);
    }

    [Fact]
    public void GetUiCapability_ReturnsNull_WhenExtensionValueIsNull()
    {
        var json = $$"""
            {
                "extensions": {
                    "{{McpApps.ExtensionId}}": null
                }
            }
            """;

        var caps = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(caps);

        Assert.Null(McpApps.GetUiCapability(caps));
    }

    #endregion

    #region F6: McpAppUiAttribute

    [Fact]
    public void McpAppUiAttribute_PopulatesBothUiObjectAndLegacyKey()
    {
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.WeatherTool))!;
        var tool = McpServerTool.Create(method, target: null);

        var meta = tool.ProtocolTool.Meta;
        Assert.NotNull(meta);

        // Structured "ui" object
        var uiNode = meta["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://weather/view.html", uiNode["resourceUri"]?.GetValue<string>());

        // Legacy flat key
        Assert.Equal("ui://weather/view.html", meta[McpApps.ResourceUriMetaKey]?.GetValue<string>());
    }

    [Fact]
    public void McpAppUiAttribute_WithVisibility_IncludesVisibilityInUiObject()
    {
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.ModelOnlyTool))!;
        var tool = McpServerTool.Create(method, target: null);

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://model-only/view.html", uiNode["resourceUri"]?.GetValue<string>());

        var visibility = uiNode["visibility"]?.AsArray();
        Assert.NotNull(visibility);
        Assert.Single(visibility);
        Assert.Equal(McpUiToolVisibility.Model, visibility[0]?.GetValue<string>());
    }

    [Fact]
    public void McpAppUiAttribute_TakesPrecedenceOver_McpMetaAttribute()
    {
        // The tool has both [McpAppUi] and [McpMeta("ui", ...)] — AppUi should win for the "ui" key.
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.ToolWithBothAttributes))!;
        var tool = McpServerTool.Create(method, target: null);

        var meta = tool.ProtocolTool.Meta;
        Assert.NotNull(meta);

        // The "ui" key should be from McpAppUiAttribute, not McpMetaAttribute
        var uiNode = meta["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://app-ui/view.html", uiNode["resourceUri"]?.GetValue<string>());

        // The legacy key should be from McpAppUiAttribute
        Assert.Equal("ui://app-ui/view.html", meta[McpApps.ResourceUriMetaKey]?.GetValue<string>());

        // Other McpMeta attributes should still be present
        Assert.Equal("extra-value", meta["extraKey"]?.GetValue<string>());
    }

    [Fact]
    public void McpAppUiAttribute_ExplicitOptionsMeta_TakesPrecedenceOver_Attribute()
    {
        // Explicit Meta["ui"] in options should override the attribute
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.WeatherTool))!;
        var explicitMeta = new JsonObject
        {
            ["ui"] = new JsonObject { ["resourceUri"] = "ui://explicit/override.html" },
            [McpApps.ResourceUriMetaKey] = "ui://explicit/override.html",
        };

        var tool = McpServerTool.Create(method, target: null, new McpServerToolCreateOptions { Meta = explicitMeta });

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.Equal("ui://explicit/override.html", uiNode?["resourceUri"]?.GetValue<string>());
        Assert.Equal("ui://explicit/override.html", tool.ProtocolTool.Meta?[McpApps.ResourceUriMetaKey]?.GetValue<string>());
    }

    #endregion

    #region F7: McpServerToolCreateOptions.AppUi

    [Fact]
    public void AppUi_PopulatesBothUiObjectAndLegacyKey()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions
            {
                Name = "get_weather",
                AppUi = new McpUiToolMeta { ResourceUri = "ui://weather/view.html" },
            });

        var meta = tool.ProtocolTool.Meta;
        Assert.NotNull(meta);

        var uiNode = meta["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://weather/view.html", uiNode["resourceUri"]?.GetValue<string>());
        Assert.Equal("ui://weather/view.html", meta[McpApps.ResourceUriMetaKey]?.GetValue<string>());
    }

    [Fact]
    public void AppUi_WithVisibility_IncludesVisibilityInUiObject()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions
            {
                Name = "get_weather",
                AppUi = new McpUiToolMeta
                {
                    ResourceUri = "ui://weather/view.html",
                    Visibility = [McpUiToolVisibility.Model],
                },
            });

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);

        var visibility = uiNode["visibility"]?.AsArray();
        Assert.NotNull(visibility);
        Assert.Single(visibility);
        Assert.Equal(McpUiToolVisibility.Model, visibility[0]?.GetValue<string>());
    }

    [Fact]
    public void AppUi_ExplicitMeta_TakesPrecedenceOver_AppUi()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions
            {
                Name = "get_weather",
                // Explicit Meta entry for "ui" should override AppUi
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = "ui://explicit/view.html" },
                },
                AppUi = new McpUiToolMeta { ResourceUri = "ui://app-ui/view.html" },
            });

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        // Explicit Meta["ui"] wins
        Assert.Equal("ui://explicit/view.html", uiNode?["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public void AppUi_NullResourceUri_DoesNotPopulateLegacyKey()
    {
        // AppUi with no ResourceUri should not add the legacy flat key
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions
            {
                Name = "get_weather",
                AppUi = new McpUiToolMeta { Visibility = [McpUiToolVisibility.App] },
            });

        Assert.Null(tool.ProtocolTool.Meta?[McpApps.ResourceUriMetaKey]);
    }

    [Fact]
    public void AppUi_IsPreservedWhenOptionsAreClonedInDeriveOptions()
    {
        // DeriveOptions() calls options.Clone() internally when creating via MethodInfo.
        // If AppUi is not included in Clone(), it would be lost when creating the tool via a method.
        var appUi = new McpUiToolMeta { ResourceUri = "ui://weather/view.html" };
        var options = new McpServerToolCreateOptions { AppUi = appUi };

        // Use the MethodInfo path, which calls DeriveOptions -> options.Clone()
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.WeatherTool))!;
        var tool = McpServerTool.Create(method, target: null, options);

        // The attribute on the method overrides options.AppUi, but both should produce the same meta.
        var meta = tool.ProtocolTool.Meta;
        Assert.NotNull(meta);
        Assert.NotNull(meta["ui"]);
        Assert.NotNull(meta[McpApps.ResourceUriMetaKey]);
    }

    #endregion

    #region Test helper types

    [McpServerToolType]
    private static class TestToolsWithAppUi
    {
        [McpServerTool]
        [McpAppUi(ResourceUri = "ui://weather/view.html")]
        [Description("Get weather")]
        public static string WeatherTool(string location) => $"Weather for {location}";

        [McpServerTool]
        [McpAppUi(ResourceUri = "ui://model-only/view.html", Visibility = [McpUiToolVisibility.Model])]
        public static string ModelOnlyTool(string location) => $"Model only for {location}";

        [McpServerTool]
        [McpAppUi(ResourceUri = "ui://app-ui/view.html")]
        [McpMeta("ui", JsonValue = """{"resourceUri": "ui://mcpmeta/view.html"}""")]
        [McpMeta("extraKey", "extra-value")]
        public static string ToolWithBothAttributes(string input) => input;
    }

    #endregion
}
