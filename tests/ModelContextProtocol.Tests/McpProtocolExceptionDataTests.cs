using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for McpProtocolException.Data propagation to JSON-RPC error responses.
/// </summary>
public class McpProtocolExceptionDataTests : ClientServerTestBase
{
    public McpProtocolExceptionDataTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithCallToolHandler((request, cancellationToken) =>
        {
            var toolName = request.Params?.Name;
            
            switch (toolName)
            {
                case "throw_with_serializable_data":
                    throw new McpProtocolException("Resource not found", (McpErrorCode)(-32002))
                    {
                        Data =
                        {
                            { "uri", "file:///path/to/resource" },
                            { "code", 404 }
                        }
                    };

                case "throw_with_nonserializable_data":
                    throw new McpProtocolException("Resource not found", (McpErrorCode)(-32002))
                    {
                        Data =
                        {
                            // Circular reference - cannot be serialized
                            { "nonSerializable", new NonSerializableObject() },
                            // This one should still be included
                            { "uri", "file:///path/to/resource" }
                        }
                    };

                case "throw_with_only_nonserializable_data":
                    throw new McpProtocolException("Resource not found", (McpErrorCode)(-32002))
                    {
                        Data =
                        {
                            // Only non-serializable data - should result in null data
                            { "nonSerializable", new NonSerializableObject() }
                        }
                    };

                default:
                    throw new McpProtocolException($"Unknown tool: '{toolName}'", McpErrorCode.InvalidParams);
            }
        });
    }

    [Fact]
    public async Task Exception_With_Serializable_Data_Propagates_To_Client()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("throw_with_serializable_data", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Resource not found", exception.Message);
        Assert.Equal((McpErrorCode)(-32002), exception.ErrorCode);
    }

    [Fact]
    public async Task Exception_With_NonSerializable_Data_Still_Propagates_Error_To_Client()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // The tool throws McpProtocolException with non-serializable data in Exception.Data.
        // The server should still send a proper error response to the client, with non-serializable
        // values filtered out.
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("throw_with_nonserializable_data", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Resource not found", exception.Message);
        Assert.Equal((McpErrorCode)(-32002), exception.ErrorCode);
    }

    [Fact]
    public async Task Exception_With_Only_NonSerializable_Data_Still_Propagates_Error_To_Client()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // When all data is non-serializable, the error should still be sent (with null data)
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("throw_with_only_nonserializable_data", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Resource not found", exception.Message);
        Assert.Equal((McpErrorCode)(-32002), exception.ErrorCode);
    }

    /// <summary>
    /// A class that cannot be serialized by System.Text.Json due to circular reference.
    /// </summary>
    private sealed class NonSerializableObject
    {
        public NonSerializableObject() => Self = this;
        public NonSerializableObject Self { get; set; }
    }
}
