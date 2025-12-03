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
    /// Optional metadata to include in the request.
    /// </summary>
    private JsonObject? _meta;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestOptions"/> class.
    /// </summary>
    public RequestOptions()
    {
    }

    /// <summary>
    /// Optional metadata to include in the request.
    /// When getting, automatically includes the progress token if set.
    /// </summary>
    public JsonObject? Meta
    {
        get
        {
            if (ProgressToken == null)
            {
                return _meta;
            }

            // Clone existing metadata or create a new one
            var meta = _meta?.DeepClone() as JsonObject ?? new JsonObject();

            // Add progress token to metadata
            meta["progressToken"] = ProgressToken.Value.Token switch
            {
                string s => s,
                long l => l,
                _ => null
            };

            return meta;
        }
        set => _meta = value;
    }

    /// <summary>
    /// The serializer options governing tool parameter serialization. If null, the default options are used.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// The progress token for tracking long-running operations.
    /// </summary>
    public ProgressToken? ProgressToken { get; set; }
}