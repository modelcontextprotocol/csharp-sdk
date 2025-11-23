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
Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

// Call the "add_numbers" tool
var toolName = "add_numbers";
Console.WriteLine($"Calling tool: {toolName}");
var result = await mcpClient.CallToolAsync(toolName: toolName, arguments: new Dictionary<string, object?>
{
    { "a", 5 },
    { "b", 10 }
});

success &= result.IsError != true;

// Exit code 0 on success, 1 on failure
return success ? 0 : 1;