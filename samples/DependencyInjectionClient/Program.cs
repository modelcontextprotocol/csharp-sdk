using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// This sample demonstrates how to wire up MCP clients with dependency injection
// using Microsoft.Extensions.Hosting and IServiceCollection.

var builder = Host.CreateApplicationBuilder(args);

// Register an MCP client as a singleton.
// The factory method creates the client with a StdioClientTransport.
// Replace the command/arguments with your own MCP server (e.g., a Docker container).
builder.Services.AddSingleton(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    var transport = new StdioClientTransport(new()
    {
        Name = "Everything",
        Command = "npx",
        Arguments = ["-y", "@modelcontextprotocol/server-everything"],
    });

    // McpClient.CreateAsync is async; we block here for DI registration.
    // In production, consider using an IHostedService to initialize async resources.
    return McpClient.CreateAsync(transport, loggerFactory: loggerFactory)
        .GetAwaiter().GetResult();
});

// Register a hosted service that uses the MCP client.
builder.Services.AddHostedService<McpWorker>();

var host = builder.Build();
await host.RunAsync();

/// <summary>
/// A background service that demonstrates using an injected MCP client.
/// </summary>
sealed class McpWorker(McpClient mcpClient, ILogger<McpWorker> logger, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // List available tools from the MCP server.
        var tools = await mcpClient.ListToolsAsync(cancellationToken: stoppingToken);
        logger.LogInformation("Available tools ({Count}):", tools.Count);
        foreach (var tool in tools)
        {
            logger.LogInformation("  {Name}: {Description}", tool.Name, tool.Description);
        }

        // Invoke a tool.
        var result = await mcpClient.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "Hello from DI!" },
            cancellationToken: stoppingToken);

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        logger.LogInformation("Echo result: {Result}", text);

        // Shut down after the demo completes.
        lifetime.StopApplication();
    }
}
