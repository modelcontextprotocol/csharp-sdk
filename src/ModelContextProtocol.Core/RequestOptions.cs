using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

/// <summary>
/// Contains optional parameters for MCP requests.
/// </summary>
public sealed class RequestOptions
{
    /// <summary>
    /// Gets or sets optional metadata to include in the request.
    /// </summary>
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options to use for serialization and deserialization.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the progress token for tracking long-running operations.
    /// </summary>
    public ProgressToken? ProgressToken { get; set; }

    /// <summary>
    /// Gets a default instance with all properties set to null.
    /// </summary>
    public static RequestOptions Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestOptions"/> class.
    /// </summary>
    public RequestOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestOptions"/> class with the specified metadata.
    /// </summary>
    /// <param name="meta">Optional metadata to include in the request.</param>
    public RequestOptions(JsonObject? meta)
    {
        Meta = meta;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestOptions"/> class with the specified options.
    /// </summary>
    /// <param name="meta">Optional metadata to include in the request.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use.</param>
    /// <param name="progressToken">The progress token for tracking operations.</param>
    public RequestOptions(JsonObject? meta = null, JsonSerializerOptions? jsonSerializerOptions = null, ProgressToken? progressToken = null)
    {
        Meta = meta;
        JsonSerializerOptions = jsonSerializerOptions;
        ProgressToken = progressToken;
    }
}