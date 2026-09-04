using HttpOverStdioServer;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO.Pipelines;

PipeReader stdin = PipeReader.Create(Console.OpenStandardInput());
StdioTransportKind transportKind = await StdioTransportDetector.DetectAsync(stdin);

await using Stream input = stdin.AsStream();
await using Stream output = Console.OpenStandardOutput();

if (transportKind is StdioTransportKind.Http2)
{
    var builder = WebApplication.CreateBuilder(args);
    AddSampleServer(builder.Services)
        .WithHttpOverStdioTransport(input, output);

    var app = builder.Build();
    app.MapMcp("/mcp");
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    AddSampleServer(builder.Services)
        .WithStreamServerTransport(input, output);
    builder.Logging.AddConsole(options =>
        options.LogToStandardErrorThreshold = LogLevel.Trace);

    await builder.Build().RunAsync();
}

static IMcpServerBuilder AddSampleServer(IServiceCollection services) =>
    services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "dual-stdio-sample",
                Version = "1.0.0",
            };
        })
        .WithTools<SampleTools>();

[McpServerToolType]
internal sealed class SampleTools
{
    [McpServerTool(Name = "echo"), Description("Echoes a message over the selected stdio transport.")]
    public static string Echo([Description("The message to echo.")] string message) =>
        $"Server received: {message}";
}
