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

await RunAsync(
    "HTTP/2 over stdio",
    new HttpOverStdioClientTransport(
        CreateStdioOptions(serverAssembly, environment),
        new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://http-over-stdio.local/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }),
    new McpClientOptions { ProtocolVersion = "2026-07-28" },
    "hello over HTTP/2 on stdio");

await RunAsync(
    "Legacy JSON-RPC stdio",
    new StdioClientTransport(CreateStdioOptions(serverAssembly, environment)),
    new McpClientOptions { ProtocolVersion = "2025-11-25" },
    "hello over legacy stdio");

static StdioClientTransportOptions CreateStdioOptions(
    string serverAssembly,
    Dictionary<string, string?> environment) =>
    new()
    {
        Name = "dual-mode stdio sample server",
        Command = "dotnet",
        Arguments = [serverAssembly],
        InheritEnvironmentVariables = false,
        EnvironmentVariables = new Dictionary<string, string?>(environment),
    };

static async Task RunAsync(
    string label,
    IClientTransport transport,
    McpClientOptions options,
    string message)
{
    await using McpClient client = await McpClient.CreateAsync(transport, options);

    Console.WriteLine(
        $"{label}: protocol {client.NegotiatedProtocolVersion}; session ID: {client.SessionId ?? "<none>"}");

    IList<McpClientTool> tools = await client.ListToolsAsync();
    Console.WriteLine($"{label}: tools: {string.Join(", ", tools.Select(tool => tool.Name))}");

    CallToolResult result = await client.CallToolAsync(
        "echo",
        new Dictionary<string, object?> { ["message"] = message });
    Console.WriteLine($"{label}: {AssertText(result)}");
}

static string AssertText(CallToolResult result) =>
    result.Content.OfType<TextContentBlock>().Single().Text;
