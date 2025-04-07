using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// An empty result object.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class EmptyResult
{
    [JsonIgnore]
    internal static Task<EmptyResult> CompletedTask { get; } = Task.FromResult(new EmptyResult());
}