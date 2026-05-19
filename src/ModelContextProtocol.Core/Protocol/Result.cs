using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for result payloads.
/// </summary>
public abstract class Result
{
    /// <summary>Prevent external derivations.</summary>
    private protected Result()
    {
    }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Gets or sets the result type discriminator used to distinguish polymorphic results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standard results use <c>"complete"</c> (or omit this field). When a server returns a task
    /// in lieu of a standard result, it sets this to <c>"task"</c>.
    /// </para>
    /// <para>
    /// See <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2322-MRTR.md">SEP-2322</see>
    /// for the introduction of this field, and
    /// <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
    /// for the <c>"task"</c> discriminator value.
    /// </para>
    /// </remarks>
    [JsonPropertyName("resultType")]
    public string? ResultType { get; set; }
}
