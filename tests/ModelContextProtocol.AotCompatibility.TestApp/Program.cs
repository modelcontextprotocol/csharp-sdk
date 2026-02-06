using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

Pipe clientToServerPipe = new(), serverToClientPipe = new();

// Create a server using a stream-based transport over an in-memory pipe.
await using McpServer server = McpServer.Create(
    new StreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream()),
    new McpServerOptions()
    {
        ToolCollection = [McpServerTool.Create((string arg) => $"Echo: {arg}", new() { Name = "Echo" })]
    });
_ = server.RunAsync();

// Connect a client using a stream-based transport over the same in-memory pipe.
await using McpClient client = await McpClient.CreateAsync(
    new StreamClientTransport(clientToServerPipe.Writer.AsStream(), serverToClientPipe.Reader.AsStream()));

// List all tools.
var tools = await client.ListToolsAsync();
if (tools.Count == 0)
{
    throw new Exception("Expected at least one tool.");
}

// Invoke a tool.
var echo = tools.First(t => t.Name == "Echo");
var result = await echo.InvokeAsync(new() { ["arg"] = "Hello World" });
if (result is null || !result.ToString()!.Contains("Echo: Hello World"))
{
    throw new Exception($"Unexpected result: {result}");
}

Console.WriteLine("Success!");
