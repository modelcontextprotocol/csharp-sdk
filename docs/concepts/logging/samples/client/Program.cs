using ModelContextProtocol.Protocol;
using ModelContextProtocol.Client;
using System.Text.Json;

var endpoint = Environment.GetEnvironmentVariable("ENDPOINT") ?? "http://localhost:3001";

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
});

await using var mcpClient = await McpClient.CreateAsync(clientTransport);

// <snippet_LoggingCapabilities>
// Verify that the server supports logging
if (mcpClient.ServerCapabilities.Logging is null)
{
    Console.WriteLine("Server does not support logging.");
    return;
}
// </snippet_LoggingCapabilities>

// Get the first argument if it was specified
var firstArgument = args.Length > 0 ? args[0] : null;

if (firstArgument is not null)
{
    // Set the logging level to the value from the first argument
    if (Enum.TryParse<LoggingLevel>(firstArgument, true, out var loggingLevel))
    {
        // <snippet_LoggingLevel>
        await mcpClient.SetLoggingLevelAsync(loggingLevel);
        // </snippet_LoggingLevel>
    }
    else
    {
        Console.WriteLine($"Invalid logging level: {firstArgument}");
        // Print a list of valid logging levels
        Console.WriteLine("Valid logging levels are:");
        foreach (var level in Enum.GetValues<LoggingLevel>())
        {
            Console.WriteLine($" - {level}");
        }
    }
}

// <snippet_LoggingHandler>
mcpClient.RegisterNotificationHandler(NotificationMethods.LoggingMessageNotification,
    (notification, ct) =>
    {
        if (JsonSerializer.Deserialize<LoggingMessageNotificationParams>(notification.Params) is { } ln)
        {
            Console.WriteLine($"[{ln.Level}] {ln.Logger} {ln.Data}");
        }
        else
        {
            Console.WriteLine($"Received unexpected logging notification: {notification.Params}");
        }

        return default;
    });
// </snippet_LoggingHandler>

// Now call the "logging_tool" tool
await mcpClient.CallToolAsync("logging_tool");

