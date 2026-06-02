using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the tasks capability configuration for clients.
/// </summary>
/// <remarks>
/// <para>
/// The tasks capability enables servers to augment their requests with tasks for long-running
/// operations. Tasks are durable state machines that carry information about the underlying
/// execution state of requests.
/// </para>
/// <para>
/// During initialization, both parties exchange their tasks capabilities to establish which
/// operations support task-based execution. Requestors should only augment requests with a
/// task if the corresponding capability has been declared by the receiver.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ClientMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether this client supports the tasks/list operation.
    /// </summary>
    [JsonPropertyName("list")]
    public ListMcpTasksCapability? List { get; set; }

    /// <summary>
    /// Gets or sets whether this client supports the tasks/cancel operation.
    /// </summary>
    [JsonPropertyName("cancel")]
    public CancelMcpTasksCapability? Cancel { get; set; }

    /// <summary>
    /// Gets or sets which client request types support task augmentation.
    /// </summary>
    /// <remarks>
    /// For clients, this includes <c>tasks.requests.sampling.createMessage</c> and
    /// <c>tasks.requests.elicitation.create</c>.
    /// </remarks>
    [JsonPropertyName("requests")]
    public ClientRequestMcpTasksCapability? Requests { get; set; }
}
