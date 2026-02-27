using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

/// <summary>
/// Provides a bag of optional parameters for use with MCP requests.
/// </summary>
public sealed class RequestOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestOptions"/> class.
    /// </summary>
    public RequestOptions()
    {
    }

    /// <summary>Creates a shallow clone of this options instance.</summary>
    /// <returns>A shallow clone of this options instance.</returns>
    internal RequestOptions Clone() => 
        new()
        {
            JsonSerializerOptions = JsonSerializerOptions,
            Meta = Meta,
            ProgressToken = ProgressToken,
        };

    /// <summary>
    /// Gets or sets optional metadata to include as the "_meta" property in a request.
    /// </summary>
    /// <remarks>
    /// Although progress tokens are propagated in MCP "_meta" objects, the <see cref="ProgressToken"/>
    /// property and the <see cref="Meta"/> property do not interact (setting <see cref="Meta"/>
    /// does not affect <see cref="ProgressToken"/>, and the object returned from <see cref="Meta"/>
    /// is not impacted by the value of <see cref="ProgressToken"/>). To get the actual <see cref="JsonObject"/>
    /// that contains state from both <see cref="Meta"/> and <see cref="ProgressToken"/>, use the
    /// <see cref="GetMetaForRequest"/> method.
    /// </remarks>
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Gets or sets an optional progress token to use for tracking long-running operations.
    /// </summary>
    /// <remarks>
    /// Although progress tokens are propagated in MCP "_meta" objects, the <see cref="ProgressToken"/>
    /// property and the <see cref="Meta"/> property do not interact (setting <see cref="ProgressToken"/>
    /// does not affect <see cref="Meta"/>, and getting <see cref="ProgressToken"/> does not read from
    /// <see cref="Meta"/>). To get the actual <see cref="JsonObject"/> that contains state from both
    /// <see cref="Meta"/> and <see cref="ProgressToken"/>, use the <see cref="GetMetaForRequest"/> method.
    /// </remarks>
    public ProgressToken? ProgressToken { get; set; }

    /// <summary>
    /// Gets or sets a <see cref="JsonSerializerOptions"/> to use for any serialization of arguments or results in the request.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.
    /// </remarks>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets a <see cref="JsonObject"/> to use in requests for the "_meta" property.
    /// </summary>
    /// <returns>
    /// A <see cref="JsonObject"/> suitable for use in requests for the "_meta" property.
    /// </returns>
    /// <remarks>
    /// Progress tokens are part of MCP's _meta property. As such, if <see cref="ProgressToken"/>
    /// is non-<see langword="null"/> but <see cref="Meta"/> is <see langword="null"/>, <see cref="GetMetaForRequest"/> will 
    /// manufacture and return a new <see cref="JsonObject"/> instance containing the token. If both <see cref="ProgressToken"/>
    /// and <see cref="Meta"/> are non-<see langword="null"/>, a new clone of <see cref="Meta"/> will be created and its
    /// "progressToken" property overwritten with <see cref="ProgressToken"/>. Otherwise, <see cref="GetMetaForRequest"/>
    /// will just return <see cref="Meta"/>.
    /// </remarks>
    public JsonObject? GetMetaForRequest()
    {
        JsonObject? meta = Meta;
        if (ProgressToken is not null)
        {
            meta = (JsonObject?)meta?.DeepClone() ?? [];
            meta["progressToken"] = ProgressToken.ToString();
        }

        return meta;
    }
}