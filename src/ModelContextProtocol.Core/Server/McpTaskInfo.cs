using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the state of a task in an <see cref="IMcpTaskStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the store's representation of a task, decoupled from the MCP protocol wire types.
/// The server infrastructure maps <see cref="McpTaskInfo"/> to the appropriate protocol response
/// types (<see cref="CreateTaskResult"/>, <see cref="GetTaskResult"/>) when communicating with clients.
/// </para>
/// </remarks>
public sealed record McpTaskInfo(
    string TaskId,
    McpTaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    TimeSpan? TimeToLive = null,
    long? PollIntervalMs = null,
    string? StatusMessage = null,
    JsonElement? Result = null,
    JsonElement? Error = null,
    IReadOnlyDictionary<string, InputRequest>? InputRequests = null);
