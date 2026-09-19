#pragma warning disable MCPEXP003

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for MCP Apps extension support: McpApps constants, typed metadata models,
/// McpAppUiAttribute, SetAppUi, and ApplyAppUiAttributes.
/// </summary>
public class McpAppsTests
{
    #region F1: Constants

    [Fact]
    public void McpApps_Constants_HaveExpectedValues()
    {
        Assert.Equal("text/html;profile=mcp-app", McpApps.HtmlMimeType);
        Assert.Equal("io.modelcontextprotocol/ui", McpApps.ExtensionId);
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

        var json = JsonSerializer.Serialize(meta, McpApps.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpUiToolMeta>(json, McpApps.SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("ui://weather/view.html", deserialized.ResourceUri);
        Assert.Equal(["model", "app"], deserialized.Visibility);
    }

    [Fact]
    public void McpUiToolMeta_OmitsNullProperties()
    {
        var meta = new McpUiToolMeta { ResourceUri = "ui://app" };
        var json = JsonSerializer.Serialize(meta, McpApps.SerializerOptions);
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

        var json = JsonSerializer.Serialize(meta, McpApps.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpUiResourceMeta>(json, McpApps.SerializerOptions);

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
            MimeTypes = [McpApps.HtmlMimeType],
        };

        var json = JsonSerializer.Serialize(caps, McpApps.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<McpUiClientCapabilities>(json, McpApps.SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal([McpApps.HtmlMimeType], deserialized.MimeTypes);
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
                        "mimeTypes": ["{{McpApps.HtmlMimeType}}"]
                    }
                }
            }
            """;

        var caps = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(caps);

        var uiCaps = McpApps.GetUiCapability(caps);

        Assert.NotNull(uiCaps);
        Assert.Equal([McpApps.HtmlMimeType], uiCaps.MimeTypes);
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

    [Theory]
    [InlineData("\"a string value\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[1, 2, 3]")]
    public void GetUiCapability_ReturnsNull_WhenExtensionValueIsMalformed(string malformedValue)
    {
        var json = $$"""
            {
                "extensions": {
                    "{{McpApps.ExtensionId}}": {{malformedValue}}
                }
            }
            """;

        var caps = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(caps);

        // Should return null gracefully, not throw
        Assert.Null(McpApps.GetUiCapability(caps));
    }

    [Fact]
    public void GetUiCapability_ReturnsCapabilities_WhenValueIsStronglyTyped()
    {
#pragma warning disable MCPEXP001
        var caps = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                [McpApps.ExtensionId] = new McpUiClientCapabilities
                {
                    MimeTypes = [McpApps.HtmlMimeType],
                },
            }
        };
#pragma warning restore MCPEXP001

        var uiCaps = McpApps.GetUiCapability(caps);
        Assert.NotNull(uiCaps);
        Assert.Equal([McpApps.HtmlMimeType], uiCaps.MimeTypes);
    }

    #endregion

    #region F6: McpAppUiAttribute via ApplyAppUiAttributes

    [Fact]
    public void ApplyAppUiAttributes_PopulatesUiObject()
    {
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.WeatherTool))!;
        var tool = McpServerTool.Create(method, target: null);

        McpApps.ApplyAppUiAttributes(tool);

        var meta = tool.ProtocolTool.Meta;
        Assert.NotNull(meta);

        // Structured "ui" object
        var uiNode = meta["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://weather/view.html", uiNode["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public void ApplyAppUiAttributes_WithVisibility_IncludesVisibilityInUiObject()
    {
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.ModelOnlyTool))!;
        var tool = McpServerTool.Create(method, target: null);

        McpApps.ApplyAppUiAttributes(tool);

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://model-only/view.html", uiNode["resourceUri"]?.GetValue<string>());

        var visibility = uiNode["visibility"]?.AsArray();
        Assert.NotNull(visibility);
        Assert.Single(visibility);
        Assert.Equal(McpUiToolVisibility.Model, visibility[0]?.GetValue<string>());
    }

    [Fact]
    public void ApplyAppUiAttributes_ExplicitMeta_TakesPrecedence()
    {
        // Explicit Meta["ui"] in options should override the attribute
        var method = typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.WeatherTool))!;
        var explicitMeta = new JsonObject
        {
            ["ui"] = new JsonObject { ["resourceUri"] = "ui://explicit/override.html" },
        };

        var tool = McpServerTool.Create(method, target: null, new McpServerToolCreateOptions { Meta = explicitMeta });

        McpApps.ApplyAppUiAttributes(tool);

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        // Explicit Meta["ui"] wins — ApplyAppUiAttributes does not overwrite
        Assert.Equal("ui://explicit/override.html", uiNode?["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public void ApplyAppUiAttributes_Collection_ProcessesAllTools()
    {
        var tools = new[]
        {
            McpServerTool.Create(typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.WeatherTool))!, target: null),
            McpServerTool.Create(typeof(TestToolsWithAppUi).GetMethod(nameof(TestToolsWithAppUi.ModelOnlyTool))!, target: null),
        };

        McpApps.ApplyAppUiAttributes(tools);

        Assert.NotNull(tools[0].ProtocolTool.Meta?["ui"]);
        Assert.NotNull(tools[1].ProtocolTool.Meta?["ui"]);
    }

    [Fact]
    public void ApplyAppUiAttributes_NoAttribute_DoesNothing()
    {
        var tool = McpServerTool.Create(
            (string input) => input,
            new McpServerToolCreateOptions { Name = "plain_tool" });

        McpApps.ApplyAppUiAttributes(tool);

        Assert.Null(tool.ProtocolTool.Meta);
    }

    #endregion

    #region F7: SetAppUi

    [Fact]
    public void SetAppUi_PopulatesUiObject()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions { Name = "get_weather" });

        McpApps.SetAppUi(tool, new McpUiToolMeta { ResourceUri = "ui://weather/view.html" });

        var meta = tool.ProtocolTool.Meta;
        Assert.NotNull(meta);

        var uiNode = meta["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://weather/view.html", uiNode["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public void SetAppUi_WithVisibility_IncludesVisibilityInUiObject()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions { Name = "get_weather" });

        McpApps.SetAppUi(tool, new McpUiToolMeta
        {
            ResourceUri = "ui://weather/view.html",
            Visibility = [McpUiToolVisibility.Model],
        });

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);

        var visibility = uiNode["visibility"]?.AsArray();
        Assert.NotNull(visibility);
        Assert.Single(visibility);
        Assert.Equal(McpUiToolVisibility.Model, visibility[0]?.GetValue<string>());
    }

    [Fact]
    public void SetAppUi_DoesNotOverwrite_ExistingUiKey()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions
            {
                Name = "get_weather",
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = "ui://explicit/view.html" },
                },
            });

        McpApps.SetAppUi(tool, new McpUiToolMeta { ResourceUri = "ui://new/view.html" });

        // Existing Meta["ui"] is preserved
        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.Equal("ui://explicit/view.html", uiNode?["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public void SetAppUi_NullResourceUri_ProducesUiObjectWithoutResourceUri()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions { Name = "get_weather" });

        McpApps.SetAppUi(tool, new McpUiToolMeta { Visibility = [McpUiToolVisibility.App] });

        var uiNode = tool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Null(uiNode["resourceUri"]);
    }

    [Fact]
    public void SetAppUi_ReturnsSameTool()
    {
        var tool = McpServerTool.Create(
            (string location) => $"Weather for {location}",
            new McpServerToolCreateOptions { Name = "get_weather" });

        var result = McpApps.SetAppUi(tool, new McpUiToolMeta { ResourceUri = "ui://weather/view.html" });
        Assert.Same(tool, result);
    }

    #endregion

    #region Builder Extension: WithMcpApps

    [Fact]
    public void WithMcpApps_AppliesAppUiAttributes_ViaOptions()
    {
        var sc = new ServiceCollection();
        sc.AddMcpServer()
            .WithTools([typeof(TestToolsWithAppUi)])
            .WithMcpApps();

        using var sp = sc.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.ToolCollection);
        Assert.NotEmpty(options.ToolCollection);

        // Both tools should have their [McpAppUi] attributes applied
        var toolsWithUi = options.ToolCollection.Where(t => t.ProtocolTool.Meta?["ui"] is not null).ToList();
        Assert.Equal(2, toolsWithUi.Count);

        var weatherTool = toolsWithUi.First(t => t.ProtocolTool.Meta!["ui"]!["resourceUri"]?.GetValue<string>() == "ui://weather/view.html");
        Assert.NotNull(weatherTool);
    }

    [Fact]
    public void WithMcpApps_EmptyToolCollection_DoesNotThrow()
    {
        var sc = new ServiceCollection();
        sc.AddMcpServer()
            .WithMcpApps();

        using var sp = sc.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // Should be a no-op when no tools are registered
        Assert.True(options.ToolCollection is null || options.ToolCollection.IsEmpty);
    }

    [Fact]
    public void WithMcpApps_AdvertisesServerCapability()
    {
        var sc = new ServiceCollection();
        sc.AddMcpServer()
            .WithMcpApps();

        using var sp = sc.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Capabilities);
        Assert.NotNull(options.Capabilities.Extensions);
        Assert.True(options.Capabilities.Extensions.ContainsKey(McpApps.ExtensionId));
    }

    #endregion

    #region WithAppTool

    [Fact]
    public async Task WithAppTool_RegistersLinkedToolAndHtmlResource()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(
                (string location) => $"Weather for {location}",
                "ui://weather/view.html",
                () => "<html>weather</html>",
                new McpServerToolCreateOptions
                {
                    Name = "weather",
                    Description = "Gets weather",
                    Meta = new JsonObject { ["custom"] = "value" },
                });

        await using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var tool = Assert.Single(options.ToolCollection!);
        var resource = Assert.Single(options.ResourceCollection!);

        Assert.Equal("weather", tool.ProtocolTool.Name);
        Assert.Equal("Gets weather", tool.ProtocolTool.Description);
        Assert.Equal("value", tool.ProtocolTool.Meta?["custom"]?.GetValue<string>());
        Assert.Equal("ui://weather/view.html", tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
        Assert.Equal("ui://weather/view.html", resource.ProtocolResourceTemplate.UriTemplate);
        Assert.Equal(McpApps.HtmlMimeType, resource.ProtocolResourceTemplate.MimeType);
        Assert.Contains(McpApps.ExtensionId, options.Capabilities!.Extensions!.Keys);
    }

    [Fact]
    public async Task WithAppTool_PreservesMatchingToolUiMetadata()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(
                () => "result",
                "ui://explicit/view.html",
                () => "<html />",
                new McpServerToolCreateOptions
                {
                    Name = "app_tool",
                    Meta = new JsonObject
                    {
                        ["ui"] = new JsonObject
                        {
                            ["resourceUri"] = "ui://explicit/view.html",
                            ["visibility"] = new JsonArray(McpUiToolVisibility.App),
                        },
                    },
                });

        await using var serviceProvider = services.BuildServiceProvider();
        var tool = Assert.Single(serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!);

        Assert.Equal("ui://explicit/view.html", tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
        Assert.Equal(McpUiToolVisibility.App, tool.ProtocolTool.Meta?["ui"]?["visibility"]?[0]?.GetValue<string>());
    }

    [Fact]
    public async Task WithAppTool_AddsResourceUriToExistingToolUiMetadata()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(
                () => "result",
                "ui://weather/view.html",
                () => "<html />",
                new McpServerToolCreateOptions
                {
                    Name = "app_tool",
                    Meta = new JsonObject
                    {
                        ["ui"] = new JsonObject
                        {
                            ["visibility"] = new JsonArray(McpUiToolVisibility.Model),
                        },
                    },
                });

        await using var serviceProvider = services.BuildServiceProvider();
        var tool = Assert.Single(serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!);

        Assert.Equal("ui://weather/view.html", tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
        Assert.Equal(McpUiToolVisibility.Model, tool.ProtocolTool.Meta?["ui"]?["visibility"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void WithAppTool_RejectsNullToolUiResourceUri()
    {
        var builder = new ServiceCollection().AddMcpServer();

        var exception = Assert.Throws<ArgumentException>(() => builder.WithAppTool(
            () => "result",
            "ui://weather/view.html",
            () => "<html />",
            new McpServerToolCreateOptions
            {
                Name = "app_tool",
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = null },
                },
            }));

        Assert.Equal("toolOptions", exception.ParamName);
        Assert.Contains("must be a string", exception.Message);
    }

    [Fact]
    public void WithAppTool_RejectsConflictingToolUiMetadata()
    {
        var builder = new ServiceCollection().AddMcpServer();

        var exception = Assert.Throws<ArgumentException>(() => builder.WithAppTool(
            () => "result",
            "ui://weather/view.html",
            () => "<html />",
            new McpServerToolCreateOptions
            {
                Name = "app_tool",
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = "ui://other/view.html" },
                },
            }));

        Assert.Equal("toolOptions", exception.ParamName);
        Assert.Contains("ui://other/view.html", exception.Message);
        Assert.Contains("ui://weather/view.html", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithAppTool_RejectsLowLevelResourceCollision(bool registerLowLevelResourceFirst)
    {
        const string ResourceUri = "ui://shared/view.html";
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        var lowLevelResource = McpServerResource.Create(
            () => "low-level",
            new() { UriTemplate = ResourceUri, MimeType = "text/plain" });

        if (registerLowLevelResourceFirst)
        {
            builder.WithResources([lowLevelResource]);
        }

        builder.WithAppTool(
            () => "app",
            ResourceUri,
            () => "<html>app</html>",
            new() { Name = "app_tool" });

        if (!registerLowLevelResourceFirst)
        {
            builder.WithResources([lowLevelResource]);
        }

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value);

        Assert.Contains(ResourceUri, exception.Message);
        Assert.Contains("already registered", exception.Message);
    }

    [Theory]
    [InlineData("ui://weather/view.html", "ui://WEATHER/view.html")]
    [InlineData("ui://weather/literal%7Bview%7D.html", "ui://weather/literal%7bview%7d.html")]
    [InlineData("ui://weather/a/../view.html", "ui://weather/view.html")]
    [InlineData("ui://weather/view.html#one", "ui://weather/view.html#two")]
    [InlineData("ui://weather/%76iew.html", "ui://weather/view.html")]
    public async Task WithAppTool_RejectsEquivalentResourceUrisWithDifferentSpelling(
        string firstResourceUri,
        string secondResourceUri)
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(() => "first", firstResourceUri, () => "first", new() { Name = "first" })
            .WithAppTool(() => "second", secondResourceUri, () => "second", new() { Name = "second" });

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value);

        Assert.Contains(firstResourceUri, exception.Message);
        Assert.Contains(secondResourceUri, exception.Message);
        Assert.Contains("same resource", exception.Message);
    }

    [Fact]
    public async Task WithAppTool_AllowsDistinctResourceQueries()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(() => "first", "ui://weather/view.html?mode=compact", () => "first", new() { Name = "first" })
            .WithAppTool(() => "second", "ui://weather/view.html?mode=full", () => "second", new() { Name = "second" });

        await using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(2, options.ToolCollection!.Count);
        Assert.Equal(2, options.ResourceCollection!.Count);
    }

    [Fact]
    public async Task WithAppTool_RejectsPostConfiguredResourceLinkMismatch()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(
                () => "result",
                "ui://weather/view.html",
                () => "<html />",
                new() { Name = "app_tool" });
        services.PostConfigure<McpServerOptions>(options =>
        {
            McpServerTool tool = Assert.Single(options.ToolCollection!);
            tool.ProtocolTool.Meta!["ui"]!["resourceUri"] = "ui://other/view.html";
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value);

        Assert.Contains("app_tool", exception.Message);
        Assert.Contains("ui://weather/view.html", exception.Message);
    }

    [Fact]
    public async Task WithAppTool_RejectsDuplicateToolName()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(() => "first", "ui://first/view.html", () => "first", new() { Name = "shared" })
            .WithAppTool(() => "second", "ui://second/view.html", () => "second", new() { Name = "shared" });

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value);

        Assert.Contains("shared", exception.Message);
        Assert.Contains("already registered", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithAppTool_RejectsLowLevelToolCollision(bool registerLowLevelToolFirst)
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        var lowLevelTool = McpServerTool.Create(() => "low-level", new() { Name = "shared" });

        if (registerLowLevelToolFirst)
        {
            builder.WithTools([lowLevelTool]);
        }

        builder.WithAppTool(
            () => "app",
            "ui://shared/view.html",
            () => "<html>app</html>",
            new() { Name = "shared" });

        if (!registerLowLevelToolFirst)
        {
            builder.WithTools([lowLevelTool]);
        }

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value);

        Assert.Contains("shared", exception.Message);
        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    public async Task WithAppTool_DuplicateResourceUriKeepsSingleResource()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(() => "first", "ui://shared/view.html", () => "first", new() { Name = "first" })
            .WithAppTool(() => "second", "ui://shared/view.html", () => "second", new() { Name = "second" });

        await using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(2, options.ToolCollection!.Count);
        Assert.Single(options.ResourceCollection!);
    }

    [Fact]
    public void WithAppTool_RejectsMissingConfiguration()
    {
        var builder = new ServiceCollection().AddMcpServer();
        Func<string> htmlFactory = () => "html";
        Delegate method = () => "result";

        Assert.Throws<ArgumentNullException>(() => builder.WithAppTool(null!, "ui://test", htmlFactory));
        Assert.Throws<ArgumentNullException>(() => builder.WithAppTool(method, null!, htmlFactory));
        Assert.Throws<ArgumentNullException>(() => builder.WithAppTool(method, "ui://test", null!));
    }

    [Fact]
    public void WithAppTool_RejectsHtmlFactoryWithUnsupportedReturnType()
    {
        var builder = new ServiceCollection().AddMcpServer();
        Func<ReadResourceResult> htmlFactory = () => new();

        var exception = Assert.Throws<ArgumentException>(() => builder.WithAppTool(
            () => "result",
            "ui://weather/view.html",
            htmlFactory));

        Assert.Equal("htmlFactory", exception.ParamName);
        Assert.Contains("string", exception.Message);
    }

    [Fact]
    public void WithAppTool_RejectsHtmlFactoryWithNonCancellationParameter()
    {
        var builder = new ServiceCollection().AddMcpServer();
        Func<string, string> htmlFactory = value => value;

        var exception = Assert.Throws<ArgumentException>(() => builder.WithAppTool(
            () => "result",
            "ui://weather/view.html",
            htmlFactory));

        Assert.Equal("htmlFactory", exception.ParamName);
        Assert.Contains(nameof(CancellationToken), exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("weather/view.html")]
    [InlineData("https://weather.example/view.html")]
    [InlineData("ui:/weather/view.html")]
    [InlineData("ui://")]
    [InlineData("ui://weather/{view}.html")]
    public void WithAppTool_RejectsInvalidResourceUri(string resourceUri)
    {
        var builder = new ServiceCollection().AddMcpServer();
        Delegate method = () => "result";
        Func<string> htmlFactory = () => "html";

        var exception = Assert.Throws<ArgumentException>(() => builder.WithAppTool(method, resourceUri, htmlFactory));

        Assert.Equal("resourceUri", exception.ParamName);
    }

    [Fact]
    public async Task WithAppTool_AcceptsEncodedBracesAsLiteralUriContent()
    {
        const string ResourceUri = "ui://weather/literal%7Bview%7D.html";
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithAppTool(
                () => "result",
                ResourceUri,
                () => "<html />",
                new() { Name = "app_tool" });

        await using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var tool = Assert.Single(options.ToolCollection!);
        var resource = Assert.Single(options.ResourceCollection!);

        Assert.Equal(ResourceUri, tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
        Assert.Equal(ResourceUri, resource.ProtocolResourceTemplate.UriTemplate);
        Assert.False(resource.IsTemplated);
        Assert.True(resource.IsMatch(ResourceUri));
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
    }

    #endregion
}
