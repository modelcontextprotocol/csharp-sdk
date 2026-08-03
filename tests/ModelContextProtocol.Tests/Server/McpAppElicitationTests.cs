#pragma warning disable MCPEXP003

using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using System.Text.Json;
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
    public void SetAppUi_RoundTripsThroughMrtrInputRequest()
    {
        var request = McpAppElicitation.SetAppUi(
            CreateRequest(),
            "ui://example/choose-option.html");

        var embedded = InputRequest.ForElicitation(request).ElicitationParams;

        Assert.NotNull(embedded);
        Assert.Equal(
            "ui://example/choose-option.html",
            McpAppElicitation.GetAppUi(embedded)?.ResourceUri);
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

    [Fact]
    public void ValidateResult_AcceptedValidContent_ReturnsNormalizedResult()
    {
        var request = new ElicitRequestParams
        {
            Message = "Enter delivery details",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["email"] = new ElicitRequestParams.StringSchema
                    {
                        Format = "email",
                        MinLength = 6,
                        MaxLength = 100,
                    },
                    ["quantity"] = new ElicitRequestParams.NumberSchema
                    {
                        Type = "integer",
                        Minimum = 1,
                        Maximum = 10,
                    },
                    ["expedited"] = new ElicitRequestParams.BooleanSchema(),
                    ["window"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                    {
                        OneOf =
                        [
                            new() { Const = "morning", Title = "Morning" },
                            new() { Const = "afternoon", Title = "Afternoon" },
                        ],
                    },
                    ["days"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                    {
                        MinItems = 1,
                        MaxItems = 2,
                        Items = new()
                        {
                            Enum = ["monday", "tuesday", "wednesday"],
                        },
                    },
                },
                Required = ["email", "quantity", "expedited", "window", "days"],
            },
        };
        var result = new ElicitResult
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement>
            {
                ["email"] = Json("\"person@example.com\""),
                ["quantity"] = Json("2"),
                ["expedited"] = Json("true"),
                ["window"] = Json("\"morning\""),
                ["days"] = Json("""["monday", "tuesday"]"""),
            },
        };

        var validation = McpAppElicitation.ValidateResult(request, result);

        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.NotNull(validation.ValidatedResult);
        Assert.Equal(2, validation.ValidatedResult.Content!["quantity"].GetInt32());
    }

    [Fact]
    public void ValidateResult_MissingRequiredProperty_ReturnsError()
    {
        var validation = McpAppElicitation.ValidateResult(
            CreateRequest(),
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>(),
            });

        var error = Assert.Single(validation.Errors);
        Assert.False(validation.IsValid);
        Assert.Null(validation.ValidatedResult);
        Assert.Equal("/content/choice", error.Path);
        Assert.Equal("Required property is missing.", error.Message);
    }

    [Fact]
    public void ValidateResult_UnexpectedProperty_ReturnsError()
    {
        var validation = McpAppElicitation.ValidateResult(
            CreateRequest(),
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["choice"] = Json("\"morning\""),
                    ["untrusted"] = Json("\"sensitive-value\""),
                },
            });

        var error = Assert.Single(validation.Errors);
        Assert.Equal("/content/untrusted", error.Path);
        Assert.DoesNotContain("sensitive-value", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"two\"")]
    [InlineData("2.5")]
    [InlineData("null")]
    public void ValidateResult_InvalidIntegerValue_ReturnsError(string json)
    {
        var request = new ElicitRequestParams
        {
            Message = "Enter a quantity",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["quantity"] = new ElicitRequestParams.NumberSchema { Type = "integer" },
                },
                Required = ["quantity"],
            },
        };

        var validation = McpAppElicitation.ValidateResult(
            request,
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["quantity"] = Json(json),
                },
            });

        var error = Assert.Single(validation.Errors);
        Assert.Equal("/content/quantity", error.Path);
        Assert.Equal("Expected an integer.", error.Message);
    }

    [Fact]
    public void ValidateResult_InvalidEnumChoices_ReturnErrorsWithoutValues()
    {
        var request = new ElicitRequestParams
        {
            Message = "Choose options",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["window"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                    {
                        OneOf = [new() { Const = "morning", Title = "Morning" }],
                    },
                    ["days"] = new ElicitRequestParams.TitledMultiSelectEnumSchema
                    {
                        Items = new()
                        {
                            AnyOf = [new() { Const = "monday", Title = "Monday" }],
                        },
                    },
                },
            },
        };

        var validation = McpAppElicitation.ValidateResult(
            request,
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["window"] = Json("\"secret-window\""),
                    ["days"] = Json("""["secret-day"]"""),
                },
            });

        Assert.False(validation.IsValid);
        Assert.Collection(
            validation.Errors,
            error =>
            {
                Assert.Equal("/content/window", error.Path);
                Assert.DoesNotContain("secret-window", error.Message, StringComparison.Ordinal);
            },
            error =>
            {
                Assert.Equal("/content/days/0", error.Path);
                Assert.DoesNotContain("secret-day", error.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ValidateResult_LengthNumericAndSelectionBounds_ReturnErrors()
    {
        var request = new ElicitRequestParams
        {
            Message = "Enter bounded values",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["name"] = new ElicitRequestParams.StringSchema { MinLength = 2, MaxLength = 4 },
                    ["score"] = new ElicitRequestParams.NumberSchema { Minimum = 1, Maximum = 5 },
                    ["choices"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                    {
                        MinItems = 2,
                        MaxItems = 3,
                        Items = new() { Enum = ["a", "b", "c"] },
                    },
                },
            },
        };

        var validation = McpAppElicitation.ValidateResult(
            request,
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["name"] = Json("\"x\""),
                    ["score"] = Json("6"),
                    ["choices"] = Json("""["a"]"""),
                },
            });

        Assert.False(validation.IsValid);
        Assert.Equal(
            ["/content/name", "/content/score", "/content/choices"],
            validation.Errors.Select(error => error.Path));
    }

    [Theory]
    [InlineData("", "email")]
    [InlineData("not-an-email", "email")]
    [InlineData("relative/path", "uri")]
    [InlineData("2026-02-30", "date")]
    [InlineData("2026-08-03 11:22:30Z", "date-time")]
    public void ValidateResult_InvalidStringFormat_ReturnsError(string value, string format)
    {
        var request = new ElicitRequestParams
        {
            Message = "Enter a formatted value",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["value"] = new ElicitRequestParams.StringSchema { Format = format },
                },
            },
        };

        var validation = McpAppElicitation.ValidateResult(
            request,
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement(value, McpApps.SerializerOptions),
                },
            });

        var error = Assert.Single(validation.Errors);
        Assert.Equal("/content/value", error.Path);
        if (value.Length > 0)
        {
            Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("person@example.com", "email")]
    [InlineData("https://example.com/path", "uri")]
    [InlineData("2026-08-03", "date")]
    [InlineData("2026-08-03T11:22:30Z", "date-time")]
    [InlineData("2026-08-03T11:22:30.462-07:00", "date-time")]
    public void ValidateResult_ValidStringFormat_IsAccepted(string value, string format)
    {
        var request = new ElicitRequestParams
        {
            Message = "Enter a formatted value",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["value"] = new ElicitRequestParams.StringSchema { Format = format },
                },
            },
        };

        var validation = McpAppElicitation.ValidateResult(
            request,
            new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement(value, McpApps.SerializerOptions),
                },
            });

        Assert.True(validation.IsValid);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public void ValidateResult_DeclineAndCancelWithoutContent_AreValid(string action)
    {
        var result = new ElicitResult { Action = action };

        var validation = McpAppElicitation.ValidateResult(CreateRequest(), result);

        Assert.True(validation.IsValid);
        Assert.Same(result, validation.ValidatedResult);
        Assert.Empty(validation.Errors);
    }

    [Fact]
    public void ValidateResult_AcceptedResult_AppliesDefaultsBeforeRequiredValidation()
    {
        var request = new ElicitRequestParams
        {
            Message = "Confirm defaults",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["window"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                    {
                        Enum = ["morning", "afternoon"],
                        Default = "morning",
                    },
                    ["expedited"] = new ElicitRequestParams.BooleanSchema { Default = false },
                },
                Required = ["window"],
            },
        };

        var validation = McpAppElicitation.ValidateResult(
            request,
            new ElicitResult { Action = "accept" });

        Assert.True(validation.IsValid);
        Assert.Equal("morning", validation.ValidatedResult!.Content!["window"].GetString());
        Assert.False(validation.ValidatedResult.Content["expedited"].GetBoolean());
    }

    [Fact]
    public void ValidateResult_AcceptedResultWithoutContentOrDefaults_ReturnsError()
    {
        var validation = McpAppElicitation.ValidateResult(
            new ElicitRequestParams
            {
                Message = "Optional form",
                RequestedSchema = new ElicitRequestParams.RequestSchema(),
            },
            new ElicitResult { Action = "accept" });

        var error = Assert.Single(validation.Errors);
        Assert.Equal("/content", error.Path);
        Assert.Equal("Accepted elicitation results must include content.", error.Message);
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

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
