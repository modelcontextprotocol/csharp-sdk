using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

public class McpRequestDelegateFactoryTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper)
{
    [Fact]
    public async Task Create_UsesExplicitMcpServerOptions()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "DefaultServer", Version = "1.0.0" };
        }).WithHttpTransport();

        await using var app = Builder.Build();

        var requestDelegate = McpRequestDelegateFactory.Create(new McpServerOptions
        {
            ServerInfo = new() { Name = "ExplicitServer", Version = "1.0.0" },
        });

        app.MapPost("/custom", requestDelegate);
        app.MapGet("/custom", requestDelegate);
        app.MapDelete("/custom", requestDelegate);

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("http://localhost:5000/custom");
        Assert.Equal("ExplicitServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task Create_UsesExplicitConfigureSessionOptions_WhenMcpServerOptionsAreNull()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "DefaultServer", Version = "1.0.0" };
        }).WithHttpTransport();

        await using var app = Builder.Build();

        var requestDelegate = McpRequestDelegateFactory.Create(
            transportOptions: new HttpServerTransportOptions
            {
                ConfigureSessionOptions = static (context, options, cancellationToken) =>
                {
                    options.ServerInfo = new() { Name = "ConfiguredByCallback", Version = "1.0.0" };
                    return Task.CompletedTask;
                }
            });

        app.MapPost("/custom", requestDelegate);
        app.MapGet("/custom", requestDelegate);
        app.MapDelete("/custom", requestDelegate);

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("http://localhost:5000/custom");
        Assert.Equal("ConfiguredByCallback", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public void Create_ThrowsArgumentException_WhenServerOptionsAndConfigureSessionOptionsAreProvided()
    {
        var exception = Assert.Throws<ArgumentException>(() => McpRequestDelegateFactory.Create(
            new McpServerOptions(),
            new HttpServerTransportOptions
            {
                ConfigureSessionOptions = static (context, options, cancellationToken) => Task.CompletedTask,
            }));

        Assert.Equal("transportOptions", exception.ParamName);
    }

    private async Task<McpClient> ConnectAsync(string endpoint)
    {
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }
}
