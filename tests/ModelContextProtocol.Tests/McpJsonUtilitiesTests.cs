using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
        Type anonType = new { Id = 42 }.GetType();

        Assert.Equal(JsonSerializer.IsReflectionEnabledByDefault, options.TryGetTypeInfo(anonType, out _));
    }

    [Fact]
    public static void DefaultOptions_UnknownEnumHandling()
    {
        var options = McpJsonUtilities.DefaultOptions;

        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            Assert.Equal("\"A\"", JsonSerializer.Serialize(EnumWithoutAnnotation.A, options));
            Assert.Equal("\"A\"", JsonSerializer.Serialize(EnumWithAnnotation.A, options));
        }
        else
        {
            options = new(options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
            Assert.Equal("1", JsonSerializer.Serialize(EnumWithoutAnnotation.A, options));
            Assert.Equal("\"A\"", JsonSerializer.Serialize(EnumWithAnnotation.A, options));
        }
    }

    public enum EnumWithoutAnnotation { A = 1, B = 2, C = 3 }

    [JsonConverter(typeof(JsonStringEnumConverter<EnumWithAnnotation>))]
    public enum EnumWithAnnotation { A = 1, B = 2, C = 3 }
}
