# MCP C# SDK

The official C# SDK for the [Model Context Protocol](https://modelcontextprotocol.io/), enabling .NET applications to connect to and interact with MCP clients and servers.

> [!NOTE]  
> This repo is still in preview, breaking changes can be introduced without prior notice.

## About MCP

The Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools.

For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://spec.modelcontextprotocol.io/)
- [GitHub Organization](https://github.com/modelcontextprotocol)

## Getting Started (Client)

Then create a client and start using tools, or other capabilities, from the servers you configure:

```csharp
McpClientOptions options = new()
{
    ClientInfo = new() { Name = "TestClient", Version = "1.0.0" }
};

McpServerConfig config = new()
{
    Id = "everything",
    Name = "Everything",
    TransportType = TransportTypes.StdIo,
    TransportOptions = new()
    {
        ["command"] = "npx",
        ["arguments"] = "-y @modelcontextprotocol/server-everything",
    }
};

var client = await McpClientFactory.CreateAsync(config, options);

// Print the list of tools available from the server.
await foreach (var tool in client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name} ({tool.Description})");
}

// Execute a tool (this would normally be driven by LLM tool invocations).
var result = await client.CallToolAsync(
    "echo",
    new() { ["message"] = "Hello MCP!" },
    CancellationToken.None);

// echo always returns one and only one text content object
Console.WriteLine(result.Content.First(c => c.Type == "text").Text);
```

Note that you should pass CancellationToken objects suitable for your use case, to enable proper error handling, timeouts, etc. This example also does not paginate the tools list, which may be necessary for large tool sets. See the IntegrationTests project for an example of pagination, as well as examples of how to handle Prompts and Resources.

It is also highly recommended that you pass a proper LoggerFactory instance to the factory constructor, to enable logging of MCP client operations.

You can find samples demonstrating how to use ModelContextProtocol with an LLM SDK in the [samples](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples) directory, and also refer to the [tests](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/tests/ModelContextProtocol.Tests) project for more examples.

Additional examples and documentation will be added as in the near future.

Remember you can connect to any MCP server, not just ones created using ModelContextProtocol. The protocol is designed to be server-agnostic, so you can use this library to connect to any compliant server.

Tools can be exposed easily as `AIFunction` instances so that they are immediately usable with `IChatClient`s.

```csharp
// Get available functions.
IList<AIFunction> tools = await client.GetAIFunctionsAsync();

// Call the chat client using the tools.
IChatClient chatClient = ...;
var response = await chatClient.GetResponseAsync(
    "your prompt here",
    new() 
    {
        Tools = [.. tools],
    });
```

## Getting Started (Server)

Here is an example of how to create an MCP server and register all tools from the current application.
It includes a simple echo tool as an example (this is included in the same file here for easy of copy and paste, but it needn't be in the same file...
the employed overload of `WithTools` examines the current assembly for classes with the `McpToolType` attribute, and registers all methods with the
`McpTool` attribute as tools.)

```csharp
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Hosting;
using System.ComponentModel;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools();
await builder.Build().RunAsync();

[McpToolType]
public static class EchoTool
{
    [McpTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
```

More control is also available, with fine-grained control over configuring the server and how it should handle client requests. For example:

```csharp
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging.Abstractions;

McpServerOptions options = new()
{
    ServerInfo = new() { Name = "MyServer", Version = "1.0.0" },
    Capabilities = new() 
    {
        Tools = new()
        {
            ListToolsHandler = async (request, cancellationToken) =>
            {
                return new ListToolsResult()
                {
                    Tools =
                    [
                        new Tool()
                        {
                            Name = "echo",
                            Description = "Echoes the input back to the client.",
                            InputSchema = new JsonSchema()
                            {
                                Type = "object",
                                Properties = new Dictionary<string, JsonSchemaProperty>()
                                {
                                    ["message"] = new JsonSchemaProperty() { Type = "string", Description = "The input to echo back." }
                                }
                            },
                        }
                    ]
                };
            },

            CallToolHandler = async (request, cancellationToken) =>
            {
                if (request.Params?.Name == "echo")
                {
                    if (request.Params.Arguments?.TryGetValue("message", out var message) is not true)
                    {
                        throw new McpServerException("Missing required argument 'message'");
                    }

                    return new CallToolResponse()
                    {
                        Content = [new Content() { Text = $"Echo: {message}", Type = "text" }]
                    };
                }

                throw new McpServerException($"Unknown tool: '{request.Params?.Name}'");
            },
        }
    },
};

await using IMcpServer server = McpServerFactory.Create(new StdioServerTransport("MyServer"), options);

await server.StartAsync();

// Run until process is stopped by the client (parent process)
await Task.Delay(Timeout.Infinite);
```

## License

This project is licensed under the [MIT License](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/LICENSE).
