using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net.Http.Headers;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Verifies that aborting an HTTP request flows cancellation into the running request handler's
/// <see cref="CancellationToken"/>.
/// <para>
/// Starting with the 2026-07-28 protocol revision (SEP-2575 + SEP-2567) the HTTP request lifetime <em>is</em> the
/// request lifetime: there are no sessions, so a dropped connection is equivalent to cancelling the
/// in-flight request. The same holds for legacy stateless mode, where each request is independent and
/// outlived by nothing. These tests pin that behavior so a tool's <see cref="CancellationToken"/> fires
/// promptly when the client goes away.
/// </para>
/// </summary>
public class RequestAbortCancellationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{

    private WebApplication? _app;

    private readonly TaskCompletionSource _toolStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _toolCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _requestAborted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task StartAsync(bool stateless)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(RequestAbortCancellationTests), Version = "1" };
        })
        .WithHttpTransport(options =>
        {
            options.Stateless = stateless;
        })
        .WithTools([McpServerTool.Create(
            async (CancellationToken cancellationToken) =>
            {
                _toolStarted.TrySetResult();
                try
                {
                    // Block until the request handler's CancellationToken fires. If cancellation never
                    // flows from the aborted HTTP request, this hangs and the test times out.
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _toolCanceled.TrySetResult();
                    throw;
                }

                return "unreachable";
            },
            new() { Name = "blockingTool" })]);

        _app = Builder.Build();

        // Record when the server observes the client abort so we can assert the abort (not some unrelated
        // cancellation path) is what tears down the in-flight tool.
        _app.Use(async (context, next) =>
        {
            context.RequestAborted.Register(() => _requestAborted.TrySetResult());
            await next();
        });

        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    [Fact]
    public async Task July2026Request_AbortFlowsCancellationToToolHandler()
    {
        // Starting with the 2026-07-28 protocol revision, Streamable HTTP no longer supports sessions (SEP-2567) and is
        // served natively only on a stateless server; a Stateless=false server refuses these requests so dual-era
        // clients fall back to initialize.
        await StartAsync(stateless: true);

        using var request = CreateBlockingToolRequest(july2026Protocol: true);

        await AssertAbortCancelsToolAsync(request);
    }

    [Fact]
    public async Task StatelessRequest_AbortFlowsCancellationToToolHandler()
    {
        await StartAsync(stateless: true);

        using var request = CreateBlockingToolRequest(july2026Protocol: false);

        await AssertAbortCancelsToolAsync(request);
    }

    private static HttpRequestMessage CreateBlockingToolRequest(bool july2026Protocol)
    {
        // A 2026-07-28 tools/call requires the SEP-2243 Mcp-Method/Mcp-Name headers and the per-request _meta
        // (protocol version, client info, capabilities) that replaces the initialize handshake (SEP-2567).
        var body = july2026Protocol
            ? """
              {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"blockingTool","_meta":{"io.modelcontextprotocol/protocolVersion":"PROTOCOL_VERSION","io.modelcontextprotocol/clientInfo":{"name":"raw","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}
              """.Replace("PROTOCOL_VERSION", McpProtocolVersions.July2026ProtocolVersion)
            : """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"blockingTool"}}""";

        var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (july2026Protocol)
        {
            request.Headers.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
            request.Headers.Add("Mcp-Method", "tools/call");
            request.Headers.Add("Mcp-Name", "blockingTool");
        }

        return request;
    }

    private async Task AssertAbortCancelsToolAsync(HttpRequestMessage request)
    {
        using var requestCts = new CancellationTokenSource();

        // Send the request without awaiting completion. The blockingTool will not return until its
        // CancellationToken fires, so this Task only completes once we abort the request below.
        // ResponseContentRead (the default) keeps SendAsync pending on the response body, so cancelling
        // requestCts actually aborts the in-flight connection. (With ResponseHeadersRead, SendAsync would
        // return as soon as the server flushed the SSE response headers and the cancel would be a no-op.)
        var sendTask = HttpClient.SendAsync(request, requestCts.Token);

        // Wait for the server to actually start running the tool before aborting.
        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Abort the in-flight HTTP request, simulating the client disconnecting.
        requestCts.Cancel();

        // The server must observe the abort...
        await _requestAborted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // ...and that abort must cancel the running tool's CancellationToken.
        await _toolCanceled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // The HttpClient call itself should observe the cancellation we requested.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
    }
}
