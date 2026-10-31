// <snippet_IntegrationTestServer>
using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<EchoTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.Run();

// WebApplicationFactory<TEntryPoint> needs a public entry point.
public partial class Program;

[McpServerToolType]
public sealed class EchoTools
{
    [McpServerTool(Name = "echo"), Description("Returns the supplied message.")]
    public static string Echo(string message) => $"Echo: {message}";
}
// </snippet_IntegrationTestServer>
