using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

internal sealed record ToolCallLifecycle(
    CallToolResult? Result,
    Exception? Exception,
    bool CancellationRequested);
