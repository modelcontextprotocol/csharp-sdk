namespace ModelContextProtocol.Client;

/// <summary>
/// Provides options for configuring <see cref="StdioClientTransport"/> instances.
/// </summary>
public sealed class StdioClientTransportOptions
{
    /// <summary>
    /// Gets or sets the command to execute to start the server process.
    /// </summary>
    /// <exception cref="ArgumentException">The value is <see langword="null"/>, empty, or composed entirely of whitespace.</exception>
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
    /// <para>
    /// This property allows you to specify environment variables that will be set in the server process's
    /// environment. Setting these variables is useful for passing configuration, authentication information, or runtime flags
    /// to the server without modifying its code.
    /// </para>
    /// <para>
    /// By default, when starting the server process, the server process will inherit the current environment's variables,
    /// as discovered via <see cref="Environment.GetEnvironmentVariables()"/>. After those variables are found, the entries
    /// in this <see cref="EnvironmentVariables"/> dictionary are used to augment and overwrite the entries read from the environment.
    /// That includes removing the variables for any of this collection's entries with a null value.
    /// </para>
    /// </remarks>
    public IDictionary<string, string?>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the timeout to wait for the server to shut down gracefully.
    /// </summary>
    /// <value>
    /// The amount of time to wait for the server to shut down gracefully. The default is 5 seconds.
    /// </value>
    /// <remarks>
    /// <para>
    /// This property dictates how long the client should wait for the server process to exit cleanly during shutdown
    /// before forcibly terminating it. This balances giving the server enough time to clean up
    /// resources and not hanging indefinitely if a server process becomes unresponsive.
    /// </para>
    /// </remarks>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a callback that is invoked for each line of stderr received from the server process.
    /// </summary>
    public Action<string>? StandardErrorLines { get; set; }
}
