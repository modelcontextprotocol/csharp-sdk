using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A request from the client to the server, to enable or adjust logging.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class SetLevelRequestParams : RequestParams
{
    /// <summary>
    /// The level of logging that the client wants to receive from the server. 
    /// The server should send all logs at this level and higher (i.e., more severe) to the client as notifications/message.
    /// </summary>
    [JsonPropertyName("level")]
    public required LoggingLevel Level { get; init; }
}
