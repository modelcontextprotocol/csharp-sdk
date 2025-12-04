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
            _meta ??= new JsonObject();
            return _meta;
        }
        set
        {
            // Capture the existing progressToken value if set.
            var progressToken = _meta?["progressToken"];
            if (value is null)
            {
                if (progressToken is not null)
                {
                    _meta = new JsonObject
                    {
                        ["progressToken"] = progressToken,
                    };
                }
                else
                {
                    _meta = null;
                }
            }
            else
            {
                if (value["progressToken"] is null && progressToken is not null) {
                    // Remove existing progressToken so it can be set into the new meta.
                    _meta?.Remove("progressToken");
                    value["progressToken"] = progressToken;
                }
                _meta = value;
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
    public ProgressToken? ProgressToken {
        get
        {
            return _meta?["progressToken"] switch
            {
                JsonValue v when v.TryGetValue(out string? s) => new ProgressToken(s),
                JsonValue v when v.TryGetValue(out long l) => new ProgressToken(l),
                _ => null
            };
        }
        set
        {
            if (value?.Token is {} token)
            {
                _meta ??= new JsonObject();
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