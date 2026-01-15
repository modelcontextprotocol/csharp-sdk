using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ResumabilityDemo.Client;

/// <summary>
/// Console output helpers for the interactive client.
/// </summary>
public static class ConsoleUI
{
    public static void PrintHelp()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║          ResumabilityDemo - MCP Resumability Test Client      ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ Connection:                                                   ║");
        Console.WriteLine("║   connect           - Connect to the MCP server               ║");
        Console.WriteLine("║   disconnect        - Gracefully disconnect from server       ║");
        Console.WriteLine("║   status            - Show connection and pending ops status  ║");
        Console.WriteLine("║ Operations:                                                   ║");
        Console.WriteLine("║   tools             - List available tools                    ║");
        Console.WriteLine("║   transports        - List active POST transports on server   ║");
        Console.WriteLine("║   echo <msg>        - Call echo tool                          ║");
        Console.WriteLine("║   delay <msg> [s]   - Call delayed_echo (default 5s)          ║");
        Console.WriteLine("║   progress [n] [ms] - Call progress_demo                      ║");
        Console.WriteLine("║   polling [r] [w]   - Call trigger_polling_mode               ║");
        Console.WriteLine("║   combo [p] [i] [r] [w] - Call progress_then_polling          ║");
        Console.WriteLine("║   uid [prefix]      - Call generate_unique_id                 ║");
        Console.WriteLine("║ Network simulation:                                           ║");
        Console.WriteLine("║   kill              - Kill active POST transports on server   ║");
        Console.WriteLine("║ Background ops:                                               ║");
        Console.WriteLine("║   pending           - List pending operations                 ║");
        Console.WriteLine("║   cancel <id>       - Cancel a pending operation              ║");
        Console.WriteLine("║ Other:                                                        ║");
        Console.WriteLine("║   help/?            - Show this help                          ║");
        Console.WriteLine("║   quit/exit         - Exit the client                         ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    public static void PrintStatus(ClientState state)
    {
        Console.WriteLine($"Connected: {(state.IsConnected ? "Yes" : "No")}");
        if (state.IsConnected && state.Client is not null)
        {
            Console.WriteLine($"  Server: {state.Client.ServerInfo?.Name} v{state.Client.ServerInfo?.Version}");
            Console.WriteLine($"  Session ID: {state.Client.SessionId}");
        }
        Console.WriteLine($"Pending operations: {state.PendingCount}");
    }

    public static void PrintPendingOperations(ClientState state)
    {
        state.CleanupCompletedOperations();
        if (state.PendingCount == 0)
        {
            Console.WriteLine("No pending operations.");
            return;
        }

        Console.WriteLine($"Pending operations ({state.PendingCount}):");
        foreach (var op in state.PendingOperations)
        {
            Console.WriteLine($"  [{op.Id}] {op.Description} - {op.Status}");
        }
    }

    public static void PrintResult(CallToolResult result)
    {
        Console.WriteLine("  Result:");
        foreach (var content in result.Content)
        {
            if (content is TextContentBlock textContent)
            {
                Console.WriteLine($"    {textContent.Text}");
            }
            else
            {
                Console.WriteLine($"    [{content.Type}]");
            }
        }
        if (result.IsError == true)
        {
            Console.WriteLine("    (Error flagged)");
        }
    }

    public static void PrintPrompt(ClientState state)
    {
        Console.Write(state.PendingCount > 0 ? $"[{state.PendingCount} pending] > " : "> ");
    }

    public static void PrintTools(IList<McpClientTool> tools)
    {
        Console.WriteLine($"Available tools ({tools.Count}):");
        foreach (var tool in tools)
        {
            Console.WriteLine($"  - {tool.Name}: {tool.Description}");
        }
    }
}
