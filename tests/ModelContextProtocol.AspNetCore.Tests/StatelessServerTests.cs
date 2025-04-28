using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol.Types;
using System.Net;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StatelessServerTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(mcpServerOptions =>
        {
            mcpServerOptions.ServerInfo = new Implementation
            {
                Name = nameof(StreamableHttpServerConformanceTests),
                Version = "73",
            };
        }).WithHttpTransport(httpServerTransportOptions =>
        {
            httpServerTransportOptions.Stateless = true;
        });

        _app = Builder.Build();

        _app.MapMcp();

        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
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
    public async Task EnablingStatelessMode_Disables_SseEndpoints()
    {
        await StartAsync();

        using var sseResponse = await HttpClient.GetAsync("/sse", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, sseResponse.StatusCode);

        using var messageResponse = await HttpClient.PostAsync("/message", new StringContent(""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, messageResponse.StatusCode);
    }

    [Fact]
    public async Task EnablingStatelessMode_Disables_GetAndDeleteEndpoints()
    {
        await StartAsync();

        using var getResponse = await HttpClient.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);

        using var deleteResponse = await HttpClient.DeleteAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteResponse.StatusCode);
    }
}
