using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using System.Resources;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the external description source feature on <see cref="McpServerToolAttribute"/>
/// (issue https://github.com/modelcontextprotocol/csharp-sdk/issues/1516).
/// </summary>
public class McpServerToolExternalDescriptionTests
{
    [Fact]
    public void ExternalDescription_StaticProperty_IsResolved()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(FromProperty), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("Resolved from static property.", tool.ProtocolTool.Description);
    }

    [Fact]
    public void ExternalDescription_StaticField_IsResolved()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(FromField), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("Resolved from static field.", tool.ProtocolTool.Description);
    }

    [Fact]
    public void ExternalDescription_StaticMethod_IsResolved()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(FromMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("Resolved from static method.", tool.ProtocolTool.Description);
    }

    [Fact]
    public void ExternalDescription_TakesPrecedenceOverDescriptionAttribute()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(OverridesDescriptionAttribute), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("Resolved from static property.", tool.ProtocolTool.Description);
    }

    [Fact]
    public void ExternalDescription_MissingResource_FallsBackToDescriptionAttribute()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(MissingFallsBack), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("Fallback description.", tool.ProtocolTool.Description);
    }

    [Fact]
    public void ExternalDescription_NameWithoutType_NoEffect()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(NameOnly), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("Plain description.", tool.ProtocolTool.Description);
    }

    [Fact]
    public void ExternalDescription_ResourceManager_IsResolved()
    {
        MethodInfo method = typeof(McpServerToolExternalDescriptionTests)
            .GetMethod(nameof(FromResourceManager), BindingFlags.NonPublic | BindingFlags.Static)!;

        McpServerTool tool = McpServerTool.Create(method);

        Assert.Equal("From resource manager.", tool.ProtocolTool.Description);
    }

    // ---- Inline "resource" type used by the resolver tests. ----

    private static class ToolDescriptions
    {
        public static string PropertyDescription => "Resolved from static property.";
        public static readonly string FieldDescription = "Resolved from static field.";
        public static string MethodDescription() => "Resolved from static method.";
    }

    /// <summary>
    /// Minimal in-memory <see cref="ResourceManager"/> used to verify the fallback path that resolves
    /// descriptions through a static <c>ResourceManager</c> property.
    /// </summary>
    private sealed class InMemoryResourceManager : ResourceManager
    {
        private readonly Dictionary<string, string> _strings;

        public InMemoryResourceManager(Dictionary<string, string> strings)
        {
            _strings = strings;
        }

        public override string? GetString(string name) => _strings.TryGetValue(name, out string? value) ? value : null;

        public override string? GetString(string name, System.Globalization.CultureInfo? culture) => GetString(name);
    }

    private static class ResourceManagerHost
    {
        public static ResourceManager ResourceManager { get; } = new InMemoryResourceManager(new()
        {
            ["WelcomeMessage"] = "From resource manager.",
        });
    }

    [McpServerTool(DescriptionResourceType = typeof(ToolDescriptions), DescriptionResourceName = nameof(ToolDescriptions.PropertyDescription))]
    private static string FromProperty() => "ok";

    [McpServerTool(DescriptionResourceType = typeof(ToolDescriptions), DescriptionResourceName = nameof(ToolDescriptions.FieldDescription))]
    private static string FromField() => "ok";

    [McpServerTool(DescriptionResourceType = typeof(ToolDescriptions), DescriptionResourceName = nameof(ToolDescriptions.MethodDescription))]
    private static string FromMethod() => "ok";

    [McpServerTool(DescriptionResourceType = typeof(ToolDescriptions), DescriptionResourceName = nameof(ToolDescriptions.PropertyDescription))]
    [Description("Compiled-in description that should be overridden.")]
    private static string OverridesDescriptionAttribute() => "ok";

    [McpServerTool(DescriptionResourceType = typeof(ToolDescriptions), DescriptionResourceName = "DoesNotExist")]
    [Description("Fallback description.")]
    private static string MissingFallsBack() => "ok";

    [McpServerTool(DescriptionResourceName = nameof(ToolDescriptions.PropertyDescription))]
    [Description("Plain description.")]
    private static string NameOnly() => "ok";

    [McpServerTool(DescriptionResourceType = typeof(ResourceManagerHost), DescriptionResourceName = "WelcomeMessage")]
    private static string FromResourceManager() => "ok";
}
