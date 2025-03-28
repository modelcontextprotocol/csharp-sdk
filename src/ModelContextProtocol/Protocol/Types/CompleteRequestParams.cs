namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A request from the client to the server, to ask for completion options.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CompleteRequestParams : RequestParams
{
    /// <summary>
    /// The reference's information
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ref")]
    public required Reference Ref { get; init; }

    /// <summary>
    /// The argument's information
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("argument")]
    public required Argument Argument { get; init; }    
}
