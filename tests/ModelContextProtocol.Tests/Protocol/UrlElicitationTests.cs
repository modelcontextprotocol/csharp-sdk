using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Configuration;

public partial class UrlElicitationTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithCallToolHandler(async (request, cancellationToken) =>
        {
            Assert.NotNull(request.Params);

            if (request.Params.Name == "TestUrlElicitation")
            {
                var result = await request.Server.ElicitAsync(new()
                    {
                        Mode = "url",
                        ElicitationId = "test-elicitation-id",
                        Url = $"https://auth.example.com/oauth/authorize?state=test-elicitation-id",
                        Message = "Please authorize access to your account by logging in through your browser.",
                    },
                    cancellationToken);

                // For URL mode, we expect the client to accept (consent to navigate)
                // The actual OAuth flow happens out-of-band
                Assert.Equal("accept", result.Action);

                await request.Server.SendNotificationAsync(
                    NotificationMethods.ElicitationCompleteNotification,
                    new ElicitationCompleteNotificationParams
                    {
                        ElicitationId = "test-elicitation-id",
                    },
                    McpJsonUtilities.DefaultOptions,
                    cancellationToken);

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "authorization-pending:test-elicitation-id" }],
                };
            }
            else if (request.Params.Name == "TestUrlElicitationDecline")
            {
                var elicitationId = Guid.NewGuid().ToString();
                var result = await request.Server.ElicitAsync(new()
                    {
                        Mode = "url",
                        ElicitationId = "elicitation-declined-id",
                        Url = $"https://payment.example.com/pay?transaction=elicitation-declined-id",
                        Message = "Please complete payment in your browser.",
                    },
                    cancellationToken);

                // User declined
                Assert.Equal("decline", result.Action);

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "payment-declined" }],
                };
            }
            else if (request.Params.Name == "TestUrlElicitationCancel")
            {
                var elicitationId = Guid.NewGuid().ToString();
                var result = await request.Server.ElicitAsync(new()
                    {
                        Mode = "url",
                        ElicitationId = "elicitation-canceled-id",
                        Url = $"https://verify.example.com/verify?id=elicitation-canceled-id",
                        Message = "Please verify your identity.",
                    },
                    cancellationToken);

                // User canceled (dismissed without explicit choice)
                Assert.Equal("cancel", result.Action);

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "verification-canceled" }],
                };
            }
            else if (request.Params.Name == "TestUrlElicitationRequired")
            {
                throw new UrlElicitationRequiredException(
                    "Authorization is required to continue.",
                    new[]
                    {
                        new ElicitRequestParams
                        {
                            Mode = "url",
                            ElicitationId = "elicitation-required-id",
                            Url = "https://auth.example.com/connect?elicitationId=elicitation-required-id",
                            Message = "Authorization is required to access Example Co.",
                        }
                    });
            }
            else if (request.Params.Name == "TestUrlElicitationMissingId")
            {
                try
                {
                    await request.Server.ElicitAsync(new()
                        {
                            Mode = "url",
                            Url = "https://missing-id.example.com/oauth",
                            Message = "URL elicitation without ID should fail.",
                        },
                        cancellationToken);
                }
                catch (ArgumentException ex)
                {
                    throw new McpException(ex.Message);
                }

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "missing-id-succeeded" }],
                };
            }
            else if (request.Params.Name == "ProbeUrlCapability")
            {
                try
                {
                    await request.Server.ElicitAsync(new()
                        {
                            Mode = "url",
                            ElicitationId = Guid.NewGuid().ToString(),
                            Url = "https://probe.example.com/oauth",
                            Message = "Capability probe for url mode.",
                        },
                        cancellationToken);

                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "url-allowed" }],
                    };
                }
                catch (InvalidOperationException ex)
                {
                    throw new McpException(ex.Message);
                }
            }
            else if (request.Params.Name == "ProbeFormCapability")
            {
                try
                {
                    var elicitationResult = await request.Server.ElicitAsync(new()
                        {
                            Message = "Capability probe for form mode.",
                            RequestedSchema = new(),
                        },
                        cancellationToken: cancellationToken);

                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = $"form-allowed:{elicitationResult.Action}" }],
                    };
                }
                catch (InvalidOperationException ex)
                {
                    throw new McpException(ex.Message);
                }
            }
            else if (request.Params.Name == "TestFormElicitationMissingSchema")
            {
                try
                {
                    await request.Server.ElicitAsync(new()
                        {
                            Message = "Form elicitation without schema should fail.",
                        },
                        cancellationToken);
                }
                catch (ArgumentException ex)
                {
                    throw new McpException(ex.Message);
                }

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "missing-schema-succeeded" }],
                };
            }

            Assert.Fail($"Unexpected tool name: {request.Params.Name}");
            return new CallToolResult { Content = [] };
        });
    }

    [Fact]
    public async Task Can_Elicit_OutOfBand_With_Url()
    {
        string? capturedElicitationId = null;
        string? capturedUrl = null;
        string? capturedMessage = null;
        var completionNotification = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                // Explicitly declare support for both modes
                Elicitation = new ElicitationCapability
                {
                    Form = new FormElicitationCapability(),
                    Url = new UrlElicitationCapability()
                }
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = async (request, cancellationToken) =>
                {
                    Assert.NotNull(request);

                    // Verify this is an URL mode elicitation request
                    Assert.NotNull(request.Mode);
                    Assert.Equal("url", request.Mode);

                    // Capture the request details
                    capturedElicitationId = request.ElicitationId;
                    capturedUrl = request.Url;
                    capturedMessage = request.Message;

                    // Verify URL-specific fields
                    Assert.NotNull(request.ElicitationId);
                    Assert.NotNull(request.Url);
                    Assert.StartsWith("https://auth.example.com/oauth/authorize", request.Url);
                    Assert.Contains("state=", request.Url);
                    Assert.Equal("Please authorize access to your account by logging in through your browser.", request.Message);

                    // Verify that RequestedSchema is null for URL mode
                    Assert.Null(request.RequestedSchema);

                    // Simulate user consent to navigate to the URL
                    // In a real implementation, the client would:
                    // 1. Display a consent dialog showing the URL and message
                    // 2. Open the URL in the system browser
                    // 3. Return "accept" to indicate user consented

                    // Return accept (user consented to navigate)
                    return new ElicitResult
                    {
                        Action = "accept",
                        // Note: No Content for URL mode - the actual interaction happens out-of-band
                    };
                }
            }
        });

        await using var completionHandler = client.RegisterNotificationHandler(
            NotificationMethods.ElicitationCompleteNotification,
            async (notification, cancellationToken) =>
            {
                var payload = notification.Params?.Deserialize<ElicitationCompleteNotificationParams>(McpJsonUtilities.DefaultOptions);
                if (payload is not null)
                {
                    completionNotification.TrySetResult(payload.ElicitationId);
                }

                await Task.CompletedTask;
            });

        var result = await client.CallToolAsync("TestUrlElicitation", cancellationToken: TestContext.Current.CancellationToken);

        // Verify the tool completed successfully
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.StartsWith("authorization-pending:", textContent.Text);

        // Verify we captured all the expected URL mode elicitation data
        Assert.NotNull(capturedElicitationId);
        Assert.NotNull(capturedUrl);
        Assert.NotNull(capturedMessage);
        Assert.Contains(capturedElicitationId, capturedUrl);

        var notifiedElicitationId = await completionNotification.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(capturedElicitationId, notifiedElicitationId);
    }

    [Fact]
    public async Task UrlElicitation_User_Can_Decline()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability()
                }
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = async (request, cancellationToken) =>
                {
                    Assert.NotNull(request);
                    Assert.NotNull(request.Mode);
                    Assert.Equal("url", request.Mode);
                    Assert.Contains("payment.example.com", request.Url);

                    // Simulate user declining to navigate to the URL
                    // This might happen if user doesn't trust the URL or doesn't want to proceed
                    return new ElicitResult
                    {
                        Action = "decline"
                    };
                }
            }
        });

        var result = await client.CallToolAsync("TestUrlElicitationDecline", cancellationToken: TestContext.Current.CancellationToken);

        // Server should handle decline gracefully
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("payment-declined", textContent.Text);
    }

    [Fact]
    public async Task UrlElicitation_User_Can_Cancel()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability()
                }
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = async (request, cancellationToken) =>
                {
                    Assert.NotNull(request);
                    Assert.NotNull(request.Mode);
                    Assert.Equal("url", request.Mode);
                    Assert.Contains("verify.example.com", request.Url);

                    // Simulate user canceling (dismissing dialog without explicit choice)
                    return new ElicitResult
                    {
                        Action = "cancel"
                    };
                }
            }
        });

        var result = await client.CallToolAsync("TestUrlElicitationCancel", cancellationToken: TestContext.Current.CancellationToken);

        // Server should handle cancellation
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("verification-canceled", textContent.Text);
    }

    [Fact]
    public async Task UrlElicitation_Defaults_To_Unsupported_When_Handler_Provided()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = (request, cancellationToken) =>
                {
                    throw new InvalidOperationException("URL handler should not be invoked in default mode.");
                },
            }
        });

        var defaultCapability = AssertServerElicitationCapability();
        Assert.NotNull(defaultCapability.Form);
        Assert.Null(defaultCapability.Url);

        var result = await client.CallToolAsync("ProbeUrlCapability", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("An error occurred invoking 'ProbeUrlCapability': Client does not support URL mode elicitation requests.", textContent.Text);
    }

    [Fact]
    public async Task FormElicitation_Defaults_To_Supported_When_Handler_Provided()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = (_, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "decline" }),
            }
        });

        var capability = AssertServerElicitationCapability();
        Assert.NotNull(capability.Form);
        Assert.Null(capability.Url);

        var result = await client.CallToolAsync("ProbeFormCapability", cancellationToken: TestContext.Current.CancellationToken);

        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("form-allowed:decline", textContent.Text);
    }

    [Fact]
    public async Task UrlElicitation_BlankCapability_Allows_Only_Form()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability(),
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = (_, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "decline" }),
            }
        });

        var capability = AssertServerElicitationCapability();
        Assert.NotNull(capability.Form);
        Assert.Null(capability.Url);

        var urlResult = await client.CallToolAsync("ProbeUrlCapability", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(urlResult.IsError);
        var urlTextContent = Assert.IsType<TextContentBlock>(urlResult.Content[0]);
        Assert.Equal("An error occurred invoking 'ProbeUrlCapability': Client does not support URL mode elicitation requests.", urlTextContent.Text);

        var formResult = await client.CallToolAsync("ProbeFormCapability", cancellationToken: TestContext.Current.CancellationToken);
        var textContent = Assert.IsType<TextContentBlock>(formResult.Content[0]);
        Assert.Equal("form-allowed:decline", textContent.Text);
    }

    [Fact]
    public async Task FormElicitation_UrlOnlyCapability_NotSupported()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability(),
                }
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = (request, cancellationToken) =>
                {
                    Assert.NotNull(request);
                    Assert.Equal("url", request.Mode);
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "decline" });
                },
            }
        });

        var capability = AssertServerElicitationCapability();
        Assert.Null(capability.Form);
        Assert.NotNull(capability.Url);

        var urlResult = await client.CallToolAsync("ProbeUrlCapability", cancellationToken: TestContext.Current.CancellationToken);
        var urlText = Assert.IsType<TextContentBlock>(urlResult.Content[0]);
        Assert.Equal("url-allowed", urlText.Text);

        var formResult = await client.CallToolAsync("ProbeFormCapability", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(formResult.IsError);
        var formText = Assert.IsType<TextContentBlock>(formResult.Content[0]);
        Assert.Equal("An error occurred invoking 'ProbeFormCapability': Client does not support form mode elicitation requests.", formText.Text);
    }

    [Fact]
    public async Task UrlElicitation_Requires_ElicitationId_For_Url_Mode()
    {
        var elicitationHandlerCalled = false;

        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability(),
                }
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = (request, cancellationToken) =>
                {
                    elicitationHandlerCalled = true;
                    return new ValueTask<ElicitResult>(new ElicitResult());
                },
            }
        });

        var result = await client.CallToolAsync("TestUrlElicitationMissingId", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("An error occurred invoking 'TestUrlElicitationMissingId': URL mode elicitation requests require an elicitation ID.", textContent.Text);
        Assert.False(elicitationHandlerCalled);
    }

    [Fact]
    public async Task UrlElicitationRequired_Exception_Propagates_To_Client()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability(),
                }
            }
        });

        var exception = await Assert.ThrowsAsync<UrlElicitationRequiredException>(
            async () => await client.CallToolAsync("TestUrlElicitationRequired", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.UrlElicitationRequired, exception.ErrorCode);

        var elicitation = Assert.Single(exception.Elicitations);
        Assert.Equal("url", elicitation.Mode);
        Assert.Equal("elicitation-required-id", elicitation.ElicitationId);
        Assert.Equal("https://auth.example.com/connect?elicitationId=elicitation-required-id", elicitation.Url);
        Assert.Equal("Authorization is required to access Example Co.", elicitation.Message);
    }

    [Fact]
    public async Task FormElicitation_Requires_RequestedSchema()
    {
        var elicitationHandlerCalled = false;

        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new(),
            },
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = (request, cancellationToken) =>
                {
                    elicitationHandlerCalled = true;
                    return new ValueTask<ElicitResult>(new ElicitResult());
                },
            }
        });

        var result = await client.CallToolAsync("TestFormElicitationMissingSchema", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("An error occurred invoking 'TestFormElicitationMissingSchema': Form mode elicitation requests require a requested schema.", textContent.Text);
        Assert.False(elicitationHandlerCalled);
    }

    private ElicitationCapability AssertServerElicitationCapability()
    {
        var capabilities = Server.ClientCapabilities;
        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities.Elicitation);
        return capabilities.Elicitation;
    }

    private sealed class TestForm
    {
        public required string Value { get; set; }
    }
}
