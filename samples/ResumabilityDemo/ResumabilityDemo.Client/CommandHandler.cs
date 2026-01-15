using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ResumabilityDemo.Client;

/// <summary>
/// Handles command execution for the interactive client.
/// </summary>
public sealed class CommandHandler
{
    private readonly ClientState _state;

    public CommandHandler(ClientState state)
    {
        _state = state;
    }

    public async Task<bool> HandleCommandAsync(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return true; // Continue loop
        }

        var command = parts[0].ToLowerInvariant();

        try
        {
            switch (command)
            {
                case "help":
                case "?":
                    ConsoleUI.PrintHelp();
                    break;

                case "quit":
                case "exit":
                    Console.WriteLine("Goodbye!");
                    return false; // Exit loop

                case "connect":
                    await HandleConnectAsync();
                    break;

                case "disconnect":
                    await HandleDisconnectAsync(graceful: true);
                    break;

                case "kill":
                    await HandleKillAsync();
                    break;

                case "status":
                    ConsoleUI.PrintStatus(_state);
                    break;

                case "pending":
                    ConsoleUI.PrintPendingOperations(_state);
                    break;

                case "cancel":
                    HandleCancel(parts);
                    break;

                case "tools":
                    await HandleListToolsAsync();
                    break;

                case "transports":
                    await HandleListTransportsAsync();
                    break;

                case "echo":
                    HandleEcho(parts);
                    break;

                case "delay":
                    HandleDelay(parts);
                    break;

                case "progress":
                    HandleProgress(parts);
                    break;

                case "polling":
                    HandlePolling(parts);
                    break;

                case "combo":
                    HandleCombo(parts);
                    break;

                case "uid":
                    HandleUid(parts);
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return true; // Continue loop
    }

    private async Task HandleConnectAsync()
    {
        Console.WriteLine($"Connecting to {_state.ServerUri}...");
        await _state.ConnectAsync();
        Console.WriteLine($"Connected! Server: {_state.Client!.ServerInfo?.Name} v{_state.Client.ServerInfo?.Version}");
        Console.WriteLine($"Session ID: {_state.Client.SessionId}");
    }

    private async Task HandleDisconnectAsync(bool graceful)
    {
        if (graceful)
        {
            Console.WriteLine("Disconnecting gracefully...");
        }
        else
        {
            Console.WriteLine("FORCING disconnect (simulating network failure)...");
        }

        await _state.DisconnectAsync(graceful);
        Console.WriteLine(graceful ? "Disconnected." : "Connection killed!");
    }

    private async Task HandleKillAsync()
    {
        _state.EnsureConnected();
        Console.WriteLine("Sending kill command to server to terminate active POST transports...");

        try
        {
            var result = await _state.Client!.CallToolAsync("kill_transports", new Dictionary<string, object?>());
            Console.WriteLine("Kill command sent:");
            ConsoleUI.PrintResult(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Kill command failed: {ex.Message}");
        }
    }

    private void HandleCancel(string[] parts)
    {
        if (parts.Length > 1 && int.TryParse(parts[1], out var cancelId))
        {
            if (_state.TryCancelOperation(cancelId))
            {
                Console.WriteLine($"Cancellation requested for operation {cancelId}.");
            }
            else
            {
                Console.WriteLine($"Operation {cancelId} not found.");
            }
        }
        else
        {
            Console.WriteLine("Usage: cancel <operation-id>");
        }
    }

    private async Task HandleListToolsAsync()
    {
        _state.EnsureConnected();
        var tools = await _state.Client!.ListToolsAsync();
        ConsoleUI.PrintTools(tools);
    }

    private async Task HandleListTransportsAsync()
    {
        _state.EnsureConnected();
        var result = await _state.Client!.CallToolAsync("list_transports", new Dictionary<string, object?>());
        ConsoleUI.PrintResult(result);
    }

    private void HandleEcho(string[] parts)
    {
        var message = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Hello!";
        StartToolCall("echo", new Dictionary<string, object?> { ["message"] = message },
            $"echo(\"{message}\")");
    }

    private void HandleDelay(string[] parts)
    {
        var message = parts.Length > 1 ? parts[1] : "Hello!";
        var seconds = parts.Length > 2 && int.TryParse(parts[2], out var ds) ? ds : 5;
        StartToolCall("delayed_echo", new Dictionary<string, object?>
        {
            ["message"] = message,
            ["delaySeconds"] = seconds
        }, $"delayed_echo(\"{message}\", {seconds}s)");
    }

    private void HandleProgress(string[] parts)
    {
        var steps = parts.Length > 1 && int.TryParse(parts[1], out var ps) ? ps : 10;
        var interval = parts.Length > 2 && int.TryParse(parts[2], out var pi) ? pi : 1000;
        StartToolCallWithProgress("progress_demo", new Dictionary<string, object?>
        {
            ["steps"] = steps,
            ["intervalMs"] = interval
        }, $"progress_demo({steps} steps, {interval}ms)");
    }

    private void HandlePolling(string[] parts)
    {
        var retryInterval = parts.Length > 1 && int.TryParse(parts[1], out var ri) ? ri : 2;
        var workDuration = parts.Length > 2 && int.TryParse(parts[2], out var wd) ? wd : 5;
        Console.WriteLine("  (Server will disconnect and client will poll for result)");
        StartToolCall("trigger_polling_mode", new Dictionary<string, object?>
        {
            ["retryIntervalSeconds"] = retryInterval,
            ["workDurationSeconds"] = workDuration
        }, $"trigger_polling_mode({retryInterval}s retry, {workDuration}s work)");
    }

    private void HandleCombo(string[] parts)
    {
        var progress = parts.Length > 1 && int.TryParse(parts[1], out var cp) ? cp : 3;
        var interval = parts.Length > 2 && int.TryParse(parts[2], out var ci) ? ci : 500;
        var retry = parts.Length > 3 && int.TryParse(parts[3], out var cr) ? cr : 1;
        var work = parts.Length > 4 && int.TryParse(parts[4], out var cw) ? cw : 3;
        StartToolCallWithProgress("progress_then_polling", new Dictionary<string, object?>
        {
            ["progressSteps"] = progress,
            ["progressIntervalMs"] = interval,
            ["retryIntervalSeconds"] = retry,
            ["postPollingWorkSeconds"] = work
        }, $"progress_then_polling({progress} steps, {interval}ms, {retry}s retry, {work}s work)");
    }

    private void HandleUid(string[] parts)
    {
        var prefix = parts.Length > 1 ? parts[1] : null;
        StartToolCall("generate_unique_id", new Dictionary<string, object?>
        {
            ["prefix"] = prefix
        }, $"generate_unique_id({prefix ?? "null"})");
    }

    private void StartToolCall(string toolName, Dictionary<string, object?> arguments, string description)
    {
        _state.EnsureConnected();

        var cts = new CancellationTokenSource();
        var client = _state.Client!;

        // Pre-allocate the operation ID so we can use it inside the task
        int operationId = 0;

        var task = Task.Run(async () =>
        {
            try
            {
                var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cts.Token);
                Console.WriteLine($"\n[{description}] completed:");
                ConsoleUI.PrintResult(result);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"\n[{description}] was canceled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[{description}] failed: {ex.Message}");
            }
            finally
            {
                _state.RemoveOperation(operationId);
                ConsoleUI.PrintPrompt(_state);
            }
        });

        operationId = _state.AddPendingOperation(description, task, cts);
        Console.WriteLine($"[{operationId}] Starting {description}...");
    }

    private void StartToolCallWithProgress(string toolName, Dictionary<string, object?> arguments, string description)
    {
        _state.EnsureConnected();

        var cts = new CancellationTokenSource();
        var client = _state.Client!;

        // Pre-allocate the operation ID so we can use it inside the task
        int operationId = 0;

        var task = Task.Run(async () =>
        {
            try
            {
                var result = await client.CallToolAsync(toolName, arguments,
                    progress: new Progress<ProgressNotificationValue>(p =>
                    {
                        Console.WriteLine($"  Progress: {p.Progress}/{p.Total}");
                    }),
                    cancellationToken: cts.Token);
                Console.WriteLine($"\n[{description}] completed:");
                ConsoleUI.PrintResult(result);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"\n[{description}] was canceled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[{description}] failed: {ex.Message}");
            }
            finally
            {
                _state.RemoveOperation(operationId);
                ConsoleUI.PrintPrompt(_state);
            }
        });

        operationId = _state.AddPendingOperation(description, task, cts);
        Console.WriteLine($"[{operationId}] Starting {description}...");
    }
}
