namespace ModelContextProtocol.Server;

/// <summary>
/// Provides ambient context when a tool is executing as a background task.
/// When established, calls to <see cref="McpServer.ElicitAsync(ModelContextProtocol.Protocol.ElicitRequestParams, CancellationToken)"/>,
/// <see cref="McpServer.SampleAsync(ModelContextProtocol.Protocol.CreateMessageRequestParams, CancellationToken)"/>,
/// and <see cref="McpServer.RequestRootsAsync(ModelContextProtocol.Protocol.ListRootsRequestParams, CancellationToken)"/>
/// are redirected through the task store as input requests rather than sent directly to the client.
/// </summary>
internal sealed class McpTaskExecutionContext
{
    internal static readonly AsyncLocal<McpTaskExecutionContext?> Current = new();

    public required string TaskId { get; init; }
    public required IMcpTaskStore Store { get; init; }

    internal sealed class Scope(McpTaskExecutionContext? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
