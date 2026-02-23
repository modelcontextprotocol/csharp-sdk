using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a tasks/list request to retrieve a list of tasks.
/// </summary>
/// <remarks>
/// This operation supports cursor-based pagination. Receivers should use cursor-based
/// pagination to limit the number of tasks returned in a single response.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ListTasksRequestParams : PaginatedRequestParams
{
    // Inherits Cursor property from PaginatedRequestParams
}

/// <summary>
/// Represents the result of a tasks/list request.
/// </summary>
/// <remarks>
/// The result contains an array of task objects and an optional cursor for pagination.
/// If <see cref="PaginatedResult.NextCursor"/> is present, more tasks are available.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ListTasksResult : PaginatedResult
{
    /// <summary>
    /// Gets or sets the list of tasks.
    /// </summary>
    [JsonPropertyName("tasks")]
    public required IList<McpTask> Tasks { get; set; }
}
