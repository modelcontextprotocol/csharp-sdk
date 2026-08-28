namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// The default <see cref="IMcpTaskExecutor"/> that runs the tool invocation pipeline
/// in-process on the .NET thread pool.
/// </summary>
/// <remarks>
/// This is the executor used by <c>WithTasks</c> when no custom executor is configured,
/// and the base behavior a custom executor can fall back to via
/// <see cref="McpTaskExecutionContext.RunToolPipelineAsync"/>. It is exposed as a type so
/// decorators can identify or wrap the default, but it cannot be constructed externally;
/// use <see cref="Instance"/>.
/// </remarks>
public sealed class ProcessLocalMcpTaskExecutor : IMcpTaskExecutor
{
    private ProcessLocalMcpTaskExecutor()
    {
    }

    /// <summary>
    /// Gets the singleton instance of the process-local executor.
    /// </summary>
    public static ProcessLocalMcpTaskExecutor Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask StartAsync(McpTaskExecutionContext context, CancellationToken cancellationToken)
    {
#if NET
        ArgumentNullException.ThrowIfNull(context);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif

        _ = Task.Run(
            () => context.RunToolPipelineAsync(context.CancellationToken).AsTask(),
            CancellationToken.None);

        return default;
    }
}
