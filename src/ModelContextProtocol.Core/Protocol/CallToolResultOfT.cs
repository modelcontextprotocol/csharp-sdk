using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a strongly-typed result of a <see cref="RequestMethods.ToolsCall"/> request.
/// </summary>
/// <typeparam name="T">
/// The type of the structured content returned by the tool. This type is used to infer the
/// <see cref="Tool.OutputSchema"/> advertised by the tool.
/// </typeparam>
/// <remarks>
/// <para>
/// <see cref="CallToolResult{T}"/> provides a way to return strongly-typed structured content from a tool
/// while still providing access to <see cref="Result.Meta"/> and <see cref="IsError"/>. When a tool method
/// returns <see cref="CallToolResult{T}"/>, the SDK uses <typeparamref name="T"/> to infer the output schema
/// and serializes <see cref="Content"/> as both the text content and structured content of the response.
/// </para>
/// <para>
/// This type is a peer of <see cref="CallToolResult"/>, not a subclass. Use <see cref="CallToolResult"/> when
/// you need full control over individual content blocks, and <see cref="CallToolResult{T}"/> when you want
/// the SDK to handle serialization of a strongly-typed result.
/// </para>
/// </remarks>
public sealed class CallToolResult<T> : ICallToolResultTyped
{
    /// <summary>
    /// Gets or sets the typed content returned by the tool.
    /// </summary>
    public T? Content { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the tool call was unsuccessful.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to signify that the tool execution failed; <see langword="false"/> if it was successful.
    /// </value>
    /// <remarks>
    /// <para>
    /// Tool execution errors (including input validation errors, API failures, and business logic errors)
    /// are reported with this property set to <see langword="true"/> and details in the <see cref="Content"/>
    /// property, rather than as protocol-level errors.
    /// </para>
    /// <para>
    /// This design allows language models to receive detailed error feedback and potentially self-correct
    /// in subsequent requests.
    /// </para>
    /// </remarks>
    public bool? IsError { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    public JsonObject? Meta { get; set; }

    /// <inheritdoc/>
    CallToolResult ICallToolResultTyped.ToCallToolResult(JsonSerializerOptions serializerOptions)
    {
        JsonNode? structuredContent = JsonSerializer.SerializeToNode(Content, serializerOptions.GetTypeInfo(typeof(T)));

        return new()
        {
            Content = [new TextContentBlock { Text = structuredContent?.ToString() ?? "null" }],
            StructuredContent = structuredContent,
            IsError = IsError,
            Meta = Meta,
        };
    }
}

/// <summary>
/// Internal interface for converting strongly-typed tool results to <see cref="CallToolResult"/>.
/// </summary>
internal interface ICallToolResultTyped
{
    /// <summary>
    /// Converts the strongly-typed result to a <see cref="CallToolResult"/>.
    /// </summary>
    CallToolResult ToCallToolResult(JsonSerializerOptions serializerOptions);
}
