using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for McpProtocolException.Data propagation to JSON-RPC error responses.
/// </summary>
/// <remarks>
/// Primitive values (strings, numbers, bools) are extracted from JsonElements and stored directly,
/// which works on all platforms including .NET Framework. Complex objects and arrays are stored as
/// JsonElement on .NET Core, but skipped on .NET Framework (where JsonElement is not serializable).
/// </remarks>
public class McpProtocolExceptionDataTests : ClientServerTestBase
{
    public static bool IsNotNetFramework => !PlatformDetection.IsNetFramework;
    
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
                    throw new McpProtocolException("Resource not found", McpErrorCode.ResourceNotFound)
                    {
                        Data =
                        {
                            { "uri", "file:///path/to/resource" },
                            { "code", 404 }
                        }
                    };

                case "throw_with_nonserializable_data":
                    throw new McpProtocolException("Resource not found", McpErrorCode.ResourceNotFound)
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
                    throw new McpProtocolException("Resource not found", McpErrorCode.ResourceNotFound)
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
        Assert.Equal(McpErrorCode.ResourceNotFound, exception.ErrorCode);

        // Verify the data was propagated to the exception
        // The Data collection should contain the expected keys
        var hasUri = false;
        var hasCode = false;
        foreach (System.Collections.DictionaryEntry entry in exception.Data)
        {
            if (entry.Key is string key)
            {
                if (key == "uri") hasUri = true;
                if (key == "code") hasCode = true;
            }
        }
        Assert.True(hasUri, "Exception.Data should contain 'uri' key");
        Assert.True(hasCode, "Exception.Data should contain 'code' key");
        
        // Verify the values - primitives are extracted as their native types (string, double, bool)
        Assert.Equal("file:///path/to/resource", exception.Data["uri"]);
        Assert.Equal(404.0, exception.Data["code"]); // Numbers are stored as double
    }

    [Fact(Skip = "Non-serializable test data not supported on .NET Framework", SkipUnless = nameof(IsNotNetFramework))]
    public async Task Exception_With_NonSerializable_Data_Still_Propagates_Error_To_Client()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // The tool throws McpProtocolException with non-serializable data in Exception.Data.
        // The server should still send a proper error response to the client, with non-serializable
        // values filtered out.
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("throw_with_nonserializable_data", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Resource not found", exception.Message);
        Assert.Equal(McpErrorCode.ResourceNotFound, exception.ErrorCode);

        // Verify that only the serializable data was propagated (non-serializable was filtered out)
        var hasUri = false;
        var hasNonSerializable = false;
        foreach (System.Collections.DictionaryEntry entry in exception.Data)
        {
            if (entry.Key is string key)
            {
                if (key == "uri") hasUri = true;
                if (key == "nonSerializable") hasNonSerializable = true;
            }
        }
        Assert.True(hasUri, "Exception.Data should contain 'uri' key");
        Assert.False(hasNonSerializable, "Exception.Data should not contain 'nonSerializable' key");
        
        Assert.Equal("file:///path/to/resource", exception.Data["uri"]);
    }

    [Fact(Skip = "Non-serializable test data not supported on .NET Framework", SkipUnless = nameof(IsNotNetFramework))]
    public async Task Exception_With_Only_NonSerializable_Data_Still_Propagates_Error_To_Client()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // When all data is non-serializable, the error should still be sent (with null data)
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("throw_with_only_nonserializable_data", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Resource not found", exception.Message);
        Assert.Equal(McpErrorCode.ResourceNotFound, exception.ErrorCode);
        
        // When all data is non-serializable, the Data collection should be empty
        // (the server's ConvertExceptionData returns null when no serializable data exists)
        Assert.Empty(exception.Data);
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
