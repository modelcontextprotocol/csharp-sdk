using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents task support for client request types.
/// </summary>
/// <remarks>
/// Clients can only support task augmentation for <c>sampling/createMessage</c> and
/// <c>elicitation/create</c> requests.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ClientRequestMcpTasksCapability
{
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
/// Represents task support for sampling-related requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class SamplingMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether sampling/createMessage requests support task augmentation.
    /// </summary>
    [JsonPropertyName("createMessage")]
    public CreateMessageMcpTasksCapability? CreateMessage { get; set; }
}

/// <summary>
/// Represents the capability for task-augmented sampling/createMessage requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CreateMessageMcpTasksCapability;

/// <summary>
/// Represents task support for elicitation-related requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ElicitationMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether elicitation/create requests support task augmentation.
    /// </summary>
    [JsonPropertyName("create")]
    public CreateElicitationMcpTasksCapability? Create { get; set; }
}

/// <summary>
/// Represents the capability for task-augmented elicitation/create requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CreateElicitationMcpTasksCapability;
