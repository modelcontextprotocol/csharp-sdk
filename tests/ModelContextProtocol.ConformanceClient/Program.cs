using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// The endpoint will be passed as the first argument
var endpoint = args.Length > 0 ? args[0] : "http://localhost:3001";

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
});

McpClientOptions options = new()
{
    ClientInfo = new()
    {
        Name = "ElicitationClient",
        Version = "1.0.0"
    }
};

await using var mcpClient = await McpClient.CreateAsync(clientTransport, options);

bool success = true;

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Connected to server with tools: {tool.Name}");
}

if (tools.Count > 0)
{
    Console.WriteLine($"Calling tool: {tools.First().Name}");

    var result = await mcpClient.CallToolAsync(toolName: tools.First().Name);

    success &= result.IsError != true;
}

// Exit code 0 on success, 1 on failure
return success ? 0 : 1;