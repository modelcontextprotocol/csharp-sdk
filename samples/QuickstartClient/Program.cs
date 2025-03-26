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

var (command, arguments) = args switch
{
    [var script] when script.EndsWith(".py") => ("python", script),
    [var script] when script.EndsWith(".js") => ("node", script),
    [var script] when Directory.Exists(script) || (File.Exists(script) && script.EndsWith(".csproj")) => ("dotnet", $"run --project {script} --no-build"),
    _ => ("dotnet", "run --project ../../../../QuickstartWeatherServer --no-build")
};

await using var mcpClient = await McpClientFactory.CreateAsync(new()
{
    Id = "demo-client",
    Name = "Demo Client",
    TransportType = TransportTypes.StdIo,
    TransportOptions = new()
    {
        ["command"] = command,
        ["arguments"] = arguments,
    }
});

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Connected to server with tools: {tool.Name}");
}

using var anthropicClient = new AnthropicClient(new APIAuthentication(builder.Configuration["ANTHROPIC_API_KEY"]))
    .Messages
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var options = new ChatOptions
{
    MaxOutputTokens = 1000,
    ModelId = "claude-3-5-sonnet-20241022",
    Tools = [.. tools]
};

while (true)
{
    Console.WriteLine("MCP Client Started!");
    Console.WriteLine("Type your queries or 'quit' to exit.");

    string? query = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(query))
    {
        continue;
    }
    if (string.Equals(query, "quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var response = anthropicClient.GetStreamingResponseAsync(query, options);

    await foreach (var message in response)
    {
        Console.Write(message.Text);
    }
    Console.WriteLine();
}
