namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Standard JSON-RPC error codes as defined in the MCP specification.
/// </summary>
internal static class ErrorCodes
{
    /// <summary>
    /// Invalid JSON was received by the server. This error code (-32700) indicates that
    /// the JSON syntax itself is malformed and cannot be parsed.
    /// </summary>
    /// <remarks>
    /// Use this error code when the request contains invalid JSON that cannot be parsed by
    /// the JSON parser. This typically happens when there are syntax errors like missing quotes,
    /// invalid escape sequences, unbalanced brackets, or other JSON syntax violations.
    /// This is different from InvalidRequest which indicates that the JSON is valid but
    /// the request structure doesn't conform to the JSON-RPC specification.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Example 1: Creating a JSON-RPC error response for a parse error
    /// var parseError = new JsonRpcError
    /// {
    ///     Id = null, // Usually the ID can't be determined from unparseable JSON
    ///     Error = new JsonRpcErrorDetail
    ///     {
    ///         Code = ErrorCodes.ParseError,
    ///         Message = "The request contains invalid JSON and cannot be parsed",
    ///         Data = JsonNode.Parse($"{{\"receivedText\": \"{receivedText.Substring(0, Math.Min(100, receivedText.Length))}...\"}}")
    ///     }
    /// };
    /// 
    /// // Example 2: Catching a JSON exception and returning the appropriate error
    /// try
    /// {
    ///     var request = JsonSerializer.Deserialize<JsonRpcRequest>(json);
    ///     // Process request
    /// }
    /// catch (JsonException ex)
    /// {
    ///     // Handle invalid JSON
    ///     return new JsonRpcError
    ///     {
    ///         Id = null,
    ///         Error = new JsonRpcErrorDetail
    ///         {
    ///             Code = ErrorCodes.ParseError,
    ///             Message = "Parse error: " + ex.Message
    ///         }
    ///     };
    /// }
    /// </code>
    /// </example>
    public const int ParseError = -32700;

    /// <summary>
    /// The JSON sent is not a valid Request object. This error code (-32600) indicates that
    /// the request structure doesn't conform to the JSON-RPC specification requirements.
    /// </summary>
    /// <remarks>
    /// Use this error code when a request is malformed, such as missing required fields 
    /// (like method or id), containing invalid values, or otherwise not following the 
    /// JSON-RPC protocol structure. This is different from ParseError which indicates
    /// invalid JSON syntax.
    /// </remarks>
    /// <example>
    /// <code>
    /// var error = new JsonRpcError
    /// {
    ///     Id = null, // Often the ID can't be determined from an invalid request
    ///     Error = new JsonRpcErrorDetail
    ///     {
    ///         Code = ErrorCodes.InvalidRequest,
    ///         Message = "The request object is missing required 'method' field",
    ///         Data = JsonNode.Parse(@"{""receivedRequest"": " + JsonSerializer.Serialize(request) + "}")
    ///     }
    /// };
    /// </code>
    /// </example>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// The method does not exist or is not available. This error code (-32601) indicates that
    /// the requested method name in the JSON-RPC request is not recognized by the server.
    /// </summary>
    /// <remarks>
    /// Use this error code when a client requests a method that isn't implemented or available.
    /// This is a standard JSON-RPC error code that helps clients understand that the method
    /// they're trying to call doesn't exist, rather than failing for other reasons.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Example 1: Creating a JSON-RPC error response for a method not found
    /// var methodNotFoundError = new JsonRpcError
    /// {
    ///     Id = request.Id,
    ///     Error = new JsonRpcErrorDetail
    ///     {
    ///         Code = ErrorCodes.MethodNotFound,
    ///         Message = $"Method '{request.Method}' not found"
    ///     }
    /// };
    /// 
    /// // Example 2: Throwing an exception with this error code
    /// throw new McpException("The method does not exist or is not available.", ErrorCodes.MethodNotFound);
    /// 
    /// // Example 3: Handling this specific error in exception handling
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
    /// }
    /// </code>
    /// </example>
    public const int MethodNotFound = -32601;

    /// <summary>
    /// Invalid method parameter(s). This error code (-32602) indicates that the parameters
    /// provided in the request are invalid, missing, or of the wrong type.
    /// </summary>
    /// <remarks>
    /// Use this error code when a request contains parameters that don't conform to the
    /// expected format or when required parameters are missing. This helps clients understand
    /// exactly what's wrong with their request.
    /// </remarks>
    /// <example>
    /// <code>
    /// var error = new JsonRpcError
    /// {
    ///     Id = request.Id,
    ///     Error = new JsonRpcErrorDetail
    ///     {
    ///         Code = ErrorCodes.InvalidParams,
    ///         Message = "Missing required 'location' parameter",
    ///         Data = JsonNode.Parse(@"{""requiredParams"": [""location""]}")
    ///     }
    /// };
    /// </code>
    /// </example>
    public const int InvalidParams = -32602;

    /// <summary>
    /// Internal JSON-RPC error. This error code (-32603) indicates that an internal error occurred
    /// while processing the request that is not covered by more specific error codes.
    /// </summary>
    /// <remarks>
    /// This is often used as a fallback error code when handling unexpected exceptions
    /// or when a more specific error code is not available.
    /// </remarks>
    /// <example>
    /// <code>
    /// var error = new JsonRpcError
    /// {
    ///     Id = request.Id,
    ///     Error = new JsonRpcErrorDetail
    ///     {
    ///         Code = ErrorCodes.InternalError,
    ///         Message = "An internal error occurred while processing the request",
    ///         Data = JsonNode.Parse($"{{\"errorDetails\": \"{ex.Message}\"}}")
    ///     }
    /// };
    /// </code>
    /// </example>
    public const int InternalError = -32603;
}