using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.AI;
using OpenAI;

// Connect to an MCP server
Console.WriteLine("Connecting client to MCP 'everything' server");
var mcpClient = await McpClientFactory.CreateAsync(
    new()
    {
        Id = "everything",
        Name = "Everything",
        TransportType = TransportTypes.StdIo,
        TransportOptions = new()
        {
            ["command"] = "npx", ["arguments"] = "-y @modelcontextprotocol/server-everything",
        }
    });

// Get all available tools
Console.WriteLine("Tools available:");
var tools = await mcpClient.GetAIFunctionsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"  {tool}");
}
Console.WriteLine();

// Create an IChatClient. (This shows using OpenAIClient, but it could be any other IChatClient implementation.)
// Provide your own OPENAI_API_KEY via an environment variable.
using IChatClient chatClient =
    new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")).AsChatClient("gpt-4o-mini")
    .AsBuilder().UseFunctionInvocation().Build();

// Have a conversation, making all tools available to the LLM.
List<ChatMessage> messages = [];
while (true)
{
    Console.Write("Q: ");
    messages.Add(new(ChatRole.User, Console.ReadLine()));

    List<ChatResponseUpdate> updates = [];
    await foreach (var update in chatClient.GetStreamingResponseAsync(messages, new() { Tools = [.. tools] }))
    {
        Console.Write(update);
        updates.Add(update);
    }
    Console.WriteLine();

    messages.AddMessages(updates);
}