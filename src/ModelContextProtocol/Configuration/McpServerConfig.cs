namespace ModelContextProtocol;

/// <summary>
/// Configuration for an MCP server connection.
/// This is passed to the client factory to create a client for a specific server.
/// </summary>
public record McpServerConfig
{
    /// <summary>
    /// Unique identifier for this server configuration.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the server.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The type of transport to use.
    /// </summary>
    public required string TransportType { get; init; }

    /// <summary>
    /// For stdio transport: path to the executable
    /// For HTTP transport: base URL of the server
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Arguments (if any) to pass to the executable.
    /// </summary>
    public string[]? Arguments { get; init; }

    /// <summary>
    /// Additional transport-specific configuration.
    /// </summary>
    public Dictionary<string, string>? TransportOptions { get; init; }
}