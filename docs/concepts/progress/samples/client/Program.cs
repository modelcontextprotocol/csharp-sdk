using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var endpoint = Environment.GetEnvironmentVariable("ENDPOINT") ?? "http://localhost:3001";

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
});

McpClientOptions options = new()
{
    ClientInfo = new()
    {
        Name = "ProgressClient",
        Version = "1.0.0"
    }
};

await using var mcpClient = await McpClient.CreateAsync(clientTransport, options);

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Connected to server with tools: {tool.Name}");
}

Console.WriteLine($"Calling tool: {tools.First().Name}");

// <snippet_ProgressHandler>
var progressHandler = new Progress<ProgressNotificationValue>(value =>
{
    Console.WriteLine($"Tool progress: {value.Progress} of {value.Total} - {value.Message}");
});

var result = await mcpClient.CallToolAsync(toolName: tools.First().Name, progress: progressHandler);
// </snippet_ProgressHandler>

foreach (var block in result.Content)
{
    if (block is TextContentBlock textBlock)
    {
        Console.WriteLine(textBlock.Text);
    }
    else
    {
        Console.WriteLine($"Received unexpected result content of type {block.GetType()}");
    }
}
