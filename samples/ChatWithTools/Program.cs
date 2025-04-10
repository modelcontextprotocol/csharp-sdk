﻿using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.AI;
using OpenAI;

using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddHttpClientInstrumentation()
    .AddSource("*")
    .AddOtlpExporter()
    .Build();
using var metricsProvider = Sdk.CreateMeterProviderBuilder()
    .AddHttpClientInstrumentation()
    .AddMeter("*")
    .AddOtlpExporter()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddOpenTelemetry(opt =>
        {
            opt.IncludeFormattedMessage = true;
            opt.IncludeScopes = true;
            opt.AddOtlpExporter();
        });
    });

// Connect to an MCP server
Console.WriteLine("Connecting client to MCP 'aspnetcore' server");


// Create an IChatClient. (This shows using OpenAIClient, but it could be any other IChatClient implementation.)
// Provide your own OPENAI_API_KEY via an environment variable.
using IChatClient chatClient =
    new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")).GetChatClient("gpt-4o-mini").AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(loggerFactory: loggerFactory, configure: o => o.EnableSensitiveData = true)
    .Build();

var samplingHandler = chatClient.CreateSamplingHandler();

var mcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new()
    {
        Command = "npx",
        Arguments = ["-y", "--verbose", "@modelcontextprotocol/server-everything"],
        Name = "Everything",
    }),
    clientOptions: new McpClientOptions()
    {
        Capabilities = new ClientCapabilities()
        {
            Sampling = new SamplingCapability() { SamplingHandler = (param, progress, ct) => {
                Console.WriteLine(param?.Meta?.ProgressToken);
                return samplingHandler(param, progress, ct);
            } }
        },
    },
    loggerFactory: loggerFactory);

// Get all available tools
Console.WriteLine("Tools available:");
var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"  {tool}");
}

Console.WriteLine();

// Have a conversation, making all tools available to the LLM.
List<ChatMessage> messages = [];
while (true)
{
    Console.Write("Q: ");

    messages.Add(new(ChatRole.User, Console.ReadLine()));

    List<ChatResponseUpdate> updates = [];
    await foreach (var update in chatClient.GetStreamingResponseAsync(messages, new() { Tools = [.. tools]}))
    {
        Console.Write(update);
        updates.Add(update);
    }
    Console.WriteLine();

    messages.AddMessages(updates);
}