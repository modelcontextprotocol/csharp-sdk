using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

string serverAssembly = Path.Combine(AppContext.BaseDirectory, "HttpOverStdioServer.dll");
if (!File.Exists(serverAssembly))
{
    throw new FileNotFoundException(
        "Build the HttpOverStdioClient project so its server executable is copied beside it.",
        serverAssembly);
}

Dictionary<string, string?> environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
foreach (string variable in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64" })
{
    if (Environment.GetEnvironmentVariable(variable) is { } value)
    {
        environment[variable] = value;
    }
}

var transport = new HttpOverStdioClientTransport(
    new StdioClientTransportOptions
    {
        Name = "HTTP/2 stdio sample server",
        Command = "dotnet",
        Arguments = [serverAssembly],
        InheritEnvironmentVariables = false,
        EnvironmentVariables = environment,
    },
    new HttpClientTransportOptions
    {
        Endpoint = new Uri("http://http-over-stdio.local/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
    });

await using McpClient client = await McpClient.CreateAsync(
    transport,
    new McpClientOptions { ProtocolVersion = "2026-07-28" });

Console.WriteLine(
    $"Connected with protocol {client.NegotiatedProtocolVersion}; session ID: {client.SessionId ?? "<none>"}");

IList<McpClientTool> tools = await client.ListToolsAsync();
Console.WriteLine($"Tools: {string.Join(", ", tools.Select(tool => tool.Name))}");

CallToolResult result = await client.CallToolAsync(
    "echo",
    new Dictionary<string, object?> { ["message"] = "hello over HTTP/2 on stdio" });
Console.WriteLine(AssertText(result));

static string AssertText(CallToolResult result) =>
    result.Content.OfType<TextContentBlock>().Single().Text;
