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
        get => _meta ??= [];
        set
        {
            // Capture the existing progressToken value if set.
            var existingProgressToken = _meta?["progressToken"];

            if (value is not null)
            {
                if (existingProgressToken is not null)
                {
                    value["progressToken"] ??= existingProgressToken;
                }

                _meta = value;
            }
            else if (existingProgressToken is not null)
            {
                _meta = new()
                {
                    ["progressToken"] = existingProgressToken,
                };
            }
            else
            {
                _meta = null;
            }
        }
    }

    /// <summary>
    /// The serializer options governing tool parameter serialization. If null, the default options are used.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// The progress token for tracking long-running operations.
    /// </summary>
    public ProgressToken? ProgressToken
    {
        get
        {
            return _meta?["progressToken"] switch
            {
                JsonValue v when v.TryGetValue(out string? s) => new(s),
                JsonValue v when v.TryGetValue(out long l) => new(l),
                _ => null
            };
        }
        set
        {
            if (value?.Token is { } token)
            {
                _meta ??= [];
                _meta["progressToken"] = token switch
                {
                    string s => s,
                    long l => l,
                    _ => throw new InvalidOperationException("ProgressToken must be a string or long"),
                };
            }
            else
            {
                _meta?.Remove("progressToken");
            }
        }
    }
}