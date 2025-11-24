using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the tasks capability configuration for servers and clients.
/// </summary>
/// <remarks>
/// <para>
/// The tasks capability enables requestors (clients or servers) to augment their requests with
/// tasks for long-running operations. Tasks are durable state machines that carry information
/// about the underlying execution state of requests.
/// </para>
/// <para>
/// During initialization, both parties exchange their tasks capabilities to establish which
/// operations support task-based execution. Requestors should only augment requests with a
/// task if the corresponding capability has been declared by the receiver.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class McpTasksCapability
{
    /// <summary>
    /// Gets or sets whether this party supports the tasks/list operation.
    /// </summary>
    /// <remarks>
    /// When present, indicates support for listing all tasks.
    /// </remarks>
    [JsonPropertyName("list")]
    public ListMcpTasksCapability? List { get; set; }

    /// <summary>
    /// Gets or sets whether this party supports the tasks/cancel operation.
    /// </summary>
    /// <remarks>
    /// When present, indicates support for cancelling tasks.
    /// </remarks>
    [JsonPropertyName("cancel")]
    public CancelMcpTasksCapability? Cancel { get; set; }

    /// <summary>
    /// Gets or sets which request types support task augmentation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set of capabilities in this property is exhaustive. If a request type is not present,
    /// it does not support task augmentation.
    /// </para>
    /// <para>
    /// For servers, this typically includes tools/call. For clients, this typically includes
    /// sampling/createMessage and elicitation/create.
    /// </para>
    /// </remarks>
    [JsonPropertyName("requests")]
    public RequestMcpTasksCapability? Requests { get; set; }
}

/// <summary>
/// Represents task support for tool-specific requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class RequestMcpTasksCapability
{
    /// <summary>
    /// Gets or sets task support for tool-related requests.
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolsMcpTasksCapability? Tools { get; set; }

    /// <summary>
    /// Gets or sets task support for sampling-related requests.
    /// </summary>
    [JsonPropertyName("sampling")]
    public SamplingMcpTasksCapability? Sampling { get; set; }

    /// <summary>
    /// Gets or sets task support for elicitation-related requests.
    /// </summary>
    [JsonPropertyName("elicitation")]
    public ElicitationMcpTasksCapability? Elicitation { get; set; }
}

/// <summary>
/// Represents task support for tool-related requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ToolsMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether tools/call requests support task augmentation.
    /// </summary>
    /// <remarks>
    /// When present, indicates that the server supports task-augmented tools/call requests.
    /// </remarks>
    [JsonPropertyName("call")]
    public CallToolMcpTasksCapability? Call { get; set; }
}

/// <summary>
/// Represents task support for sampling-related requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class SamplingMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether sampling/createMessage requests support task augmentation.
    /// </summary>
    /// <remarks>
    /// When present, indicates that the client supports task-augmented sampling/createMessage requests.
    /// </remarks>
    [JsonPropertyName("createMessage")]
    public CreateMessageMcpTasksCapability? CreateMessage { get; set; }
}

/// <summary>
/// Represents task support for elicitation-related requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ElicitationMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether elicitation/create requests support task augmentation.
    /// </summary>
    /// <remarks>
    /// When present, indicates that the client supports task-augmented elicitation/create requests.
    /// </remarks>
    [JsonPropertyName("create")]
    public CreateElicitationMcpTasksCapability? Create { get; set; }
}

/// <summary>
/// Represents the capability for listing tasks.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ListMcpTasksCapability;

/// <summary>
/// Represents the capability for cancelling tasks.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CancelMcpTasksCapability;

/// <summary>
/// Represents the capability for task-augmented tools/call requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CallToolMcpTasksCapability;

/// <summary>
/// Represents the capability for task-augmented sampling/createMessage requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CreateMessageMcpTasksCapability;

/// <summary>
/// Represents the capability for task-augmented elicitation/create requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CreateElicitationMcpTasksCapability;
