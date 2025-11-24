using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents execution-related metadata for a tool.
/// </summary>
/// <remarks>
/// This type provides hints about how a tool should be executed, particularly
/// regarding task augmentation support.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ToolExecution
{
    /// <summary>
    /// Gets or sets the level of task augmentation support for this tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property declares whether a tool supports task-augmented execution:
    /// <list type="bullet">
    /// <item><description><see cref="ToolTaskSupport.Forbidden"/>: Clients must not attempt to invoke 
    /// the tool as a task. This is the default behavior.</description></item>
    /// <item><description><see cref="ToolTaskSupport.Optional"/>: Clients may invoke the tool as a task 
    /// or as a normal request.</description></item>
    /// <item><description><see cref="ToolTaskSupport.Required"/>: Clients must invoke the tool as a task.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This is a fine-grained layer in addition to server capabilities. Even if a server's capabilities
    /// include tasks.requests.tools.call, this property controls whether each specific tool supports tasks.
    /// </para>
    /// </remarks>
    [JsonPropertyName("taskSupport")]
    public ToolTaskSupport? TaskSupport { get; set; }
}

/// <summary>
/// Represents the level of task augmentation support for a tool.
/// </summary>
/// <remarks>
/// <para>
/// This enum defines how a tool interacts with the task augmentation system:
/// <list type="bullet">
/// <item><description><see cref="Forbidden"/>: Task augmentation is not allowed (default)</description></item>
/// <item><description><see cref="Optional"/>: Task augmentation is supported but not required</description></item>
/// <item><description><see cref="Required"/>: Task augmentation is mandatory</description></item>
/// </list>
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
[JsonConverter(typeof(JsonStringEnumConverter<ToolTaskSupport>))]
public enum ToolTaskSupport
{
    /// <summary>
    /// Clients must not attempt to invoke the tool as a task.
    /// </summary>
    /// <remarks>
    /// This is the default behavior. Servers should return a -32601 (Method not found) error
    /// if a client attempts to invoke the tool as a task when this is set.
    /// </remarks>
    [JsonStringEnumMemberName("forbidden")]
    Forbidden,

    /// <summary>
    /// Clients may invoke the tool as a task or as a normal request.
    /// </summary>
    /// <remarks>
    /// When this is set, clients can choose whether to use task augmentation based on their needs.
    /// </remarks>
    [JsonStringEnumMemberName("optional")]
    Optional,

    /// <summary>
    /// Clients must invoke the tool as a task.
    /// </summary>
    /// <remarks>
    /// Servers must return a -32601 (Method not found) error if a client does not attempt
    /// to invoke the tool as a task when this is set.
    /// </remarks>
    [JsonStringEnumMemberName("required")]
    Required
}
