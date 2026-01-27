using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>();

IClientTransport clientTransport;
var (command, arguments) = GetCommandAndArguments(args);

if (command == "http")
{
    // make sure AspNetCoreMcpServer is running
    clientTransport = new HttpClientTransport(new()
    {
        Endpoint = new Uri("http://localhost:3001")
    });
}
else
{
    clientTransport = new StdioClientTransport(new()
    {
        Name = "Demo Server",
        Command = command,
        Arguments = arguments,
    });
}
await using var mcpClient = await McpClient.CreateAsync(clientTransport!);

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Connected to server with tools: {tool.Name}");
}

using var anthropicClient = new AnthropicClient(new() { ApiKey = builder.Configuration["ANTHROPIC_API_KEY"] })
    .AsIChatClient("claude-haiku-4-5-20251001")
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var options = new ChatOptions
{
    MaxOutputTokens = 1000,
    ModelId = "claude-haiku-4-5-20251001",
    Tools = [.. tools]
};

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("MCP Client Started!");
Console.ResetColor();

var messages = new List<ChatMessage>();
var sb = new StringBuilder();

PromptForInput();
while (Console.ReadLine() is string query && !"exit".Equals(query, StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(query))
    {
        PromptForInput();
        continue;
    }

    messages.Add(new ChatMessage(ChatRole.User, query));
    await foreach (var message in anthropicClient.GetStreamingResponseAsync(messages, options))
    {
        Console.Write(message);
        sb.Append(message.ToString());
    }

    Console.WriteLine();
    sb.AppendLine();
    messages.Add(new ChatMessage(ChatRole.Assistant, sb.ToString()));
    sb.Clear();

    PromptForInput();
}

static void PromptForInput()
{
    Console.WriteLine("Enter a command (or 'exit' to quit):");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("> ");
    Console.ResetColor();
}

/// <summary>
/// Determines the command (executable) to run and the script/path to pass to it. This allows different
/// languages/runtime environments to be used as the MCP server.
/// </summary>
/// <remarks>
/// This method uses the file extension of the first argument to determine the command, if it's py, it'll run python,
/// if it's js, it'll run node, if it's a directory or a csproj file, it'll run dotnet.
///
/// If no arguments are provided, it defaults to running the QuickstartWeatherServer project from the current repo.
///
/// This method would only be required if you're creating a generic client, such as we use for the quickstart.
/// </remarks>
static (string command, string[] arguments) GetCommandAndArguments(string[] args)
{
    return args switch
    {
        [var mode] when mode.Equals("http", StringComparison.OrdinalIgnoreCase) => ("http", args),
        [var script] when script.EndsWith(".py") => ("python", args),
        [var script] when script.EndsWith(".js") => ("node", args),
        [var script] when Directory.Exists(script) || (File.Exists(script) && script.EndsWith(".csproj")) => ("dotnet", ["run", "--project", script]),
        _ => ("dotnet", ["run", "--project", Path.Combine(GetCurrentSourceDirectory(), "../QuickstartWeatherServer")])
    };
}

static string GetCurrentSourceDirectory([CallerFilePath] string? currentFile = null)
{
    Debug.Assert(!string.IsNullOrWhiteSpace(currentFile));
    return Path.GetDirectoryName(currentFile) ?? throw new InvalidOperationException("Unable to determine source directory.");
}
