using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

public static class McpJsonUtilitiesTests
{
    [Fact]
    public static void DefaultOptions_IsSingleton()
    {
        var options = McpJsonUtilities.DefaultOptions;

        Assert.NotNull(options);
        Assert.True(options.IsReadOnly);
        Assert.Same(options, McpJsonUtilities.DefaultOptions);
    }

    [Fact]
    public static void DefaultOptions_UseReflectionWhenEnabled()
    {
        var options = McpJsonUtilities.DefaultOptions;
        bool isReflectionEnabled = JsonSerializer.IsReflectionEnabledByDefault;
        Type anonType = new { Id = 42 }.GetType();

        Assert.True(isReflectionEnabled); // To be disabled once https://github.com/dotnet/extensions/pull/6241 is incorporated
        Assert.Equal(isReflectionEnabled, options.TryGetTypeInfo(anonType, out _));
    }
}
