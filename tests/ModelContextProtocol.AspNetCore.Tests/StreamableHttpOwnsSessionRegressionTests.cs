using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Regression tests for StreamableHttp client disposal behavior with OwnsSession option.
/// </summary>
/// <remarks>
/// This test reproduces a bug where McpClient.DisposeAsync() hangs indefinitely when using
/// Streamable HTTP transport with HttpClientTransportOptions.OwnsSession = false.
/// The hang occurs because the background GET receive task (ReceiveUnsolicitedMessagesAsync)
/// is not properly canceled when the client is disposed.
/// </remarks>
public class StreamableHttpOwnsSessionRegressionTests(ITestOutputHelper testOutputHelper)
    : KestrelInMemoryTest(testOutputHelper)
{
    /// <summary>
    /// Regression test: McpClient.DisposeAsync should complete quickly when OwnsSession = false.
    /// </summary>
    /// <remarks>
    /// This test reproduces the reported issue where DisposeAsync hangs indefinitely.
    /// The test sets up a Streamable HTTP server with a long-lived GET SSE stream,
    /// creates a client with OwnsSession = false, performs an initial call that triggers
    /// the background ReceiveUnsolicitedMessagesAsync task, then attempts to dispose the client.
    /// 
    /// Expected behavior: DisposeAsync should complete within a short timeout (2-5 seconds).
    /// Bug behavior: DisposeAsync hangs because the background GET task is not canceled.
    /// </remarks>
    [Fact]
    public async Task DisposeAsync_CompletesQuickly_WhenOwnsSessionIsFalse()
    {
        // Arrange: Set up a Streamable HTTP server
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "StreamableHttpTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport().WithTools<SimpleTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Create client with OwnsSession = false
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false, // This is the key setting that triggers the bug
        };

        await using var transport = new HttpClientTransport(transportOptions, HttpClient, LoggerFactory);
        var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Perform an initial call to trigger initialization and start the background GET receive task
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tools);

        // Give more time to ensure the background GET task has started and is actively reading from the SSE stream
        // The GET request should now be waiting for SSE events from the server
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Act: Attempt to dispose the client
        var disposeTask = client.DisposeAsync().AsTask();

        // Assert: Dispose should complete within a reasonable timeout
        // Using 5 seconds as specified in the problem statement
        var timeout = TimeSpan.FromSeconds(5);
        var completedTask = await Task.WhenAny(disposeTask, Task.Delay(timeout, TestContext.Current.CancellationToken));

        // If the bug is present, disposeTask will not complete and this assertion will fail
        Assert.True(completedTask == disposeTask,
            $"McpClient.DisposeAsync() did not complete within {timeout.TotalSeconds} seconds. " +
            "This indicates the background GET receive task is not being properly canceled when OwnsSession = false.");

        // Verify the dispose actually succeeded without throwing
        await disposeTask;
    }

    /// <summary>
    /// Control test: DisposeAsync should complete quickly when OwnsSession = true (default).
    /// </summary>
    /// <remarks>
    /// This test verifies that the issue is specific to OwnsSession = false.
    /// With OwnsSession = true (the default), disposal should work correctly.
    /// </remarks>
    [Fact]
    public async Task DisposeAsync_CompletesQuickly_WhenOwnsSessionIsTrue()
    {
        // Arrange: Set up a Streamable HTTP server
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "StreamableHttpTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport().WithTools<SimpleTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Create client with OwnsSession = true (default)
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = true, // This is the default value
        };

        await using var transport = new HttpClientTransport(transportOptions, HttpClient, LoggerFactory);
        var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Perform an initial call to trigger initialization and start the background GET receive task
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tools);

        // Small delay to ensure the background GET task has started
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Act: Attempt to dispose the client
        var disposeTask = client.DisposeAsync().AsTask();

        // Assert: Dispose should complete within a reasonable timeout
        var timeout = TimeSpan.FromSeconds(5);
        var completedTask = await Task.WhenAny(disposeTask, Task.Delay(timeout, TestContext.Current.CancellationToken));

        // This should pass even before the fix, showing the issue is specific to OwnsSession = false
        Assert.True(completedTask == disposeTask,
            $"McpClient.DisposeAsync() did not complete within {timeout.TotalSeconds} seconds with OwnsSession = true.");

        // Verify the dispose actually succeeded without throwing
        await disposeTask;
    }

    /// <summary>
    /// Additional regression test: Multiple disposal attempts should not hang.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_IsIdempotent_WhenOwnsSessionIsFalse()
    {
        // Arrange
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "StreamableHttpTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport().WithTools<SimpleTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false,
        };

        await using var transport = new HttpClientTransport(transportOptions, HttpClient, LoggerFactory);
        var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Act: Call DisposeAsync multiple times
        await client.DisposeAsync();
        await client.DisposeAsync(); // Second call should be a no-op

        // Assert: Should not throw or hang
        Assert.True(true, "Multiple DisposeAsync calls completed successfully");
    }

    /// <summary>
    /// Simple tools for testing.
    /// </summary>
    [McpServerToolType]
    private class SimpleTools
    {
        [McpServerTool, Description("A simple echo tool")]
        public static string Echo(string message) => $"Echo: {message}";

        [McpServerTool, Description("Returns the current time")]
        public static string GetTime() => DateTime.UtcNow.ToString("o");
    }
}
