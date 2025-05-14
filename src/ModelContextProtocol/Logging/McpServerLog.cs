namespace ModelContextProtocol.Logging;

/// <summary>
/// Delegate that allows receivign log details about request/response calls. 
/// </summary>
public delegate void McpLogHandler(McpLogContext context);

/// <summary>
/// Class that provides context details about a given call, for logging purposes.
/// </summary>
public sealed class McpLogContext
{
    /// <summary>
    /// Gets <see cref="McpStatus"/> information about when this log was emitted.
    /// </summary>
    public McpStatus Status { get; init; }
    
    /// <summary>
    /// Gets information about the JSON message that was received/sent when this event happened.
    /// </summary>
    public required string Json { get; init; }
    
    /// <summary>
    /// If applicable, gets the <see cref="Exception"/> that happened when this message was processed.
    /// </summary>
    /// <remarks>This property is commonly associated with a <see cref="McpStatus.ErrorOccurred"/> status.</remarks>
    public Exception? Exception { get; init; }
    
    /// <summary>
    /// Gets information about the method that was called.
    /// </summary>
    public string? Method { get; init; }
    
    /// <summary>
    /// Gets a <see cref="IServiceProvider"/> instance that allows you accessing instances registered for the application.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }
}

/// <summary>
/// Enum that defines possible events that might happen during a MCP messaging workflow.
/// </summary>
public enum McpStatus
{
    /// <summary>
    /// Specifies that the given message was received from a MCP Client.
    /// </summary>
    RequestReceived,
    
    /// <summary>
    /// Specifies that the MCP Server just sent this message back to the Client.
    /// </summary>
    ResponseSent,
    
    /// <summary>
    /// Specifies that an exception happened when trying to process a given request.
    /// </summary>
    ErrorOccurred
}