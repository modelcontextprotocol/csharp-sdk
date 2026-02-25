namespace ModelContextProtocol.Client;

/// <summary>
/// Provides details about the completion of a stdio-based MCP client session.
/// </summary>
public sealed class StdioClientCompletionDetails : ClientCompletionDetails
{
    /// <summary>
    /// Gets the process ID of the server process, or <see langword="null"/> if unavailable.
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// Gets the exit code of the server process, or <see langword="null"/> if unavailable.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Gets the last lines of the server process's standard error output, or <see langword="null"/> if unavailable.
    /// </summary>
    public IReadOnlyList<string>? StandardErrorTail { get; set; }
}
