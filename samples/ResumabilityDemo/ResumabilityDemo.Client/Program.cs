using Microsoft.Extensions.Logging;

namespace ResumabilityDemo.Client;

/// <summary>
/// Interactive client for testing MCP resumability features.
/// </summary>
public static class Program
{
    private static readonly Uri DefaultServerUri = new("http://localhost:5000/mcp");

    public static async Task Main(string[] args)
    {
        // Logging is disabled by default to keep console clean
        // Use --verbose flag to enable debug logging
        var verbose = args.Contains("--verbose") || args.Contains("-v");

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            if (verbose)
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.None);
            }
        });

        // Filter out --verbose/-v from args for URI parsing
        var filteredArgs = args.Where(a => a != "--verbose" && a != "-v").ToArray();
        var serverUri = filteredArgs.Length > 0 ? new Uri(filteredArgs[0]) : DefaultServerUri;

        await using var state = new ClientState
        {
            LoggerFactory = loggerFactory,
            ServerUri = serverUri
        };

        var commandHandler = new CommandHandler(state);

        Console.WriteLine($"Server URI: {serverUri}");
        if (verbose)
        {
            Console.WriteLine("Verbose logging enabled");
        }

        ConsoleUI.PrintHelp();
        Console.WriteLine();

        while (true)
        {
            state.CleanupCompletedOperations();
            ConsoleUI.PrintPrompt(state);

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                continue;

            if (!await commandHandler.HandleCommandAsync(input))
            {
                break;
            }
        }
    }
}
