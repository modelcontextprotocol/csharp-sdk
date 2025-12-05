using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

public static class RequestOptionsTests
{
    [Fact]
    public static void RequestOptions_DefaultConstructor()
    {
        // Arrange & Act
        var options = new RequestOptions();

        // Assert
        // ProgressToken and JsonSerializerOptions should be null by default
        Assert.Null(options.ProgressToken);
        Assert.Null(options.JsonSerializerOptions);
        // Meta should return a new (empty) JsonObject when accessed
        Assert.NotNull(options.Meta);
        Assert.Empty(options.Meta);
    }

    [Fact]
    public static void RequestOptions_MetaSetter_SetsValue()
    {
        // Arrange
        var options = new RequestOptions();
        var metaValue = new JsonObject
        {
            ["key"] = "value"
        };

        // Act
        options.Meta = metaValue;

        // Assert
        Assert.Same(metaValue, options.Meta);
        Assert.Equal("value", options.Meta["key"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_Meta_SetsSingleField()
    {
        // Arrange
        var options = new RequestOptions();

        // Act
        options.Meta!["key"] = "value";

        // Assert
        Assert.Equal("value", options.Meta!["key"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_MetaSetter_NullValue_RemovesMeta()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject { ["key"] = "value" }
        };

        // Act
        options.Meta = null;

        // Assert - accessing Meta will create a new empty JsonObject
        Assert.NotNull(options.Meta);
        Assert.Empty(options.Meta);
    }

    [Fact]
    public static void RequestOptions_MetaSetter_WithProgressToken_InMeta()
    {
        // Arrange
        var options = new RequestOptions();
        var newMeta = new JsonObject
        {
            ["custom"] = "data",
            ["progressToken"] = "token"
        };

        // Act
        options.Meta = newMeta;

        // Assert
        Assert.Equal("token", options.ProgressToken?.ToString());
        Assert.Equal("data", options.Meta["custom"]?.ToString());
        Assert.True(options.Meta.ContainsKey("progressToken"));
    }

    [Fact]
    public static void RequestOptions_ProgressTokenGetter_ReturnsNull_WhenNotSet()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject { ["other"] = "value" }
        };

        // Act
        var token = options.ProgressToken;

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public static void RequestOptions_ProgressTokenSetter_StringToken_SetsInMeta()
    {
        // Arrange
        var options = new RequestOptions();
        var token = new ProgressToken("my-token");

        // Act
        options.ProgressToken = token;

        // Assert
        Assert.Equal("my-token", options.ProgressToken?.ToString());
        Assert.NotNull(options.Meta);
        Assert.Equal("my-token", options.Meta["progressToken"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_ProgressTokenSetter_LongToken_SetsInMeta()
    {
        // Arrange
        var options = new RequestOptions();
        var token = new ProgressToken(42L);

        // Act
        options.ProgressToken = token;

        // Assert
        Assert.Equal("42", options.ProgressToken?.ToString());
        Assert.NotNull(options.Meta);
        Assert.Equal(42L, options.Meta["progressToken"]?.AsValue().GetValue<long>());
    }

    [Fact]
    public static void RequestOptions_ProgressTokenSetter_Null_RemovesFromMeta()
    {
        // Arrange
        var options = new RequestOptions
        {
            ProgressToken = new ProgressToken("token-to-remove")
        };

        // Act
        options.ProgressToken = null;

        // Assert
        Assert.Null(options.ProgressToken);
        Assert.False(options.Meta!.ContainsKey("progressToken"));
    }

    [Fact]
    public static void RequestOptions_ProgressTokenSetter_Null_WhenNoProgressToken_DoesNothing()
    {
        // Arrange
        var options = new RequestOptions();

        // Act
        options.ProgressToken = null;

        // Assert
        Assert.Null(options.ProgressToken);
    }

    [Fact]
    public static void RequestOptions_ProgressTokenGetter_StringValue_ReturnsCorrectToken()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject { ["progressToken"] = "test-token" }
        };

        // Act
        var token = options.ProgressToken;

        // Assert
        Assert.NotNull(token);
        Assert.Equal("test-token", token.Value.ToString());
    }

    [Fact]
    public static void RequestOptions_ProgressTokenGetter_LongValue_ReturnsCorrectToken()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject { ["progressToken"] = 123L }
        };

        // Act
        var token = options.ProgressToken;

        // Assert
        Assert.NotNull(token);
        Assert.Equal("123", token.Value.ToString());
    }

    [Fact]
    public static void RequestOptions_ProgressTokenGetter_InvalidValue_ReturnsNull()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject { ["progressToken"] = new JsonObject() }
        };

        // Act
        var token = options.ProgressToken;

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public static void RequestOptions_JsonSerializerOptions_GetSet()
    {
        // Arrange
        var options = new RequestOptions();
        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };

        // Act
        options.JsonSerializerOptions = serializerOptions;

        // Assert
        Assert.Same(serializerOptions, options.JsonSerializerOptions);
    }

    [Fact]
    public static void RequestOptions_MetaAndProgressToken_WorkTogether()
    {
        // Arrange
        var options = new RequestOptions();

        // Act - set progress token first
        options.ProgressToken = new ProgressToken("token1");
        options.Meta!["custom"] = "value1";

        // Assert
        Assert.Equal("token1", options.ProgressToken?.ToString());
        Assert.Equal("value1", options.Meta["custom"]?.ToString());
        Assert.True(options.Meta.ContainsKey("progressToken"));
    }

    [Fact]
    public static void RequestOptions_ProgressToken_OverwritesPreviousValue()
    {
        // Arrange
        var options = new RequestOptions
        {
            ProgressToken = new ProgressToken("old-token")
        };

        // Act
        options.ProgressToken = new ProgressToken("new-token");

        // Assert
        Assert.Equal("new-token", options.ProgressToken?.ToString());
        Assert.Equal("new-token", options.Meta!["progressToken"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_ProgressToken_StringToLong_ChangesType()
    {
        // Arrange
        var options = new RequestOptions
        {
            ProgressToken = new ProgressToken("string-token")
        };

        // Act
        options.ProgressToken = new ProgressToken(999L);

        // Assert
        Assert.Equal("999", options.ProgressToken?.ToString());
        Assert.Equal(999L, options.Meta!["progressToken"]?.AsValue().GetValue<long>());
    }

    [Fact]
    public static void RequestOptions_Meta_MultipleProperties_Preserved()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject
            {
                ["prop1"] = "value1",
                ["prop2"] = 42,
                ["prop3"] = true
            }
        };

        // Act
        options.ProgressToken = new ProgressToken("my-token");

        // Assert
        Assert.Equal("value1", options.Meta["prop1"]?.ToString());
        Assert.Equal(42, options.Meta["prop2"]?.AsValue().GetValue<int>());
        Assert.True(options.Meta["prop3"]?.AsValue().GetValue<bool>());
        Assert.Equal("my-token", options.Meta["progressToken"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_MetaSetter_ReplacesExistingMeta()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject
            {
                ["old1"] = "value1",
                ["old2"] = "value2"
            }
        };

        var newMeta = new JsonObject
        {
            ["new1"] = "newValue1",
            ["new2"] = "newValue2"
        };

        // Act
        options.Meta = newMeta;

        // Assert
        Assert.False(options.Meta.ContainsKey("old1"));
        Assert.False(options.Meta.ContainsKey("old2"));
        Assert.Equal("newValue1", options.Meta["new1"]?.ToString());
        Assert.Equal("newValue2", options.Meta["new2"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_MetaSetter_NullWithNoProgressToken_ClearsMeta()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject { ["key"] = "value" }
        };

        // Act
        options.Meta = null;

        // Assert - getter creates new empty object
        Assert.NotNull(options.Meta);
        Assert.Empty(options.Meta);
    }

    [Fact]
    public static void RequestOptions_ProgressTokenSetter_CreatesMetaIfNeeded()
    {
        // Arrange
        var options = new RequestOptions();
        Assert.Null(options.ProgressToken);

        // Act
        options.ProgressToken = new ProgressToken("create-meta");

        // Assert
        Assert.NotNull(options.ProgressToken);
        Assert.Equal("create-meta", options.ProgressToken?.ToString());
        Assert.True(options.Meta!.ContainsKey("progressToken"));
    }

    [Fact]
    public static void RequestOptions_ComplexScenario_SetMetaThenProgressToken()
    {
        // Arrange
        var options = new RequestOptions
        {
            Meta = new JsonObject
            {
                ["custom1"] = "value1",
                ["custom2"] = 123
            }
        };

        // Act
        options.ProgressToken = new ProgressToken("scenario-token");

        // Assert
        Assert.Equal("value1", options.Meta["custom1"]?.ToString());
        Assert.Equal(123, options.Meta["custom2"]?.AsValue().GetValue<int>());
        Assert.Equal("scenario-token", options.ProgressToken?.ToString());
        Assert.Equal(3, options.Meta.Count);
    }

    [Fact]
    public static void RequestOptions_ComplexScenario_SetMetaWithProgressToken()
    {
        // Arrange
        var options = new RequestOptions();

        // Act
        var newMeta = new JsonObject
        {
            ["data1"] = "info1",
            ["data2"] = false,
            ["progressToken"] = 456L
        };
        options.Meta = newMeta;

        // Assert
        Assert.Equal("info1", options.Meta["data1"]?.ToString());
        Assert.False(options.Meta["data2"]?.AsValue().GetValue<bool>());
        Assert.Equal(456L, options.ProgressToken?.Token as long?);
        Assert.Equal(3, options.Meta.Count);
    }

    [Fact]
    public static void RequestOptions_ComplexScenario_SetMetaWithProgressUpdatesProgress()
    {
        // Arrange
        RequestOptions options = new();

        // Act
        options.ProgressToken = new ProgressToken("token1");
        JsonObject meta = new() { ["progressToken"] = "token2" };
        options.Meta = meta;

        // Assert
        Assert.Equal("token2", options.ProgressToken?.Token as string);
        Assert.Equal("token2", options.Meta["progressToken"]?.ToString());
    }

    [Fact]
    public static void RequestOptions_AllProperties_CanBeSetIndependently()
    {
        // Arrange
        var options = new RequestOptions();
        var customOptions = new JsonSerializerOptions { WriteIndented = true };

        // Act
        options.JsonSerializerOptions = customOptions;
        options.ProgressToken = new ProgressToken("independent");
        options.Meta!["field"] = "value";

        // Assert
        Assert.Same(customOptions, options.JsonSerializerOptions);
        Assert.Equal("independent", options.ProgressToken?.ToString());
        Assert.Equal("value", options.Meta["field"]?.ToString());
    }
}