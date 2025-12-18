using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Protocol;

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

    [Fact]
    public static void DefaultOptions_CanSerializeIEnumerableOfContentBlock()
    {
        var options = McpJsonUtilities.DefaultOptions;
        
        // Create an IEnumerable<ContentBlock> with different content types
        IEnumerable<ContentBlock> contentBlocks = new List<ContentBlock>
        {
            new TextContentBlock { Text = "Hello World" },
            new TextContentBlock { Text = "Test message" }
        };

        // Should not throw NotSupportedException
        string json = JsonSerializer.Serialize(contentBlocks, options);
        
        Assert.NotNull(json);
        Assert.Contains("Hello World", json);
        Assert.Contains("Test message", json);
        Assert.Contains("\"type\":\"text\"", json);
        
        // Should also be able to deserialize back
        var deserialized = JsonSerializer.Deserialize<IEnumerable<ContentBlock>>(json, options);
        Assert.NotNull(deserialized);
        var deserializedList = deserialized.ToList();
        Assert.Equal(2, deserializedList.Count);
        Assert.All(deserializedList, cb => Assert.Equal("text", cb.Type));
        
        var textBlocks = deserializedList.Cast<TextContentBlock>().ToArray();
        Assert.Equal("Hello World", textBlocks[0].Text);
        Assert.Equal("Test message", textBlocks[1].Text);
    }

    public enum EnumWithoutAnnotation { A = 1, B = 2, C = 3 }

    [JsonConverter(typeof(JsonStringEnumConverter<EnumWithAnnotation>))]
    public enum EnumWithAnnotation { A = 1, B = 2, C = 3 }
}
