using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents an empty result object for operations that need to indicate successful completion 
/// but don't need to return any specific data.
/// </summary>
public sealed class EmptyResult : Result
{
    [JsonIgnore]
    internal static EmptyResult Instance { get; } = new();
}