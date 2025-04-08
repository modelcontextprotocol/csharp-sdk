namespace ModelContextProtocol;

/// <summary>
/// An exception that is thrown when a Model Context Protocol (MCP) error occurs. This includes configuration errors,
/// protocol errors, validation failures, timeouts, and other issues specific to MCP client-server communication.
/// </summary>
/// <remarks>
/// Common scenarios where this exception is thrown include:
/// <list type="bullet">
/// <item><description>Missing required arguments in API calls</description></item>
/// <item><description>Protocol version mismatches between client and server</description></item>
/// <item><description>Timeouts during initialization or communication</description></item>
/// <item><description>Unknown tools, prompts, or resources being requested</description></item>
/// <item><description>Server-side errors during request processing</description></item>
/// <item><description>Configuration errors when setting up MCP clients or servers</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Example of throwing an McpException for a missing required parameter
/// if (request.Params?.Level is null)
/// {
///     throw new McpException("Missing required argument 'level'");
/// }
/// 
/// // Example of throwing an McpException for a protocol version mismatch
/// if (initializeResponse.ProtocolVersion != expectedVersion)
/// {
///     throw new McpException($"Server protocol version mismatch. Expected {expectedVersion}, got {initializeResponse.ProtocolVersion}");
/// }
/// </code>
/// </example>
public class McpException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class.
    /// </summary>
    public McpException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public McpException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public McpException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message and JSON-RPC error code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="errorCode">A JSON-RPC error code from <see cref="Protocol.Messages.ErrorCodes"/> class.</param>
    /// <remarks>
    /// Use this constructor when you need to specify a standard JSON-RPC error code for protocol-related errors
    /// but don't have an inner exception to include.
    /// </remarks>
    /// <example>
    /// <code>
    /// throw new McpException("The method does not exist or is not available.", ErrorCodes.MethodNotFound);
    /// </code>
    /// </example>
    public McpException(string message, int? errorCode) : this(message, null, errorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message, inner exception, and JSON-RPC error code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    /// <param name="errorCode">A JSON-RPC error code from <see cref="Protocol.Messages.ErrorCodes"/> class.</param>
    /// <remarks>
    /// This is the most complete constructor, allowing you to specify both an underlying cause exception and a standard 
    /// JSON-RPC error code. Use this when wrapping lower-level exceptions while maintaining protocol compliance.
    /// </remarks>
    /// <example>
    /// <code>
    /// try
    /// {
    ///     // Some operation that may throw
    ///     resource = await FetchResourceAsync(uri);
    /// }
    /// catch (Exception ex)
    /// {
    ///     throw new McpException("Invalid resource URI", ex, ErrorCodes.InvalidParams);
    /// }
    /// </code>
    /// </example>
    public McpException(string message, Exception? innerException, int? errorCode) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the JSON-RPC error code associated with this exception.
    /// </summary>
    /// <value>
    /// A standard JSON-RPC error code or null if the exception wasn't caused by a JSON-RPC error.
    /// </value>
    /// <remarks>
    /// This property contains a standard JSON-RPC error code as defined in the MCP specification
    /// and the <see cref="Protocol.Messages.ErrorCodes"/> class. Common error codes include:
    /// <list type="bullet">
    /// <item><description>-32700: Parse error - Invalid JSON received</description></item>
    /// <item><description>-32600: Invalid request - The JSON is not a valid Request object</description></item>
    /// <item><description>-32601: Method not found - The method does not exist or is not available</description></item>
    /// <item><description>-32602: Invalid params - Invalid method parameters</description></item>
    /// <item><description>-32603: Internal error - Internal JSON-RPC error</description></item>
    /// </list>
    /// This value will be null if the exception wasn't created with an error code.
    /// 
    /// When handling McpExceptions, you can check this property to determine the specific 
    /// protocol-level error that occurred and handle different error types accordingly.
    /// </remarks>
    /// <example>
    /// <code>
    /// try
    /// {
    ///     await client.CallToolAsync("nonExistentTool", new { });
    /// }
    /// catch (McpException ex)
    /// {
    ///     if (ex.ErrorCode == ErrorCodes.MethodNotFound)
    ///     {
    ///         // Handle tool not found error specifically
    ///         Console.WriteLine("Tool not found. Available tools: " + string.Join(", ", await client.ListToolsAsync()));
    ///     }
    ///     else
    ///     {
    ///         // Handle other errors
    ///         Console.WriteLine($"Error calling tool: {ex.Message}");
    ///     }
    /// }
    /// </code>
    /// </example>
    public int? ErrorCode { get; }
}