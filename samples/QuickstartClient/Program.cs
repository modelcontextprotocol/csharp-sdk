using Anthropic.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Configuration
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>();

var mcpClient = await McpClientFactory.CreateAsync(new()
{
    Id = "weather",
    Name = "Weather",
    TransportType = TransportTypes.StdIo,
    TransportOptions = new()
    {
        ["command"] = "dotnet",
        ["arguments"] = "run --project ../QuickstartWeatherServer",
    }
});

var anthropicClient = new AnthropicClient(new APIAuthentication(builder.Configuration["ANTHROPIC_API_KEY"]))
    .Messages
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Tool: {tool.Name}");
}

while (true)
{
    Console.WriteLine("MCP Client Started!");
    Console.WriteLine("Enter a command (or 'exit' to quit):");

    string? command = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(command))
    {
        continue;
    }
    if (string.Equals(command, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var response = await ProcessQueryAsync(command);

    if (string.IsNullOrWhiteSpace(response))
    {
        Console.WriteLine("No response received.");
    }
    else
    {
        Console.WriteLine($"Response: {response}");
    }
}

async Task<string> ProcessQueryAsync(string query)
{
    var options = new ChatOptions
    {
        MaxOutputTokens = 1000,
        ModelId = "claude-3-5-sonnet-20241022",
        Tools = [.. tools.Cast<AITool>()]
    };

    var response = await anthropicClient.GetResponseAsync(query, options);

    return "";
}