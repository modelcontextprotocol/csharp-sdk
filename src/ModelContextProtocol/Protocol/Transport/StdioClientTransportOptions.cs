namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides options for configuring <see cref="StdioClientTransport"/> instances.
/// </summary>
public record StdioClientTransportOptions
{
    /// <summary>
    /// Gets or sets the command to execute to start the server process.
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
    /// Gets or sets the arguments to pass to the server process when it is started.
    /// </summary>
    public IList<string>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets a transport identifier used for logging purposes.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the server process.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets environment variables to set for the server process.
    /// </summary>
    /// <remarks>
    /// This property allows you to specify environment variables that will be set in the server process's
    /// environment. This is useful for passing configuration, authentication information, or runtime flags
    /// to the server without modifying its code.
    /// </remarks>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the timeout to wait for the server to shut down gracefully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property dictates how long the client should wait for the server process to exit cleanly during shutdown
    /// before forcibly terminating it. This balances between giving the server enough time to clean up 
    /// resources and not hanging indefinitely if a server process becomes unresponsive.
    /// </para>
    /// <para>
    /// The default is five seconds.
    /// </para>
    /// </remarks>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
