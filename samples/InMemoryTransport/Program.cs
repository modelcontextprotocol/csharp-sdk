﻿using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

Pipe clientToServerPipe = new(), serverToClientPipe = new();

// Create a server using a stream-based transport over an in-memory pipe.
await using IMcpServer server = McpServerFactory.Create(
    new StreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream()),
    new McpServerOptions()
    {
        Capabilities = new()
        {
            Tools = new()
            {
                ToolCollection = [McpServerTool.Create((string arg) => $"Echo: {arg}", new() { Name = "Echo" })]
            }
        }
    });
_ = server.RunAsync();

// Connect a client using a stream-based transport over the same in-memory pipe.
await using IMcpClient client = await McpClientFactory.CreateAsync(
    new StreamClientTransport(clientToServerPipe.Writer.AsStream(), serverToClientPipe.Reader.AsStream()));

// List all tools.
var tools = await client.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Tool Name: {tool.Name}");
}
Console.WriteLine();

// Invoke a tool.
var echo = tools.First(t => t.Name == "Echo");
Console.WriteLine(await echo.InvokeAsync(new()
{
    ["arg"] = "Hello World"
}));