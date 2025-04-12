using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents an empty result object for operations that need to indicate successful completion 
/// but don't need to return any specific data.
/// </summary>
public class EmptyResult
{
    [JsonIgnore]
    internal static EmptyResult Instance { get; } = new();
}