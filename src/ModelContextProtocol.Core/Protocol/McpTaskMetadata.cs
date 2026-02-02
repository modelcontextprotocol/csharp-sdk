using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents metadata for augmenting a request with task execution.
/// </summary>
/// <remarks>
/// <para>
/// When included in a request's params, this metadata signals that the requestor
/// wants the receiver to execute the request as a task rather than synchronously.
/// The receiver will return a <see cref="Result"/> containing task data
/// instead of the actual operation result.
/// </para>
/// <para>
/// Requestors can specify a desired TTL (time-to-live) duration for the task,
/// though receivers may override this value based on their resource management policies.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class McpTaskMetadata
{
    /// <summary>
    /// Gets or sets the requested time to live (retention duration) to retain the task from creation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a hint to the receiver about how long the requestor expects to need access
    /// to the task data. Receivers may override this value based on their resource constraints
    /// and policies.
    /// </para>
    /// <para>
    /// A null value indicates no specific retention requirement. The actual TTL used by the
    /// receiver will be returned in the <see cref="McpTask.TimeToLive"/> property.
    /// </para>
    /// </remarks>
    [JsonPropertyName("ttl")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }
}
