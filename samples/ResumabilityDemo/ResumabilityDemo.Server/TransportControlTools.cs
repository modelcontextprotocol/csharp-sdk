using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ResumabilityDemo.Server;

/// <summary>
/// Tools for controlling and simulating transport disconnections for testing.
/// </summary>
[McpServerToolType]
public sealed class TransportControlTools
{
    private readonly TransportRegistry _registry;
    private readonly ILogger<TransportControlTools> _logger;

    public TransportControlTools(TransportRegistry registry, ILogger<TransportControlTools> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Kills all active POST transports to simulate a network disconnection.
    /// This does NOT end the MCP session - the client can still reconnect.
    /// </summary>
    [McpServerTool, Description("Kills all active POST transports to simulate network failure. The session remains active and the client can poll for results.")]
    public async Task<string> KillTransports()
    {
        _logger.LogWarning("KillTransports called - simulating network failure");

        var count = await _registry.KillAllAsync();

        return count > 0
            ? $"Killed {count} active transport(s). Pending tool calls will fail but results remain in the event store."
            : "No active transports to kill.";
    }

    /// <summary>
    /// Lists all currently registered (active) transports.
    /// </summary>
    [McpServerTool, Description("Lists all currently active POST transports.")]
    public string ListTransports()
    {
        var ids = _registry.GetRegisteredIds();

        if (ids.Count == 0)
        {
            return "No active transports.";
        }

        return $"Active transports ({ids.Count}):\n" + string.Join("\n", ids.Select(id => $"  - {id}"));
    }

    /// <summary>
    /// Gets the count of active transports.
    /// </summary>
    [McpServerTool, Description("Gets the count of active POST transports.")]
    public int TransportCount()
    {
        return _registry.Count;
    }
}
