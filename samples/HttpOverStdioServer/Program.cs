using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "http-over-stdio-sample",
            Version = "1.0.0",
        };
    })
    .WithHttpOverStdioTransport()
    .WithTools<SampleTools>();

var app = builder.Build();
app.MapMcp("/mcp");
await app.RunAsync();

[McpServerToolType]
internal sealed class SampleTools
{
    [McpServerTool(Name = "echo"), Description("Echoes a message over Streamable HTTP on stdio.")]
    public static string Echo([Description("The message to echo.")] string message) =>
        $"Server received: {message}";
}
