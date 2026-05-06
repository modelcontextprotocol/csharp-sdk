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
    /// Gets or sets a value indicating whether the server process should inherit the current process's environment variables.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to inherit the current process's environment variables (the default); <see langword="false"/>
    /// to start the server process with an empty environment and only the variables explicitly provided via
    /// <see cref="EnvironmentVariables"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// When <see langword="true"/> (the default), the server process starts with all of the current process's environment
    /// variables. Any entries in <see cref="EnvironmentVariables"/> are then applied on top, adding or overwriting inherited
    /// variables.
    /// </para>
    /// <para>
    /// When <see langword="false"/>, the server process starts with a completely empty environment. The <see cref="EnvironmentVariables"/>
    /// dictionary is the sole source of environment variables for the child process. This is useful when you want to minimize
    /// the attack surface by preventing credentials, tokens, proxy settings, and other sensitive values present in the current
    /// environment from unintentionally reaching the child process.
    /// </para>
    /// <para>
    /// <strong>Security consideration:</strong> Inheriting environment variables (the default) can unintentionally expose
    /// sensitive values to the child process. Variables such as <c>AWS_SECRET_ACCESS_KEY</c>, <c>GITHUB_TOKEN</c>,
    /// <c>OPENAI_API_KEY</c>, and similar credentials that are present in the parent process will automatically flow into
    /// the server process, which may be undesirable when running third-party or untrusted MCP servers.
    /// </para>
    /// <para>
    /// <strong>Compatibility consideration:</strong> Disabling inheritance can cause the child process to fail to start or
    /// behave unexpectedly if it relies on variables provided by the operating system or the user's shell environment. Common
    /// examples include <c>PATH</c> (required to locate executables), <c>HOME</c> (required by many tools on Unix),
    /// <c>DOTNET_ROOT</c>, <c>LD_LIBRARY_PATH</c>, <c>JAVA_HOME</c>, and proxy settings (<c>HTTP_PROXY</c>,
    /// <c>HTTPS_PROXY</c>, <c>NO_PROXY</c>). When disabling inheritance, ensure that all variables required by the server
    /// process are explicitly provided via <see cref="EnvironmentVariables"/>.
    /// </para>
    /// </remarks>
    public bool InheritEnvironmentVariables { get; set; } = true;

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
    /// When <see cref="InheritEnvironmentVariables"/> is <see langword="true"/> (the default), the server process starts with
    /// all environment variables inherited from the current process. The entries in this <see cref="EnvironmentVariables"/>
    /// dictionary are then applied on top: adding new variables, overwriting inherited ones, or removing variables whose
    /// value is set to <see langword="null"/>.
    /// </para>
    /// <para>
    /// When <see cref="InheritEnvironmentVariables"/> is <see langword="false"/>, the server process starts with an empty
    /// environment. This dictionary is the sole source of environment variables for the child process.
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
