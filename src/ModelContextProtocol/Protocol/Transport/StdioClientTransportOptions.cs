namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents configuration options for the stdio transport.
/// </summary>
public record StdioClientTransportOptions
{
    /// <summary>
    /// The default timeout to wait for the server to shut down gracefully.
    /// </summary>
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The command to execute to start the server process.
    /// </summary>
    public required string Command
    {
        get;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Command cannot be null or empty.", nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Arguments to pass to the server process.
    /// </summary>
    public IList<string>? Arguments { get; set; }

    /// <summary>
    /// Description of the server process used for logging purposes.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The working directory for the server process.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables to set for the server process.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// The timeout to wait for the server to shut down gracefully.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; init; } = DefaultShutdownTimeout;
}
