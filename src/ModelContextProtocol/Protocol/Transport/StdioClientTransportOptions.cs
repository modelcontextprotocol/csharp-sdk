namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents configuration options for the stdio transport.
/// </summary>
public record StdioClientTransportOptions
{
    /// <summary>
    /// The default timeout to wait for the server to shut down gracefully.
    /// </summary>
    /// <remarks>
    /// This value (5 seconds) is used as the default when no explicit shutdown timeout is specified.
    /// During shutdown, the client waits for this duration to allow the server process to exit cleanly 
    /// before forcibly terminating it if necessary. This helps ensure resources are released properly.
    /// 
    /// You may need to adjust this value based on server complexity or shutdown requirements:
    /// - For servers with more complex shutdown procedures, consider a longer timeout
    /// - For simple servers or development scenarios, the default value is typically sufficient
    /// </remarks>
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The command to execute to start the server process.
    /// </summary>
    /// <remarks>
    /// This property specifies the executable or command that will be run to start the MCP server process.
    /// It is required for stdio transport and typically corresponds to the runtime or interpreter needed
    /// to execute the server code.
    /// 
    /// On Windows, non-shell commands are automatically wrapped with cmd.exe to ensure proper stdio handling.
    /// 
    /// <example>
    /// <code>
    /// // For a .NET server
    /// var transportOptions = new StdioClientTransportOptions
    /// {
    ///     Command = "dotnet",
    ///     Arguments = "run --project QuickstartWeatherServer --no-build"
    /// };
    /// 
    /// // For a Python server
    /// var transportOptions = new StdioClientTransportOptions
    /// {
    ///     Command = "python",
    ///     Arguments = "server.py"
    /// };
    /// 
    /// // For a Node.js server
    /// var transportOptions = new StdioClientTransportOptions
    /// {
    ///     Command = "node",
    ///     Arguments = "server.js"
    /// };
    /// </code>
    /// </example>
    /// </remarks>
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
    /// Arguments to pass to the server process when it is started.
    /// </summary>
    public IList<string>? Arguments { get; set; }

    /// <summary>
    /// Specifies a transport identifier used for logging purposes.
    /// </summary>
    /// <remarks>
    /// These arguments are passed to the process specified in <see cref="Command"/> when the server is launched.
    /// They are typically used to configure the server or specify its behavior.
    /// 
    /// <example>
    /// <code>
    /// var transportOptions = new StdioClientTransportOptions
    /// {
    ///     Command = "dotnet",
    ///     Arguments = "run --project QuickstartWeatherServer --no-build"
    /// };
    /// </code>
    /// </example>
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// The working directory for the server process.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables to set for the server process.
    /// </summary>
    /// <remarks>
    /// This property allows you to specify environment variables that will be set in the server process's
    /// environment. This is useful for passing configuration, authentication information, or runtime flags
    /// to the server without modifying its code.
    /// 
    /// When a server process is started, these environment variables are added to the process's environment.
    /// The server can access these variables using standard environment variable access methods like
    /// <see cref="Environment.GetEnvironmentVariable(string)"/> or <see cref="Environment.GetEnvironmentVariables()"/>.
    /// 
    /// <example>
    /// <code>
    /// // Setting environment variables directly
    /// var transportOptions = new StdioClientTransportOptions
    /// {
    ///     Command = "python",
    ///     Arguments = "server.py",
    ///     EnvironmentVariables = new Dictionary&lt;string, string&gt;
    ///     {
    ///         ["API_KEY"] = "your-api-key",
    ///         ["DEBUG"] = "true",
    ///         ["SERVER_PORT"] = "8080"
    ///     }
    /// };
    /// 
    /// // When using McpClientFactory, you can also set environment variables using the "env:" prefix
    /// await using var mcpClient = await McpClientFactory.CreateAsync(new()
    /// {
    ///     Id = "server-id",
    ///     Name = "Server Name",
    ///     TransportType = TransportTypes.StdIo,
    ///     TransportOptions = new()
    ///     {
    ///         ["command"] = "python",
    ///         ["arguments"] = "server.py",
    ///         ["env:API_KEY"] = "your-api-key",
    ///         ["env:DEBUG"] = "true"
    ///     }
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// The timeout to wait for the server to shut down gracefully.
    /// </summary>
    /// <remarks>
    /// Specifies how long the client should wait for the server process to exit cleanly during shutdown
    /// before forcibly terminating it. This balances between giving the server enough time to clean up 
    /// resources and not hanging indefinitely if a server process becomes unresponsive.
    /// 
    /// <example>
    /// <code>
    /// // Create transport options with a custom shutdown timeout
    /// var transportOptions = new StdioClientTransportOptions
    /// {
    ///     Command = "python",
    ///     Arguments = "server.py",
    ///     ShutdownTimeout = TimeSpan.FromSeconds(10) // Allow 10 seconds for shutdown
    /// };
    /// </code>
    /// </example>
    /// 
    /// This can also be configured through the <c>serverConfig.TransportOptions</c> dictionary with the key "shutdownTimeout".
    /// </remarks>
    public TimeSpan ShutdownTimeout { get; init; } = DefaultShutdownTimeout;
}
